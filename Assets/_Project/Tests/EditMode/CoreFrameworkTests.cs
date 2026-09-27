using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Template.Core.Eventing;
using Template.Core.Fsm;
using Template.Core.Pooling;
using UnityEngine;
using UnityEngine.TestTools;

namespace Template.Tests
{
    /// <summary>对象池的行为契约（阶段 3.0 测试骨架的第一组用例）。</summary>
    public class ObjectPoolTests
    {
        [Test]
        public void Rent_AfterReturn_ReusesSameInstance()
        {
            var pool = new ObjectPool<List<int>>(() => new List<int>(), onReturn: list => list.Clear());

            List<int> first = pool.Rent();
            first.Add(7);
            pool.Return(first);

            List<int> second = pool.Rent();

            Assert.AreSame(first, second, "归还后再次 Rent 应复用同一实例（池的意义所在）");
            Assert.AreEqual(0, second.Count, "onReturn 回调应已清空对象");
            Assert.AreEqual(1, pool.TotalCreated, "只应创建过一次");
        }

        [Test]
        public void Return_BeyondCapacity_DropsInsteadOfGrowing()
        {
            var pool = new ObjectPool<object>(() => new object(), capacity: 2);

            for (int i = 0; i < 5; i++)
                pool.Return(new object());

            Assert.AreEqual(2, pool.CountInactive, "空闲数不应超过容量（超出的交给 GC）");
        }

        [Test]
        public void Rent_FactoryReturnsNull_ThrowsInsteadOfSilentNull()
        {
            var pool = new ObjectPool<object>(() => null);

            Assert.Throws<InvalidOperationException>(() => pool.Rent());
        }
    }

    /// <summary>事件总线的行为契约：广播语义、广播中退订、异常隔离。</summary>
    public class EventBusTests
    {
        private struct TestEvent
        {
            public int Value;
        }

        private EventSubscriptions _subs;

        [SetUp]
        public void SetUp() => _subs = new EventSubscriptions();

        [TearDown]
        public void TearDown() => _subs.DisposeAll();

        [Test]
        public void Publish_DeliversToEverySubscriber()
        {
            int sum = 0;
            _subs.Subscribe<TestEvent>(e => sum += e.Value);
            _subs.Subscribe<TestEvent>(e => sum += e.Value * 10);

            EventBus<TestEvent>.Publish(new TestEvent { Value = 2 });

            Assert.AreEqual(22, sum);
        }

        [Test]
        public void Unsubscribe_DuringBroadcast_DoesNotBreakCurrentDispatch()
        {
            var visited = new List<string>();
            Action<TestEvent> selfRemoving = null;
            selfRemoving = e =>
            {
                visited.Add("b");
                EventBus<TestEvent>.Unsubscribe(selfRemoving); // 广播中退订自己（红线场景）
            };

            _subs.Subscribe<TestEvent>(e => visited.Add("a"));
            EventBus<TestEvent>.Subscribe(selfRemoving); // 本用例自己退订，不交给清理器
            _subs.Subscribe<TestEvent>(e => visited.Add("c"));

            Assert.DoesNotThrow(() => EventBus<TestEvent>.Publish(default));
            Assert.AreEqual(new[] { "a", "b", "c" }, visited, "本次广播必须完整走完（快照语义）");

            // 下次广播：已退订者不再收到，且列表已被压缩
            visited.Clear();
            EventBus<TestEvent>.Publish(default);
            Assert.AreEqual(new[] { "a", "c" }, visited);
        }

        [Test]
        public void HandlerThrows_DoesNotStopOtherSubscribers()
        {
            bool survivorRan = false;
            _subs.Subscribe<TestEvent>(e => throw new InvalidOperationException("故意抛错"));
            _subs.Subscribe<TestEvent>(e => survivorRan = true);

            // 总线会把监听者异常记成 Error 日志（这是预期行为）——必须在日志发生前显式声明，
            // 否则 Unity Test Framework 会把"未声明的 Error 日志"直接判为测试失败。
            LogAssert.Expect(LogType.Error, new Regex("监听者抛异常"));

            Assert.DoesNotThrow(() => EventBus<TestEvent>.Publish(default)); // 异常被总线隔离
            Assert.IsTrue(survivorRan, "前一个监听者崩溃不应影响后面的监听者");
        }
    }

    /// <summary>状态机的转换表语义。</summary>
    public class StateMachineTests
    {
        private sealed class CountingState : StateBase
        {
            public int Entered;
            public int Exited;

            public override void OnEnter() => Entered++;
            public override void OnExit() => Exited++;
        }

        [Test]
        public void Tick_FollowsTransitionTable()
        {
            var idle = new CountingState();
            var run = new CountingState();
            bool shouldRun = false;

            var fsm = new StateMachine();
            fsm.SetInitialState(idle);
            fsm.AddTransition(new Transition(run, () => shouldRun));
            fsm.Start();

            Assert.AreSame(idle, fsm.Current, "初始状态应立即生效");
            Assert.AreEqual(1, idle.Entered);

            fsm.Tick();
            Assert.AreSame(idle, fsm.Current, "条件不满足时不应切换");

            shouldRun = true;
            fsm.Tick();
            Assert.AreSame(run, fsm.Current, "条件满足后应切换到目标状态");
            Assert.AreEqual(1, idle.Exited, "旧状态应收到 OnExit");
            Assert.AreEqual(1, run.Entered, "新状态应收到 OnEnter");

            fsm.Shutdown();
        }
    }
}
