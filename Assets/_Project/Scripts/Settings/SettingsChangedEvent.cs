namespace Template.Settings
{
    /// <summary>
    /// 设置项变更事件（强类型，走全局事件中心）。监听者用**键对象**做比较，调用点没有字符串：
    ///
    ///     EventBus&lt;SettingsChangedEvent&lt;float&gt;&gt;.Subscribe(OnFloatChanged);
    ///     void OnFloatChanged(SettingsChangedEvent&lt;float&gt; e)
    ///     {
    ///         if (e.Key.Equals(AudioSettings.MasterVolume)) ApplyVolume(e.Value);
    ///     }
    ///
    /// T 是值类型时事件结构自身仍是 struct（事件中心的约束只作用于事件类型），无装箱。
    /// </summary>
    public readonly struct SettingsChangedEvent<T>
    {
        /// <summary>发生变更的设置项（用它与自己声明的键做相等比较）。</summary>
        public readonly SettingsKey<T> Key;

        /// <summary>变更后的值。</summary>
        public readonly T Value;

        public SettingsChangedEvent(SettingsKey<T> key, T value)
        {
            Key = key;
            Value = value;
        }
    }
}
