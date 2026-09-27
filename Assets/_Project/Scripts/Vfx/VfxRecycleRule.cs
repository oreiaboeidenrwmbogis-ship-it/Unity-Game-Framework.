namespace Template.Vfx
{
    /// <summary>
    /// 特效回收判定（**纯逻辑，可单测**）—— 抽出来是因为这里的坑最容易出：
    /// 粒子系统在"起播当帧"还没开始播放，这时判定"没在播放"会把刚生成的特效立刻回收掉
    /// （音效池踩过同一个坑：`isPlaying` 起播当帧不为 true）。
    /// </summary>
    public static class VfxRecycleRule
    {
        /// <summary>最短存活时间：小于它一律不回收（躲开"起播当帧"）。</summary>
        public const float MinLifetimeSeconds = 0.1f;

        /// <summary>没有粒子系统时（如程序生成的简单特效）按这个时长回收。</summary>
        public const float FallbackLifetimeSeconds = 3f;

        /// <summary>
        /// 是否该回收：
        /// <list type="bullet">
        /// <item>存活时间不足 <see cref="MinLifetimeSeconds"/> → 不回收（可能还没真正开始播）；</item>
        /// <item>有粒子系统 → 全部不再存活才回收；</item>
        /// <item>没有粒子系统 → 超过 <see cref="FallbackLifetimeSeconds"/> 就回收（兜底，避免永久占着池）。</item>
        /// </list>
        /// </summary>
        public static bool ShouldRecycle(float ageSeconds, bool hasParticleSystem, bool anyParticleAlive,
            float fallbackLifetime = FallbackLifetimeSeconds)
        {
            if (ageSeconds < MinLifetimeSeconds)
                return false;
            return hasParticleSystem ? !anyParticleAlive : ageSeconds >= fallbackLifetime;
        }
    }
}
