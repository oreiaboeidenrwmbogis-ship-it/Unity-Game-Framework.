using UnityEngine;

namespace Template.Audio
{
    /// <summary>
    /// 默认界面音效（按钮点击 / 面板开合）。模板不附音频资产，默认 clip 为运行期程序生成的正弦音，
    /// 保证 clone 即有声、Demo 可验收；真实项目在 Boot 阶段换成自制音效：
    ///
    ///     AudioFeedback.DefaultScreenOpen = myOpenClip;   // 任意 AudioClip 资产
    ///     AudioFeedback.DefaultClose      = null;         // 置空即关闭该类事件音
    ///
    /// 线程安全说明：此属性只在主线程访问（Init / 事件回调）。置入前务必在 Boot 阶段完成替换，
    /// 之后再改不影响已缓存的生成值。
    /// </summary>
    public static class AudioFeedback
    {
        private const int SampleRate = 44100;

        private static AudioClip _screenOpen;
        private static AudioClip _popupOpen;
        private static AudioClip _close;

        /// <summary>全屏页打开音。</summary>
        public static AudioClip DefaultScreenOpen
        {
            get { return _screenOpen ?? (_screenOpen = Synthesize("ui_screen_open", 0.16f, 0.45f, 880f, 1320f)); }
            set { _screenOpen = value; }
        }

        /// <summary>弹窗打开音（比全屏页更"重"）。</summary>
        public static AudioClip DefaultPopupOpen
        {
            get { return _popupOpen ?? (_popupOpen = Synthesize("ui_popup_open", 0.18f, 0.55f, 660f, 990f)); }
            set { _popupOpen = value; }
        }

        /// <summary>面板关闭音。</summary>
        public static AudioClip DefaultClose
        {
            get { return _close ?? (_close = Synthesize("ui_panel_close", 0.14f, 0.4f, 720f, 480f)); }
            set { _close = value; }
        }

        /// <summary>
        /// 生成一段带包络的双音正弦（freqStart → freqEnd 平滑滑音）。
        /// 仅用于占位：正式项目用真实音效资产替换。
        /// </summary>
        private static AudioClip Synthesize(string name, float duration, float amplitude,
            float freqStart, float freqEnd)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(duration * SampleRate));
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float progress = i / (float)count;
                float freq = Mathf.Lerp(freqStart, freqEnd, progress);
                float envelope = Mathf.Clamp01(t / 0.005f) * Mathf.Clamp01((duration - t) / (duration * 0.7f));
                samples[i] = amplitude * envelope * Mathf.Sin(2f * Mathf.PI * freq * t);
            }
            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
