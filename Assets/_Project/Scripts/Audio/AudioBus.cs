namespace Template.Audio
{
    /// <summary>
    /// 音频通道（可独立调节音量的播放分组）。
    /// Master 是总控通道：不用于播放，只作为 BGM/SFX/Voice 的整体缩放。
    /// </summary>
    public enum AudioBus
    {
        /// <summary>总控（所有通道的乘数；无独立播放）。</summary>
        Master = 0,

        /// <summary>背景音乐（单轨，切换时交叉淡变）。</summary>
        Bgm = 1,

        /// <summary>音效（可并发叠加，支持 2D / 3D 空间音）。</summary>
        Sfx = 2,

        /// <summary>语音（单轨，新语音打断旧语音）。</summary>
        Voice = 3,
    }
}
