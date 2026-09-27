using System;
using System.Collections.Generic;
using Template.Core.Logging;
using Template.Core.Services;

namespace Template.Save
{
    /// <summary>
    /// 存档服务：多槽位保存/读取 + 版本迁移 + 损坏降级。
    ///
    /// 设计要点：
    /// <list type="bullet">
    /// <item>**不认识任何业务数据**：内容来自各系统注册的 <see cref="ISaveSection"/>（背包/任务/成就各存各的）；</item>
    /// <item>**版本迁移链**：读档时从存档版本逐级跑到 <see cref="CurrentVersion"/>，缺哪一级就明确报错；</item>
    /// <item>**损坏降级**：主档解析失败 → 回退上一次的备份档（<c>.bak</c>）→ 仍失败则返回失败并说明原因，绝不白屏；</item>
    /// <item>存储与序列化都可替换（<see cref="ISaveStorage"/> / <see cref="ISaveSerializer"/>），
    /// 换云存档或 MessagePack 不动本类与业务代码。</item>
    /// </list>
    ///
    /// 用法：
    /// <code>
    /// var save = ServiceLocator.Get&lt;SaveService&gt;();
    /// save.RegisterSection(myInventorySection);     // Boot 阶段注册
    /// save.Save(0, "第 3 章 · 12 分钟");             // 存到槽位 0
    /// save.Load(0, out string error);                // 读档（error 非空表示失败/降级）
    /// </code>
    /// </summary>
    public sealed class SaveService : IGameService
    {
        /// <summary>
        /// 当前存档格式版本。**结构变更时 +1，并补一条 <see cref="ISaveMigration"/> 处理老档**
        /// （只加字段不改结构的话不必升版本：JsonUtility 缺字段会保留默认值）。
        /// </summary>
        public const int CurrentVersion = 1;

        private readonly ISaveStorage _storage;
        private readonly ISaveSerializer _serializer;
        private readonly SaveMigrator _migrator;
        private readonly List<ISaveSection> _sections = new List<ISaveSection>();

        /// <summary>存档目录/存储描述（日志与排查用）。</summary>
        public string StorageDescription => _storage is FileSaveStorage file ? file.Root : _storage.GetType().Name;

        public SaveService() : this(new FileSaveStorage(), new JsonSaveSerializer()) { }

        public SaveService(ISaveStorage storage, ISaveSerializer serializer)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _migrator = new SaveMigrator(CurrentVersion);
        }

        public void Init()
        {
            Log.Info("Save", "存档系统就绪：格式 v{0}，{1} 个分片，{2} 条迁移，目录 {3}",
                CurrentVersion, _sections.Count, _migrator.Count, StorageDescription);
        }

        public void Dispose()
        {
            _sections.Clear();
        }

        // ── 注册 ──

        /// <summary>注册数据分片（Boot 阶段调用；键重复会被拒绝）。</summary>
        public void RegisterSection(ISaveSection section)
        {
            if (section == null || string.IsNullOrEmpty(section.Key))
            {
                Log.Error("Save", "RegisterSection：分片或键为空");
                return;
            }
            for (int i = 0; i < _sections.Count; i++)
            {
                if (_sections[i].Key == section.Key)
                {
                    Log.Error("Save", "RegisterSection：分片键重复 '{0}'（每段数据只能有一个所有者）", section.Key);
                    return;
                }
            }
            _sections.Add(section);
            Log.Verbose("Save", "已注册存档分片 '{0}'（当前 {1} 个）", section.Key, _sections.Count);
        }

        /// <summary>注册一条迁移（加新版本时在 Boot 阶段补齐；迁移链的启动逻辑见 <see cref="SaveMigrator"/>）。</summary>
        public bool RegisterMigration(ISaveMigration migration) => _migrator.Register(migration);

        // ── 槽位 ──

        public int[] ListSlots() => _storage.ListSlots();

        public bool SlotExists(int slot) => _storage.Exists(slot);

        public bool Delete(int slot)
        {
            if (!_storage.Exists(slot))
                return false;
            _storage.Delete(slot);
            Log.Info("Save", "已删除槽位 {0} 的存档", slot);
            return true;
        }

        /// <summary>读取槽位概览（存档列表界面用）；读不到返回 false。</summary>
        public bool TryGetSlotInfo(int slot, out SaveSlotInfo info)
        {
            info = default;
            if (!TryReadData(slot, out SaveData data, out _))
                return false;
            info = new SaveSlotInfo(slot, data);
            return true;
        }

        // ── 保存 ──

        /// <summary>收集所有分片并写入槽位。<paramref name="summary"/> 显示在存档列表里。</summary>
        public bool Save(int slot, string summary)
        {
            var data = new SaveData
            {
                Version = CurrentVersion,
                Meta = new SaveMeta
                {
                    SavedAtUtcTicks = DateTime.UtcNow.Ticks,
                    Summary = summary ?? string.Empty,
                    AppVersion = UnityEngine.Application.version,
                },
            };

            for (int i = 0; i < _sections.Count; i++)
            {
                ISaveSection section = _sections[i];
                try
                {
                    data.SetSection(section.Key, section.Capture(_serializer));
                }
                catch (Exception ex)
                {
                    // 单个分片存不出来不该毁掉整次保存：记日志跳过（读档时该分片走默认值）
                    Log.Error("Save", "分片 '{0}' 写入失败（该分片本次不存档）：{1}", section.Key, ex.Message);
                }
            }

            _storage.Save(slot, _serializer.Serialize(data));
            Log.Info("Save", "已保存到槽位 {0}：v{1}，{2} 个分片，{3}",
                slot, CurrentVersion, data.Sections.Count, string.IsNullOrEmpty(summary) ? "(无摘要)" : summary);
            return true;
        }

        // ── 读档 ──

        /// <summary>
        /// 读档并恢复各分片。返回值是"是否读到了档"；<paramref name="error"/> 是**失败或降级**的原因：
        /// 完全正常时为 null；主档损坏回退了备份时给出原因（业务据此提示玩家"最近一次保存丢失"）。
        /// 降级顺序：主档 → 备份档 → 失败（调用方可据此提示玩家并新建档）。
        /// </summary>
        public bool Load(int slot, out string error)
        {
            error = null;
            if (!TryReadData(slot, out SaveData data, out error))
                return false;

            // 注意：迁移的错误必须走局部变量 —— TryMigrate 会重置 out 参数，
            // 直接传 error 会把上面 TryReadData 写好的"已回退备份"降级原因抹掉
            // （这样业务就再也拿不到降级提示了）。
            if (!_migrator.TryMigrate(data, _serializer, out string migrateError))
            {
                error = migrateError;
                return false;
            }

            RestoreSections(data);
            Log.Info("Save", "已读取槽位 {0}：v{1}，{2}/{3} 个分片恢复",
                slot, data.Version, data.Sections.Count, _sections.Count);
            return true;
        }

        /// <summary>读取并解析槽位（含主档→备份档降级），供 Load / 列表 / 调试复用。</summary>
        public bool TryReadData(int slot, out SaveData data, out string error)
        {
            data = null;
            error = null;

            if (TryParse(_storage.Load(slot), out data, out string mainError))
                return true;

            // 主档坏了：回退上一次保存前的备份（FileSaveStorage 每次覆盖写入前会留 .bak）
            if (TryParse(_storage.LoadBackup(slot), out data, out string backupError))
            {
                error = $"主档损坏已回退备份：{mainError}";
                Log.Warn("Save", "槽位 {0} 主档损坏，已回退备份档（损失最近一次保存）：{1}", slot, mainError);
                return true;
            }

            error = mainError ?? backupError ?? $"槽位 {slot} 没有存档";
            Log.Error("Save", "读取槽位 {0} 失败：{1}", slot, error);
            return false;
        }

        private bool TryParse(string text, out SaveData data, out string error)
        {
            data = null;
            error = null;
            if (string.IsNullOrEmpty(text))
            {
                error = "存档内容为空";
                return false;
            }
            try
            {
                data = _serializer.Deserialize<SaveData>(text);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            if (data == null)
            {
                error = "存档解析结果为空";
                return false;
            }
            return true;
        }

        private void RestoreSections(SaveData data)
        {
            for (int i = 0; i < _sections.Count; i++)
            {
                ISaveSection section = _sections[i];
                SaveSectionEntry entry = data.FindSection(section.Key);
                if (entry == null)
                {
                    // 老档没有这个分片（该功能当时还不存在）——不是错误，走自身默认值
                    Log.Warn("Save", "存档里没有分片 '{0}'（该功能可能是后来加的），保持默认状态", section.Key);
                    continue;
                }

                try
                {
                    if (!section.Restore(entry.Payload, _serializer))
                        Log.Warn("Save", "分片 '{0}' 自身拒绝恢复（版本/内容不符），保持默认状态", section.Key);
                }
                catch (Exception ex)
                {
                    // 一个分片坏掉不能拖垮整个读档
                    Log.Error("Save", "分片 '{0}' 恢复异常（其余分片不受影响）：{1}", section.Key, ex.Message);
                }
            }
        }
    }
}
