using System;

namespace Template.Settings
{
    /// <summary>
    /// 设置项强类型键 = 唯一 id + 缺省值。设置项由**使用方模块**声明（谁用谁定义），例如：
    ///     // Template.Audio/AudioSettings.cs
    ///     public static readonly SettingsKey&lt;float&gt; MasterVolume = new SettingsKey&lt;float&gt;("audio.master.volume", 1f);
    ///
    /// 新增一个设置项 = 定义一个键 + 写一处应用逻辑（消费变更事件），设置系统内核零改动。
    /// 类型仅支持 float / int / bool / string —— 足够覆盖音量、开关、键位、语言码等常见设置项，
    /// 且与 JSON 存储一一对应（不做多态，避免存档演化时的类型地狱）。
    ///
    /// 相等性只比较 Id：同一 id 即同一项设置（默认值不同不影响读写，只影响"文件里没有该项"时的回退值）。
    /// </summary>
    public readonly struct SettingsKey<T> : IEquatable<SettingsKey<T>>
    {
        /// <summary>唯一 id（约定 "模块.设置项" 命名，如 "audio.bgm.volume"）。</summary>
        public readonly string Id;

        /// <summary>缺省值（设置文件里没有该项时返回它）。</summary>
        public readonly T Default;

        public SettingsKey(string id, T defaultValue = default)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("设置键 id 不能为空", nameof(id));
            Id = id;
            Default = defaultValue;
        }

        public bool Equals(SettingsKey<T> other) => string.Equals(Id, other.Id, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SettingsKey<T> other && Equals(other);
        public override int GetHashCode() => Id != null ? Id.GetHashCode() : 0;
        public override string ToString() => Id;
    }
}
