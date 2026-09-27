namespace Template.Core.Fsm
{
    /// <summary>
    /// 状态接口（状态机节点）。所有状态转换都经所属状态机的 ChangeState —— 禁止状态内部互相 new。
    /// </summary>
    public interface IState
    {
        /// <summary>进入状态（切换瞬间调用一次；在此做入场：重置数据、订阅、播放动画等）。</summary>
        void OnEnter();

        /// <summary>退出状态（切换瞬间调用一次；在此做出场清理，保证下次进入是“干净的”）。</summary>
        void OnExit();

        /// <summary>每帧由状态机驱动（仅当前状态被驱动）。</summary>
        void Tick();
    }

    /// <summary>
    /// 状态基类：只需覆写关心的虚方法，避免空实现噪音。
    /// </summary>
    public abstract class StateBase : IState
    {
        public virtual void OnEnter() { }
        public virtual void OnExit() { }
        public virtual void Tick() { }
    }
}
