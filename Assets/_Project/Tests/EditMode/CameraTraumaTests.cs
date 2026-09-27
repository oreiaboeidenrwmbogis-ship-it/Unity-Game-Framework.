using NUnit.Framework;
using Template.CameraSystem;

namespace Template.Tests
{
    /// <summary>
    /// Trauma 震动模型的数学（纯逻辑）。要点：
    /// 累加要夹紧、要按秒衰减、力度必须**平方映射**（否则小事件也会显得很夸张）。
    /// </summary>
    public class CameraTraumaTests
    {
        [Test]
        public void Add_AccumulatesAndClampsAtOne()
        {
            var trauma = new CameraTrauma();

            Assert.AreEqual(0.3f, trauma.Add(0.3f), 0.0001f);
            Assert.AreEqual(0.6f, trauma.Add(0.3f), 0.0001f);
            Assert.AreEqual(CameraTrauma.MaxTrauma, trauma.Add(0.8f), 0.0001f, "累加不能越过 1");
            Assert.IsTrue(trauma.IsActive);
        }

        [Test]
        public void Add_NegativeIsIgnored()
        {
            var trauma = new CameraTrauma();
            trauma.Add(0.5f);

            Assert.AreEqual(0.5f, trauma.Add(-1f), 0.0001f, "负值不该减少创伤（清空用 Reset）");
        }

        [Test]
        public void Decay_ReducesByRateAndStopsAtZero()
        {
            var trauma = new CameraTrauma { DecayPerSecond = 2f };
            trauma.Add(1f);

            Assert.AreEqual(0.8f, trauma.Decay(0.1f), 0.0001f);
            Assert.AreEqual(0.4f, trauma.Decay(0.2f), 0.0001f);
            Assert.AreEqual(0f, trauma.Decay(10f), 0.0001f, "衰减到 0 就该停住，不能变成负数");
            Assert.IsFalse(trauma.IsActive);
        }

        [Test]
        public void Decay_WithZeroOrNegativeDelta_IsNoOp()
        {
            var trauma = new CameraTrauma();
            trauma.Add(0.5f);

            Assert.AreEqual(0.5f, trauma.Decay(0f), 0.0001f);
            Assert.AreEqual(0.5f, trauma.Decay(-1f), 0.0001f);
        }

        [Test]
        public void ForceOf_IsQuadraticMapping()
        {
            var trauma = new CameraTrauma { MaxForce = 3f };

            Assert.AreEqual(3f, trauma.ForceOf(1f), 0.0001f, "满创伤 = 满力度");
            Assert.AreEqual(0.75f, trauma.ForceOf(0.5f), 0.0001f, "半创伤只有 1/4 力度（平方映射）");
            Assert.AreEqual(0.27f, trauma.ForceOf(0.3f), 0.0001f, "小创伤几乎不晃");
            Assert.AreEqual(0f, trauma.ForceOf(0f), 0.0001f);
        }

        [Test]
        public void ForceOf_ClampsOutOfRangeInput()
        {
            var trauma = new CameraTrauma { MaxForce = 2f };

            Assert.AreEqual(2f, trauma.ForceOf(5f), 0.0001f);
            Assert.AreEqual(0f, trauma.ForceOf(-1f), 0.0001f);
        }

        [Test]
        public void Reset_ClearsTrauma()
        {
            var trauma = new CameraTrauma();
            trauma.Add(1f);

            trauma.Reset();

            Assert.AreEqual(0f, trauma.Value, 0.0001f);
            Assert.IsFalse(trauma.IsActive);
        }
    }
}
