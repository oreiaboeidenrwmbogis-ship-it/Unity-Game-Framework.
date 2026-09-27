using UnityEngine;

namespace Template.Audio
{
    /// <summary>
    /// 线性音量（0~1，供设置界面滑条使用）与 AudioMixer 分贝值的互相换算。
    ///
    /// 为什么需要它：AudioMixer 的 volume 参数单位是 dB，0 dB = 原音量、-80 dB = 静音；
    /// 而玩家面向上看到的滑条是 0~100% 的线性值。本类做唯一一处换算，避免各处 20*log10 散落。
    /// </summary>
    public static class AudioVolume
    {
        /// <summary>Mixer 静音阈值（dB）。低于此值 Unity 按静音处理，且 20*log10(0) = -∞ 须拦截。</summary>
        public const float MinDecibel = -80f;

        /// <summary>线性值 → 分贝。0（含以下）直接映射为 <see cref="MinDecibel"/>（静音）。</summary>
        public static float LinearToDecibel(float linear)
        {
            if (linear <= 0.0001f)
                return MinDecibel;
            return Mathf.Max(MinDecibel, 20f * Mathf.Log10(Mathf.Clamp01(linear)));
        }

        /// <summary>分贝 → 线性值（供设置界面回显滑条位置）。</summary>
        public static float DecibelToLinear(float decibel)
        {
            if (decibel <= MinDecibel)
                return 0f;
            return Mathf.Clamp01(Mathf.Pow(10f, decibel / 20f));
        }

        /// <summary>线性值归一到 0~1（设置界面输入消毒用）。</summary>
        public static float Clamp01(float linear) => Mathf.Clamp01(linear);
    }
}
