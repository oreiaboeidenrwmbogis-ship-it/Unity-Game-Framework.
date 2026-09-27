using NUnit.Framework;
using Template.Vfx;

namespace Template.Tests
{
    /// <summary>
    /// 特效回收判定（纯逻辑）。这里最容易出的事故是"刚生成就被回收"——
    /// 粒子系统在起播当帧还没开始播，只看"没在播放"就回收会把特效吞掉
    /// （音效池踩过同一个坑：`isPlaying` 起播当帧不为 true）。
    /// </summary>
    public class VfxRecycleRuleTests
    {
        [Test]
        public void ShouldRecycle_NewlySpawned_IsNeverRecycled()
        {
            // 起播当帧：粒子"还没在活着"，但绝不能回收
            Assert.IsFalse(VfxRecycleRule.ShouldRecycle(0f, hasParticleSystem: true, anyParticleAlive: false));
            Assert.IsFalse(VfxRecycleRule.ShouldRecycle(
                VfxRecycleRule.MinLifetimeSeconds * 0.5f, hasParticleSystem: true, anyParticleAlive: false));
        }

        [Test]
        public void ShouldRecycle_WithParticles_WaitsUntilAllDead()
        {
            const float age = 1f; // 已过最短存活时间

            Assert.IsFalse(VfxRecycleRule.ShouldRecycle(age, hasParticleSystem: true, anyParticleAlive: true),
                "还有粒子活着就不能回收");
            Assert.IsTrue(VfxRecycleRule.ShouldRecycle(age, hasParticleSystem: true, anyParticleAlive: false),
                "粒子全灭了才回收");
        }

        [Test]
        public void ShouldRecycle_WithoutParticles_FallsBackToLifetime()
        {
            const float fallback = 3f;

            Assert.IsFalse(VfxRecycleRule.ShouldRecycle(1f, hasParticleSystem: false, anyParticleAlive: false, fallback),
                "没有粒子系统时按兜底时长回收（避免很快就没了）");
            Assert.IsTrue(VfxRecycleRule.ShouldRecycle(3f, hasParticleSystem: false, anyParticleAlive: false, fallback),
                "到兜底时长必须回收，否则会永久占着池");
        }

        [Test]
        public void FallbackLifetime_IsLongerThanMinLifetime()
        {
            Assert.Greater(VfxRecycleRule.FallbackLifetimeSeconds, VfxRecycleRule.MinLifetimeSeconds);
        }
    }
}
