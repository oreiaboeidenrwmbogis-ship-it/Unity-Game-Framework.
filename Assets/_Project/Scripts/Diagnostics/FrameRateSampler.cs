// 整个 Diagnostics 模块都被这行守卫包住 —— 正式包（非编辑器、非开发构建）里本文件编译为空，
// 连同调用点的开销一起消失（与 Log.Verbose/Info 用的是同一个条件）。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;

namespace Template.Diagnostics
{
    /// <summary>
    /// 帧率采样器（**纯逻辑**，不碰任何 Unity API —— 因此可用 EditMode 单测喂固定 delta 验证）。
    ///
    /// 为什么不是直接显示 <c>1 / Time.deltaTime</c>：
    /// 1. 单帧读数抖得没法看（60↔120 来回跳），看趋势必须**滑动窗口平滑**；
    /// 2. 平均帧率永远暴露不了"一秒一次的大尖峰"—— 卡顿要看**窗口内最差帧**，
    ///    所以本类同时给平均值与最差值，二者结合才判断得出"稳定但偶尔卡"还是"整体就是慢"。
    ///
    /// 内存：一个定长 float 数组，零 GC（每帧只是一次赋值与加减）。
    /// </summary>
    public sealed class FrameRateSampler
    {
        private readonly float[] _frameMs; // 环形缓冲：最近 Window 帧的帧时长（毫秒）
        private int _cursor;
        private int _count;
        private double _sumMs;

        /// <param name="window">采样窗口（帧数）。越大越平滑、对卡顿的反应越慢；默认 120 帧 ≈ 2 秒@60fps。</param>
        public FrameRateSampler(int window = 120)
        {
            if (window < 1)
                window = 1;
            _frameMs = new float[window];
        }

        /// <summary>窗口大小（帧）。</summary>
        public int Window => _frameMs.Length;

        /// <summary>已采样帧数（不足窗口大小说明读数还没稳定下来）。</summary>
        public int SampleCount => _count;

        /// <summary>是否已填满窗口（读数已稳定）。</summary>
        public bool IsStable => _count >= _frameMs.Length;

        /// <summary>推送一帧。参数是**不受 timeScale 影响的**真实帧间隔（暂停时也要反映真实性能）。</summary>
        public void Sample(float unscaledDeltaSeconds)
        {
            // 无效帧间隔一律丢弃（首帧、无域重载边界、断点/挂起恢复时的畸形 delta）。
            // 注意 NaN：它与任何数比较都是 false，所以 **不能**只写 `<= 0f` ——
            // NaN 会溜进去把整个窗口的平均值与最差值污染成 NaN（单测 Sample_InvalidDelta_IsIgnored 抓的就是这个）。
            if (float.IsNaN(unscaledDeltaSeconds) || float.IsInfinity(unscaledDeltaSeconds)
                || unscaledDeltaSeconds <= 0f)
            {
                return;
            }

            float ms = unscaledDeltaSeconds * 1000f;

            if (_count == _frameMs.Length)
                _sumMs -= _frameMs[_cursor]; // 挤掉窗口里最旧的一帧

            _frameMs[_cursor] = ms;
            _sumMs += ms;
            _cursor = (_cursor + 1) % _frameMs.Length;
            if (_count < _frameMs.Length)
                _count++;
        }

        /// <summary>平均帧时长（毫秒）。</summary>
        public float AverageFrameMs => _count == 0 ? 0f : (float)(_sumMs / _count);

        /// <summary>平滑帧率（窗口平均）。</summary>
        public float Fps
        {
            get
            {
                float avg = AverageFrameMs;
                return avg <= 0f ? 0f : 1000f / avg;
            }
        }

        /// <summary>窗口内最差（最长）帧时长，毫秒。卡顿判定看它。</summary>
        public float WorstFrameMs
        {
            get
            {
                float worst = 0f;
                for (int i = 0; i < _count; i++)
                {
                    if (_frameMs[i] > worst)
                        worst = _frameMs[i];
                }
                return worst;
            }
        }

        /// <summary>窗口内最差帧对应的瞬时帧率（"掉到过多少帧"）。</summary>
        public float WorstFps
        {
            get
            {
                float worst = WorstFrameMs;
                return worst <= 0f ? 0f : 1000f / worst;
            }
        }

        /// <summary>清空窗口（切场景/进出菜单后重新计量）。</summary>
        public void Reset()
        {
            Array.Clear(_frameMs, 0, _frameMs.Length);
            _cursor = 0;
            _count = 0;
            _sumMs = 0d;
        }
    }
}
#endif
