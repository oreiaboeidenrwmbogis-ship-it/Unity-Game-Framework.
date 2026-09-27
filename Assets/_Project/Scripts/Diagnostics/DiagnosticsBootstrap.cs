// 整个 Diagnostics 模块都被这行守卫包住 —— 正式包里本文件编译为空（见 README 阶段 4.11）。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Template.Core.App;
using UnityEngine;

namespace Template.Diagnostics
{
    /// <summary>
    /// 调试模块服务登记入口（登记—装配两段式，与其它模块同模式）：
    /// AfterAssembliesLoaded 登记工厂 → Bootstrap.Awake 装配阶段实例化并纳入统一生命周期。
    ///
    /// 注意：调试服务是**可整体摘除**的可选模块（删目录即可），没有任何模块反向依赖它；
    /// 想接入面板的模块请用 <c>ServiceLocator.TryGet&lt;DiagnosticsService&gt;</c> 注册分节。
    /// </summary>
    internal static class DiagnosticsBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RegisterService()
        {
            Bootstrap.AddModuleService(() => new DiagnosticsService());
        }
    }
}
#endif
