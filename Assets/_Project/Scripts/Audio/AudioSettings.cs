using Template.Settings;

namespace Template.Audio
{
    /// <summary>
    /// 音频模块的设置项定义（谁用谁定义：键与默认值由使用方声明，设置系统只负责存取与广播）。
    ///
    /// 新增一条音频设置（如"UI 音效开关"）：在此加一个键，在 AudioService 的变更处理里加一条应用逻辑 ——
    /// 设置系统内核、存储格式都不动。
    /// </summary>
    public static class AudioSettings
    {
        /// <summary>
        /// 各通道音量键，**索引 = (int)AudioBus** —— 与枚举一一对应，
        /// 因此音频侧可以整批拉取/整批应用，不需要写 switch。
        /// </summary>
        public static readonly SettingsKey<float>[] VolumeKeys =
        {
            new SettingsKey<float>("audio.master.volume", 1f),
            new SettingsKey<float>("audio.bgm.volume", 1f),
            new SettingsKey<float>("audio.sfx.volume", 1f),
            new SettingsKey<float>("audio.voice.volume", 1f),
        };
    }
}
