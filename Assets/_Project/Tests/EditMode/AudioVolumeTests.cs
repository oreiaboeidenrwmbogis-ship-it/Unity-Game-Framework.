using NUnit.Framework;
using Template.Audio;

namespace Template.Tests
{
    /// <summary>线性音量 ↔ 分贝换算（接入 AudioMixer 时要用，先按数学契约测住）。</summary>
    public class AudioVolumeTests
    {
        [Test]
        public void LinearToDecibel_EndpointsAreClamped()
        {
            Assert.AreEqual(AudioVolume.MinDecibel, AudioVolume.LinearToDecibel(0f), 0.01f, "静音应是 -80dB 下限");
            Assert.AreEqual(0f, AudioVolume.LinearToDecibel(1f), 0.01f, "满音量应是 0dB");
        }

        [Test]
        public void LinearToDecibel_HalfIsAboutMinusSix()
        {
            // 20*log10(0.5) ≈ -6.02dB —— 数字音频里"减半"的常识值
            Assert.AreEqual(-6.02f, AudioVolume.LinearToDecibel(0.5f), 0.05f);
        }

        [TestCase(0f)]
        [TestCase(0.25f)]
        [TestCase(0.5f)]
        [TestCase(1f)]
        public void DecibelToLinear_IsInverseOfLinearToDecibel(float linear)
        {
            float decibel = AudioVolume.LinearToDecibel(linear);
            Assert.AreEqual(linear, AudioVolume.DecibelToLinear(decibel), 0.001f);
        }

        [Test]
        public void DecibelToLinear_BelowFloor_IsSilence()
        {
            Assert.AreEqual(0f, AudioVolume.DecibelToLinear(-100f));
            Assert.AreEqual(0f, AudioVolume.DecibelToLinear(AudioVolume.MinDecibel));
        }
    }
}
