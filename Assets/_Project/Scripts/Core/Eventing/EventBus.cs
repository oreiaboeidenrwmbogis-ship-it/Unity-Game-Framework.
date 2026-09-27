using System;
using System.Collections.Generic;
using System.Text;
using Template.Core.Logging;
using UnityEngine;

namespace Template.Core.Eventing
{
    /// <summary>
    /// 事件中心（非泛型）：全局配置与统计。泛型事件总线见 <see cref="EventBus{TEvent}"/>。
    /// </summary>
    public static class EventBus
    {
        /// <summary>调试用：开启后发布/订阅输出 Verbose 日志（含事件类型名与监听数），排查模块耦合问题。</summary>
        public static bool VerboseLogging { get; set; }

        /// <summary>全事件类型累计发布次数（主线程累加）。</summary>
        public static long TotalPublishCount { get; private set; }

        internal static void RecordPublish() => TotalPublishCount++;

        // 全类型订阅者注册表：域重载 / 每次 Play 前清空所有监听（防无域重载模式下跨 Play 泄漏）
        private static readonly Dictionary<Type, Action> _resetActions = new Dictionary<Type, Action>();

        internal static void RegisterReset(Type eventType, Action reset)
        {
            lock (_resetActions)
            {
                _resetActions[eventType] = reset;
            }
        }

        // 全类型监听数统计：泛型 EventBus<TEvent> 在静态构造里登记自己的计数读取器（键 = 事件类型）。
        // 用途：调试面板显示"当前有多少个事件监听者"—— 泄漏（忘了退订）最直观的表现就是它只涨不跌。
        private static readonly Dictionary<Type, Func<int>> _handlerCounts = new Dictionary<Type, Func<int>>();

        internal static void RegisterHandlerCount(Type eventType, Func<int> getter)
        {
            lock (_handlerCounts)
            {
                _handlerCounts[eventType] = getter; // 键为类型：无域重载模式下重复登记是幂等的
            }
        }

        /// <summary>全部事件类型的监听者总数（调试/性能监控用）。</summary>
        public static int TotalHandlerCount
        {
            get
            {
                int total = 0;
                lock (_handlerCounts)
                {
                    foreach (Func<int> getter in _handlerCounts.Values)
                    {
                        try { total += getter(); }
                        catch { /* 统计失败不影响业务 */ }
                    }
                }
                return total;
            }
        }

        /// <summary>已登记统计的事件类型数（被订阅过的类型）。</summary>
        public static int TrackedEventTypeCount
        {
            get { lock (_handlerCounts) { return _handlerCounts.Count; } }
        }

        /// <summary>逐类型监听数快照（只列有监听者的类型，避免刷屏）。</summary>
        public static string DumpHandlers()
        {
            var sb = new StringBuilder();
            int types = 0;
            lock (_handlerCounts)
            {
                foreach (KeyValuePair<Type, Func<int>> pair in _handlerCounts)
                {
                    int count;
                    try { count = pair.Value(); }
                    catch { continue; }
                    if (count <= 0)
                        continue;

                    types++;
                    sb.Append(pair.Key.Name).Append(' ').Append(count).AppendLine();
                }
            }
            return types == 0 ? "（当前无事件监听者）" : sb.ToString().TrimEnd();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAll()
        {
            TotalPublishCount = 0;
            lock (_resetActions)
            {
                foreach (Action reset in _resetActions.Values)
                {
                    try { reset(); }
                    catch (Exception ex) { Log.Error("Event", "事件总线重置失败 <{0}>: {1}", ex.Message); }
                }
            }
        }
    }

    /// <summary>
    /// 强类型泛型事件总线 —— 模块解耦的核心：发布方不认识任何监听者，监听者互不认识。
    ///
    /// 设计要点：
    /// 1. 事件必须是 struct（零 GC，且约束发布方一次性携带所有数据）；
    /// 2. 广播安全：监听器中订阅/退订不会破坏本次广播（快照语义：本次新增监听者不接收当前事件）；
    /// 3. 异常隔离：单个监听者抛异常只记录日志，不瘫痪整条总线；
    /// 4. 红线：对象销毁时必须退订（Subscribe 返回令牌可 using / Dispose），否则事件中心持有引用导致泄漏。
    ///
    /// 用法：
    ///     EventBus&lt;EnemyKilledEvent&gt;.Subscribe(OnEnemyKilled);       // 监听
    ///     EventBus&lt;EnemyKilledEvent&gt;.Publish(new EnemyKilledEvent()); // 发布
    /// </summary>
    public static class EventBus<TEvent> where TEvent : struct
    {
        private static readonly List<Action<TEvent>> _handlers = new List<Action<TEvent>>();
        private static int _aliveCount;      // 有效监听数（不随广播中退订即时变）
        private static int _dispatchDepth;   // 广播嵌套深度
        private static bool _dirty;          // 广播期间产生过退订，广播结束需要压缩

        /// <summary>当前有效监听者数量（调试/统计）。</summary>
        public static int HandlerCount => _aliveCount;

        /// <summary>本事件类型累计发布次数。</summary>
        public static long PublishCount { get; private set; }

        public static bool HasSubscribers => _aliveCount > 0;

        static EventBus()
        {
            EventBus.RegisterReset(typeof(TEvent), Reset);
            EventBus.RegisterHandlerCount(typeof(TEvent), () => _aliveCount); // 调试面板：事件监听数统计
        }

        private static void Reset()
        {
            _handlers.Clear();
            _aliveCount = 0;
            _dispatchDepth = 0;
            _dirty = false;
            PublishCount = 0;
        }

        /// <summary>
        /// 订阅事件。重复订阅同一 handler 会被拒绝并告警（通常是双重订阅 bug）。
        /// 返回订阅令牌（struct，using 或字段 Dispose 即退订）。
        /// </summary>
        public static Subscription Subscribe(Action<TEvent> handler)
        {
            if (handler == null)
            {
                Log.Error("Event", "Subscribe: handler 为空 <{0}>", typeof(TEvent).Name);
                return default;
            }
            if (_handlers.Contains(handler))
            {
                Log.Warn("Event", "Subscribe: <{0}> 重复订阅同一 handler（忽略）", typeof(TEvent).Name);
                return default;
            }

            _handlers.Add(handler);
            _aliveCount++;
            if (EventBus.VerboseLogging)
                Log.Verbose("Event", "订阅 <{0}>，当前监听数 {1}", typeof(TEvent).Name, _aliveCount);
            return new Subscription(handler);
        }

        /// <summary>退订。广播期间调用是安全的（内部延迟到广播结束后压缩列表）。</summary>
        public static void Unsubscribe(Action<TEvent> handler)
        {
            if (handler == null)
                return;

            if (_dispatchDepth > 0)
            {
                // 广播中：不能立即 RemoveAt（正在迭代），将槽位置空，广播结束后统一压缩
                for (int i = _handlers.Count - 1; i >= 0; i--)
                {
                    if (_handlers[i] == handler)
                    {
                        _handlers[i] = null;
                        _aliveCount--;
                        _dirty = true;
                        return;
                    }
                }
            }
            else
            {
                int index = _handlers.LastIndexOf(handler);
                if (index >= 0)
                {
                    _handlers.RemoveAt(index);
                    _aliveCount--;
                    return;
                }
            }

            Log.Warn("Event", "Unsubscribe: <{0}> 未找到该 handler（可能已退订或从未订阅）", typeof(TEvent).Name);
        }

        /// <summary>发布事件。同步执行所有监听者（按订阅顺序）。</summary>
        public static void Publish(TEvent evt)
        {
            if (_aliveCount == 0)
            {
                if (EventBus.VerboseLogging)
                    Log.Verbose("Event", "发布 <{0}>（无监听者）", typeof(TEvent).Name);
                return;
            }

            EventBus.RecordPublish();
            PublishCount++;
            if (EventBus.VerboseLogging)
                Log.Verbose("Event", "发布 <{0}>，分发 {1} 个监听者", typeof(TEvent).Name, _aliveCount);

            _dispatchDepth++;
            int count = _handlers.Count; // 快照：本次广播中新增的监听者不接收当前事件
            for (int i = 0; i < count; i++)
            {
                Action<TEvent> handler = _handlers[i];
                if (handler == null)
                    continue; // 广播中退订留下的空槽

                try
                {
                    handler(evt);
                }
                catch (Exception ex)
                {
                    // 异常隔离：一个监听者崩溃不能瘫痪整条总线
                    Log.Error("Event", "事件 <{0}> 的监听者抛异常: {1}", typeof(TEvent).Name, ex);
                }
            }
            _dispatchDepth--;

            if (_dispatchDepth == 0 && _dirty)
            {
                _dirty = false;
                Compact();
            }
        }

        private static void Compact()
        {
            for (int i = _handlers.Count - 1; i >= 0; i--)
            {
                if (_handlers[i] == null)
                    _handlers.RemoveAt(i);
            }
        }

        /// <summary>
        /// 订阅令牌。使用 using 作用域自动退订，或存为字段在 OnDisable/OnDestroy 时 Dispose。
        /// struct 实现 IDisposable，using 时零装箱。
        /// </summary>
        public readonly struct Subscription : IDisposable
        {
            private readonly Action<TEvent> _handler;

            internal Subscription(Action<TEvent> handler) => _handler = handler;

            public void Dispose()
            {
                if (_handler != null)
                    Unsubscribe(_handler);
            }
        }
    }
}
