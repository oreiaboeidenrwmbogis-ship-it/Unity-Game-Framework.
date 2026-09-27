using System;
using System.Collections.Generic;
using Template.Core.Logging;

namespace Template.Core.Fsm
{
    /// <summary>
    /// 转换规则（表驱动）：当 Condition() 为真（且 From 匹配当前状态，null = 任意来源）时切换到 Target。
    /// 规则在状态机中按注册顺序求值，每帧至多执行一次转换，防止状态间连锁震荡。
    /// </summary>
    public sealed class Transition
    {
        public IState From;
        public IState Target;
        public Func<bool> Condition;

        /// <param name="target">目标状态（必填）。</param>
        /// <param name="condition">触发条件（必填；外部状态/数据由闭包捕获）。</param>
        /// <param name="from">仅当当前状态是 from 时生效；null 表示任意当前状态。</param>
        public Transition(IState target, Func<bool> condition, IState from = null)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            From = from;
        }
    }

    /// <summary>
    /// 通用有限状态机 —— 玩家状态 / 敌人 AI / 复杂 UI 面板状态共用一套。
    ///
    /// 用法：
    ///     var fsm = new StateMachine();
    ///     fsm.AddState? (不需要注册表，Transition 直接引用状态实例即可)
    ///     fsm.Start(new IdleState());
    ///     fsm.AddTransition(new Transition(runState, () => Input.GetKey(KeyCode.LeftShift)));
    ///     fsm.Tick();  // 每帧驱动
    ///
    /// 分层（Hierarchical FSM）：把“内含子状态机的状态”作为普通状态实例组合即可 ——
    /// 子状态机是状态的一个字段，由父状态 OnEnter 启动、OnExit 停止、Tick 转发。
    /// 注意：Changed 是普通 C# 事件，订阅者不得抛异常；长生命周期订阅方需自行退订。
    /// </summary>
    public sealed class StateMachine
    {
        private readonly List<Transition> _transitions = new List<Transition>();
        private IState _current;
        private IState _initial;
        private bool _started;

        /// <summary>当前状态（未启动时为 null）。</summary>
        public IState Current => _current;

        /// <summary>状态切换事件：(from, to)。初始状态进入不触发（Start 语义）。</summary>
        public event Action<IState, IState> Changed;

        public StateMachine() { }

        /// <summary>启动：进入初始状态。重复调用被忽略（先 Shutdown 再 Start 可重启）。</summary>
        public void Start()
        {
            if (_started)
                return;
            if (_initial == null)
            {
                Log.Error("Fsm", "StateMachine.Start: 未设置初始状态");
                return;
            }
            _started = true;
            _current = _initial;
            _current.OnEnter();
        }

        /// <summary>设置初始状态（Start 前调用）。</summary>
        public void SetInitialState(IState state)
        {
            _initial = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>注册一条转换规则。</summary>
        public void AddTransition(Transition transition)
        {
            if (transition == null)
                throw new ArgumentNullException(nameof(transition));
            _transitions.Add(transition);
        }

        /// <summary>每帧驱动：先求值转换条件（每帧至多切换一次），再驱动当前状态。</summary>
        public void Tick()
        {
            if (!_started || _current == null)
                return;

            for (int i = 0; i < _transitions.Count; i++)
            {
                Transition t = _transitions[i];
                if (t.From != null && !ReferenceEquals(t.From, _current))
                    continue;
                if (!t.Condition())
                    continue;
                ChangeState(t.Target);
                return; // 每帧一次转换，下一帧继续评估新状态的条件
            }

            _current.Tick();
        }

        /// <summary>外部驱动切换（如输入）。跳过条件表，直接切。</summary>
        public void ChangeState(IState next)
        {
            if (!_started || _current == null)
            {
                Log.Error("Fsm", "ChangeState: 状态机未启动");
                return;
            }
            if (next == null)
            {
                Log.Error("Fsm", "ChangeState: 目标状态为 null");
                return;
            }
            if (ReferenceEquals(next, _current))
                return;

            IState previous = _current;
            previous.OnExit();
            _current = next;
            _current.OnEnter();

            Action<IState, IState> changed = Changed;
            if (changed != null)
                changed(previous, _current); // 契约：订阅者不得抛异常（可改用全局事件总线隔离）
        }

        /// <summary>当前状态是否为指定类型（免引用比较样板）。</summary>
        public bool IsInState<T>() where T : IState => _current is T;

        /// <summary>停机：退出当前状态并复位（可再次 Start）。</summary>
        public void Shutdown()
        {
            if (!_started)
                return;
            _started = false;
            if (_current != null)
            {
                _current.OnExit();
                _current = null;
            }
            _transitions.Clear();
        }
    }
}
