using System;
using System.Collections.Generic;
using System.Globalization;
using Template.Core.Eventing;
using Template.Core.Logging;
using Template.Core.Services;
using UnityEngine;

namespace Template.Settings
{
    /// <summary>
    /// 设置服务：全局设置的**唯一出口与存档载体**（音量归音频管、键位归输入管 —— 但它们的持久化都经这里）。
    ///
    /// 数据模型：键值对（键 = <see cref="SettingsKey{T}"/>），文件里就是一行行
    /// "audio.bgm.volume": { Value: "0.3", Type: "float" } —— 人类可读、可手工修、
    /// 新增/删除设置项不动存储格式（这是"设置项级可删性"的关键）。
    ///
    /// 用法（消费方模块）：
    ///     1) 声明键：public static readonly SettingsKey&lt;float&gt; MasterVolume = new("audio.master.volume", 1f);
    ///     2) Init 时拉取初值：_volumes[i] = settings.Get(MasterVolume);
    ///     3) 订阅变更：EventBus&lt;SettingsChangedEvent&lt;float&gt;&gt;.Subscribe(OnChanged);
    ///     4) 用户改动时写入：settings.Set(MasterVolume, 0.8f);
    ///   拉取 + 订阅两者都要 —— 只订阅会漏掉"本模块 Init 晚于他人写入"的初值，只拉取则收不到后续变更。
    ///
    /// 加载：**首次访问即加载**（懒加载）—— 于是 Init 顺序无关紧要，任何服务在自己的 Init 里
    /// Get 都能拿到已加载的值，无需人工保证 Settings 先 Init。
    ///
    /// 写盘：Set 只标脏，由 Tick 延迟合并（滑条连续拖动只在停手后落一次盘），
    /// Dispose（退出 Play / 换服务）时兜底落盘 —— 改完立刻按 Stop 也不会丢。
    ///
    /// 可整删性：本模块是**可选依赖**。音频/输入用 ServiceLocator.TryGet 解析它，
    /// 删掉本模块后它们自动降级为"设置只在本次运行内生效、不持久化"，其余功能不受影响。
    /// </summary>
    public sealed class SettingsService : IGameService, ITickable
    {
        /// <summary>设置文件格式版本。结构发生不兼容变更时 +1，并在 EnsureLoaded 中补一段迁移。</summary>
        private const int FileVersion = 1;

        /// <summary>变更后延迟写盘的秒数（unscaled）：吸收滑条拖动期间的高频写入。</summary>
        private const float FlushDelay = 0.6f;

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private readonly ISettingsStorage _storage;

        private bool _loaded;      // 懒加载标记（首次 Get/Set 触发）
        private bool _dirty;       // 有未落盘的变更
        private float _dirtySince; // 最后一次变更的时刻（unscaled）

        // 自上次落盘以来变更过的键（去重）。注意**不在 Set 里逐条打日志**：
        // 滑条拖动会每帧写入，逐条日志会把 Console 刷爆（每条还带堆栈）。
        // 改成落盘时一次性报告"改了哪些键"，信息更全、噪音更低。
        private readonly HashSet<string> _dirtyKeys = new HashSet<string>();

        /// <summary>内存中的设置项数量（不含未写入的默认值）。</summary>
        public int Count => _entries.Count;

        public SettingsService() : this(new JsonFileSettingsStorage()) { }

        /// <summary>可注入存储（测试用内存实现 / 项目换云存档实现）。</summary>
        public SettingsService(ISettingsStorage storage)
        {
            _storage = storage ?? new JsonFileSettingsStorage();
        }

        public void Init()
        {
            EnsureLoaded(); // 提前加载：让启动日志里能看到"设置已载入 N 项"，也便于验收
            Log.Info("Settings", "设置系统就绪：{0} 项已载入（{1}）", _entries.Count, DescribeStorage());
        }

        public void Dispose()
        {
            Flush(); // 兜底：改完设置立刻按 Stop 也不丢（Tick 可能来不及跑）
        }

        /// <summary>读取设置项；文件里没有（或类型不可解析）时返回该键的默认值。</summary>
        public T Get<T>(SettingsKey<T> key)
        {
            EnsureLoaded();
            if (_entries.TryGetValue(key.Id, out Entry entry) && TryParse(entry.Value, out T value))
                return value;
            return key.Default;
        }

        /// <summary>
        /// 写入设置项：值有变化才落盘并广播 <see cref="SettingsChangedEvent{T}"/>（无变化返回 false）。
        /// 注意本方法**不负责把值作用到引擎**（那是各模块订阅者的职责，见类注释）。
        /// </summary>
        public bool Set<T>(SettingsKey<T> key, T value)
        {
            EnsureLoaded();

            string raw = Serialize(value);
            string type = TypeTagOf<T>();
            if (_entries.TryGetValue(key.Id, out Entry current)
                && string.Equals(current.Value, raw, StringComparison.Ordinal)
                && string.Equals(current.Type, type, StringComparison.Ordinal))
                return false; // 值没变：不写盘、不广播（滑条每帧回调也不会产生噪音）

            _entries[key.Id] = new Entry { Value = raw, Type = type };
            _dirty = true;
            _dirtySince = Time.unscaledTime;
            _dirtyKeys.Add(key.Id);

            EventBus<SettingsChangedEvent<T>>.Publish(new SettingsChangedEvent<T>(key, value));
            return true;
        }

        /// <summary>把设置项恢复为默认值（等价于写入该键的 Default，同样会广播）。</summary>
        public bool Reset<T>(SettingsKey<T> key) => Set(key, key.Default);

        /// <summary>立即写盘（正常由 Tick 延迟合并；切换场景 / 主动保存 / Dispose 前可显式调用）。</summary>
        public void Flush()
        {
            if (!_dirty)
                return;

            _dirty = false;

            var file = new SettingsFileDto { Version = FileVersion };
            foreach (KeyValuePair<string, Entry> pair in _entries)
            {
                file.Entries.Add(new EntryDto
                {
                    Key = pair.Key,
                    Value = pair.Value.Value,
                    Type = pair.Value.Type,
                });
            }
            _storage.Save(JsonUtility.ToJson(file, true));

            Log.Info("Settings", "设置已保存：{0} 项变更 [{1}] → {2}",
                _dirtyKeys.Count, string.Join(", ", _dirtyKeys), DescribeStorage());
            _dirtyKeys.Clear();
        }

        /// <summary>有未落盘变更时延迟合并写盘（Bootstrap 每帧驱动）。</summary>
        public void Tick()
        {
            if (!_dirty || Time.unscaledTime - _dirtySince < FlushDelay)
                return;
            Flush();
        }

        // ── 加载 ──

        private void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true; // 先置位：加载过程若被重入（不应发生）也不会递归

            string content = _storage.Load();
            if (string.IsNullOrEmpty(content))
            {
                Log.Info("Settings", "首次运行：尚无设置文件，全部使用各模块声明的默认值");
                return;
            }

            SettingsFileDto file;
            try
            {
                file = JsonUtility.FromJson<SettingsFileDto>(content);
            }
            catch (Exception ex)
            {
                // 损坏的设置文件不阻断启动：全用默认值，直到玩家下次改动才被覆盖（设置不像存档那样不可再生）
                Log.Error("Settings", "设置文件解析失败（本次运行改用默认值）: {0}", ex.Message);
                return;
            }
            if (file == null)
                return;

            if (file.Version > FileVersion)
                Log.Warn("Settings", "设置文件版本 {0} 高于程序支持的 {1}（降级运行？）——只读取能识别的项",
                    file.Version, FileVersion);
            // 版本迁移链入口：file.Version < FileVersion 时在此逐级迁移（阶段 3 存档系统同一套路）

            if (file.Entries == null)
                return;
            for (int i = 0; i < file.Entries.Count; i++)
            {
                EntryDto dto = file.Entries[i];
                if (dto == null || string.IsNullOrEmpty(dto.Key))
                    continue;
                _entries[dto.Key] = new Entry
                {
                    Value = dto.Value ?? string.Empty,
                    Type = dto.Type ?? string.Empty,
                };
            }
            Log.Info("Settings", "设置已载入：{0} 项（文件版本 {1}）", _entries.Count, file.Version);
        }

        // ── 值的字符串化（一律用 InvariantCulture：小数点不能跟着系统区域设置变） ──

        private static string Serialize<T>(T value)
        {
            if (typeof(T) == typeof(string))
                return (string)(object)value ?? string.Empty;
            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            return value?.ToString() ?? string.Empty;
        }

        private static bool TryParse<T>(string raw, out T value)
        {
            value = default;
            if (typeof(T) == typeof(float))
            {
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                    return false;
                value = (T)(object)f;
                return true;
            }
            if (typeof(T) == typeof(int))
            {
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                    return false;
                value = (T)(object)i;
                return true;
            }
            if (typeof(T) == typeof(bool))
            {
                if (!bool.TryParse(raw, out bool b))
                    return false;
                value = (T)(object)b;
                return true;
            }
            if (typeof(T) == typeof(string))
            {
                value = (T)(object)(raw ?? string.Empty);
                return true;
            }
            Log.Warn("Settings", "不支持的设置类型 <{0}>：仅支持 float / int / bool / string", typeof(T).Name);
            return false;
        }

        private static string TypeTagOf<T>()
        {
            if (typeof(T) == typeof(float)) return "float";
            if (typeof(T) == typeof(int)) return "int";
            if (typeof(T) == typeof(bool)) return "bool";
            if (typeof(T) == typeof(string)) return "string";
            return typeof(T).Name;
        }

        private string DescribeStorage() => _storage is JsonFileSettingsStorage file ? file.Path : _storage.GetType().Name;

        private struct Entry
        {
            public string Value;
            public string Type;
        }
    }

    /// <summary>设置文件根对象（JsonUtility 要求字段公开、类型标 [Serializable]）。</summary>
    [Serializable]
    internal sealed class SettingsFileDto
    {
        public int Version;
        public List<EntryDto> Entries = new List<EntryDto>();
    }

    /// <summary>设置文件中的一项：值一律以字符串存放，Type 只作自描述（便于人工排查与类型变更检测）。</summary>
    [Serializable]
    internal sealed class EntryDto
    {
        public string Key;
        public string Value;
        public string Type;
    }
}
