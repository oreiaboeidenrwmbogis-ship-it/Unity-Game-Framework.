using System;
using System.Collections.Generic;
using Template.Core.Logging;
using Template.Core.Services;
using UnityEngine;

namespace Template.Core.Timing
{
    /// <summary>计时器时钟：Scaled 受全局冻结/时间缩放影响；Unscaled 走真实时间（UI、加载超时等）。</summary>
    public enum TimerClock
    {
        Scaled,
        Unscaled,
    }

    /// <summary>定时器句柄：唯一标识 + 取消失效，可安全重复 Cancel（幂等）。</summary>
    public readonly struct TimerHandle
    {
        public static readonly TimerHandle None = new TimerHandle(null, 0L);

        private readonly TimeService _owner;
        private readonly long _id;

        internal TimerHandle(TimeService owner, long id)
        {
            _owner = owner;
            _id = id;
        }

        public bool IsValid => _id != 0L && _owner != null;

        /// <summary>取消该定时器（幂等；已触发或已取消时无副作用）。</summary>
        public void Cancel() => _owner?.Cancel(this);

        internal long Id => _id;
    }

    /// <summary>
    /// 时间管理器（全局服务）：手动 Tick 驱动的高精度 Timer + 全局冻结（暂停/子弹时间的落地者）。
    ///
    /// 设计要点：
    /// 1. Timer 由 Bootstrap 每帧手动 tick 推进（非 Coroutine），天然支持暂停/缩放，与 MonoBehaviour 生命周期解耦；
    /// 2. 双时钟：Scaled 走 Time.deltaTime（受 timeScale 影响，冻结即停摆）；Unscaled 走真实时间；
    /// 3. Freeze/Unfreeze 统一管理暂停（timeScale=0 并记住原值），游戏暂停请走 GameStateService，
    ///    它会在此冻结全部 Scaled 计时与动画/粒子；如需“暂停中也走”的逻辑用 Unscaled 时钟；
    /// 4. 回调异常隔离：单个回调抛异常不影响其他定时器。
    /// </summary>
    public sealed class TimeService : IGameService, ITickable
    {
        private struct TimerData
        {
            public long Id;
            public double Remaining;   // 剩余秒数（double 防长时间运行精度漂移）
            public float Interval;     // Repeat 间隔
            public bool Repeat;
            public bool Canceled;
            public Action Callback;
            public TimerClock Clock;
        }

        private readonly List<TimerData> _timers = new List<TimerData>();
        private long _nextId = 1L;
        private bool _frozen;
        private float _scaleBeforeFreeze = 1f;

        public float TimeScale => Time.timeScale;
        public bool IsFrozen => _frozen;

        /// <summary>当前待处理的定时器数量（含已取消未清理的，统计用）。</summary>
        public int TimerCount => _timers.Count;

        public void Init() { }

        public void Dispose()
        {
            _timers.Clear();
            Unfreeze(); // 退出时确保恢复 timeScale，防止编辑器遗留冻结状态
        }

        #region 冻结 / 时间缩放

        /// <summary>冻结时间：timeScale → 0，所有 Scaled 时钟（计时器/动画/粒子）停摆。重复调用安全。</summary>
        public void Freeze()
        {
            if (_frozen)
                return;
            _frozen = true;
            _scaleBeforeFreeze = Time.timeScale;
            Time.timeScale = 0f;
            Log.Info("Time", "时间冻结（timeScale=0，恢复值为 {0}）", _scaleBeforeFreeze);
        }

        /// <summary>解除冻结：恢复冻结前的 timeScale。未冻结时调用无副作用。</summary>
        public void Unfreeze()
        {
            if (!_frozen)
                return;
            _frozen = false;
            Time.timeScale = _scaleBeforeFreeze;
            Log.Info("Time", "时间恢复（timeScale={0}）", _scaleBeforeFreeze);
        }

        /// <summary>
        /// 设置全局时间缩放（子弹时间等）。注意：不要用 SetTimeScale(0) 代替 Freeze ——
        /// Freeze 会记住原值以便恢复。游戏暂停请走 GameStateService（内部调用 Freeze/Unfreeze）。
        /// </summary>
        public void SetTimeScale(float scale)
        {
            if (scale <= 0f)
            {
                Log.Warn("Time", "SetTimeScale({0}) 被忽略：暂停请调用 Freeze()", scale);
                return;
            }
            if (_frozen)
            {
                // 冻结中改 timeScale 会在 Unfreeze 时被覆盖，明确告警
                Log.Warn("Time", "SetTimeScale 在冻结期间被忽略（Unfreeze 会恢复 {0}）", _scaleBeforeFreeze);
                return;
            }
            Time.timeScale = scale;
        }

        #endregion

        #region 定时器

        /// <summary>延迟 seconds 后执行一次回调。seconds 必须 ≥ 0。</summary>
        public TimerHandle Delay(float seconds, Action callback, TimerClock clock = TimerClock.Scaled)
            => AddTimer(seconds, callback, clock, repeat: false);

        /// <summary>按 interval 周期重复执行（无限，直到 Cancel / 服务销毁）。</summary>
        public TimerHandle Repeat(float interval, Action callback, TimerClock clock = TimerClock.Scaled)
            => AddTimer(interval, callback, clock, repeat: true);

        private TimerHandle AddTimer(float seconds, Action callback, TimerClock clock, bool repeat)
        {
            if (callback == null)
            {
                Log.Error("Time", "AddTimer: 回调为空");
                return TimerHandle.None;
            }
            if (seconds < 0f)
            {
                Log.Error("Time", "AddTimer: 时长不能为负 ({0})", seconds);
                return TimerHandle.None;
            }

            long id = _nextId++;
            _timers.Add(new TimerData
            {
                Id = id,
                Remaining = seconds,
                Interval = seconds,
                Repeat = repeat,
                Callback = callback,
                Clock = clock,
            });
            return new TimerHandle(this, id);
        }

        /// <summary>取消定时器（幂等）。已触发的定时器无法取消。</summary>
        public void Cancel(TimerHandle handle)
        {
            if (!handle.IsValid)
                return;
            for (int i = 0; i < _timers.Count; i++)
            {
                if (_timers[i].Id == handle.Id)
                {
                    TimerData t = _timers[i];
                    t.Canceled = true;
                    _timers[i] = t;
                    return; // 惰性清理在 Tick 末尾进行
                }
            }
        }

        #endregion

        public void Tick()
        {
            if (_timers.Count == 0)
                return;

            float dtScaled = Time.deltaTime;          // 受 timeScale 影响：冻结时 = 0
            float dtUnscaled = Time.unscaledDeltaTime;
            int count = _timers.Count;                // 快照：回调中新增的定时器下一帧才开始

            for (int i = 0; i < count; i++)
            {
                TimerData t = _timers[i];
                if (t.Canceled)
                    continue;

                double dt = t.Clock == TimerClock.Scaled ? dtScaled : dtUnscaled;
                t.Remaining -= dt;
                if (t.Remaining > 0d)
                {
                    _timers[i] = t;
                    continue;
                }

                // 到期
                if (t.Repeat)
                {
                    t.Remaining += t.Interval;
                    if (t.Remaining <= 0d)
                        t.Remaining = t.Interval; // 极端卡顿保护：单帧最多补一轮，允许轻微漂移
                }
                else
                {
                    t.Canceled = true;
                }
                _timers[i] = t;

                FireSafely(t.Callback);
            }

            // 惰性清理已取消项
            for (int i = _timers.Count - 1; i >= 0; i--)
            {
                if (_timers[i].Canceled)
                    _timers.RemoveAt(i);
            }
        }

        private static void FireSafely(Action callback)
        {
            try
            {
                callback();
            }
            catch (Exception ex)
            {
                Log.Error("Time", "定时器回调抛异常: {0}", ex);
            }
        }
    }
}
