using System;

namespace Template.Core.Services
{
    /// <summary>
    /// 全局服务契约。服务不继承 MonoBehaviour —— 生命周期（Init/Dispose）由 Bootstrap 统一管理，
    /// 每帧推进通过 <see cref="ITickable"/> 由 Bootstrap.Update 驱动，场景中不出现满天飞的 Update()。
    /// </summary>
    public interface IGameService : IDisposable
    {
        /// <summary>
        /// 启动阶段调用一次：初始化内部状态、解析依赖（ServiceLocator.Get&lt;T&gt;）。
        /// 调用时机：全部服务已注册完毕、场景对象 Awake 之前（Bootstrap.Awake 内按注册顺序调用），
        /// 因此这里可以安全访问其他服务。
        /// </summary>
        void Init();

        /// <summary>
        /// 释放内部资源（退订、清理等），由 Bootstrap 在进程结束时按注册逆序调用。
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// 需要每帧推进的服务实现此接口，由 Bootstrap.Update 统一驱动。
    /// 每个服务的 Tick 异常会被 Bootstrap 隔离记录，不影响其他服务。
    /// </summary>
    public interface ITickable
    {
        void Tick();
    }
}
