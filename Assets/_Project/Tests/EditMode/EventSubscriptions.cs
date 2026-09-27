using System;
using System.Collections.Generic;
using Template.Core.Eventing;

namespace Template.Tests
{
    /// <summary>
    /// 测试用订阅清理器。事件总线是**静态**的（<c>EventBus&lt;TEvent&gt;</c>），
    /// 用例之间不退订就会互相污染（上一个用例的监听者会在下一个用例里被触发），
    /// 所以每个测试类在 [SetUp] 建一个、[TearDown] 统一退订。
    ///
    /// 用法：
    ///     private EventSubscriptions _subs;
    ///     [SetUp] public void SetUp() => _subs = new EventSubscriptions();
    ///     [TearDown] public void TearDown() => _subs.DisposeAll();
    ///     ...
    ///     _subs.Subscribe&lt;MyEvent&gt;(OnMyEvent);
    /// </summary>
    internal sealed class EventSubscriptions
    {
        private readonly List<Action> _unsubscribe = new List<Action>();

        public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : struct
        {
            EventBus<TEvent>.Subscribe(handler);
            _unsubscribe.Add(() => EventBus<TEvent>.Unsubscribe(handler));
        }

        /// <summary>逆序退订（与订阅顺序相反，和真实的销毁顺序一致）。</summary>
        public void DisposeAll()
        {
            for (int i = _unsubscribe.Count - 1; i >= 0; i--)
                _unsubscribe[i]();
            _unsubscribe.Clear();
        }
    }
}
