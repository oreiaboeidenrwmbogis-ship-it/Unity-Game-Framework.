using UnityEngine;

namespace Template.Demo
{
    /// <summary>
    /// 演示用音频素材 —— 全部运行期程序合成（正弦/噪声 + 包络），不依赖任何音频资产。
    /// 仅为让 Demo 有真实可听内容（与 DemoUiPanels 代码构建面板同一思路）；
    /// 真实项目的音频一律走资产 + Addressables，本类随 Demo 目录整体删除。
    /// </summary>
    internal static class DemoAudioClips
    {
        private const int SampleRate = 44100;

        // 小调 7 和弦琶音（A3 C4 E4 G4），0.104s 一个音、8 音一循环
        private static readonly float[] Arpeggio = { 220f, 261.63f, 329.63f, 392f, 329.63f, 261.63f, 220f, 196f };

        /// <summary>循环 BGM：柔和琶音闭环（8 音 × 0.5s = 4 秒，循环无缝）。</summary>
        public static AudioClip CreateLoopBgm()
        {
            const float noteDuration = 0.5f;
            int count = Mathf.RoundToInt(Arpeggio.Length * noteDuration * SampleRate);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                int step = (int)(t / noteDuration) % Arpeggio.Length;
                float noteT = t - Mathf.Floor(t / noteDuration) * noteDuration;
                float freq = Arpeggio[step];
                // 软包络：音头 60ms 渐入、音尾 150ms 渐出（跨度大于单音时长 → 音间自然衔接）
                float envelope = Mathf.Clamp01(noteT / 0.06f)
                    * Mathf.Clamp01((noteDuration * 1.3f - noteT) / 0.15f);
                samples[i] = 0.22f * envelope
                    * (Mathf.Sin(2f * Mathf.PI * freq * t) + 0.4f * Mathf.Sin(4f * Mathf.PI * freq * t));
            }
            var clip = AudioClip.Create("demo_bgm_loop", count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>枪声：低频 boom + 宽带噪声爆裂，短促（0.14s）。</summary>
        public static AudioClip CreateShot()
        {
            return Create("demo_sfx_shot", 0.14f, (t, duration) =>
            {
                float decay = Mathf.Exp(-26f * t);
                float noise = UnityEngine.Random.Range(-1f, 1f);
                return decay * (0.7f * noise + 0.5f * Mathf.Sin(2f * Mathf.PI * 80f * t));
            });
        }

        /// <summary>命中提示：高频短哔，用于验证 3D 空间音（音高固定便于听方位）。</summary>
        public static AudioClip CreatePing()
        {
            return Create("demo_sfx_ping", 0.12f, (t, duration) =>
            {
                float envelope = Mathf.Clamp01(t / 0.004f) * Mathf.Clamp01((duration - t) / (duration * 0.6f));
                return 0.5f * envelope * Mathf.Sin(2f * Mathf.PI * 1320f * t);
            });
        }

        /// <summary>语音占位：400→300Hz 下滑长音（0.5s），用于演示单轨打断语义。</summary>
        public static AudioClip CreateVoiceLine(int index)
        {
            float start = index % 2 == 0 ? 400f : 340f;
            return Create("demo_voice_" + index, 0.5f, (t, duration) =>
            {
                float envelope = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((duration - t) / 0.15f);
                return 0.35f * envelope * Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(start, start - 100f, t / duration) * t);
            });
        }

        private static AudioClip Create(string name, float duration, System.Func<float, float, float> sample)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[count];
            for (int i = 0; i < count; i++)
                data[i] = Mathf.Clamp(sample(i / (float)SampleRate, duration), -1f, 1f);
            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
