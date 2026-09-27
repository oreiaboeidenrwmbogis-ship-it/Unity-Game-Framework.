using Template.Core.App;
using UnityEngine;

namespace Template.UI
{
    /// <summary>
    /// UI 模块服务登记入口。
    ///
    /// 为什么不是写进 Bootstrap.RegisterServices：Template.UI 引用 Template.Core，
    /// Core 若反向 new UIService 会构成 asmdef 循环引用（编译失败）。因此各外部模块
    /// 用“登记—装配”两段式注册自己的服务：
    ///     1) 本文件在 AfterAssembliesLoaded（引擎保证早于 Bootstrap 的 BeforeSceneLoad 自举，
    ///        且关掉 Splash Screen 也照常执行）调用 Bootstrap.AddModuleService 登记工厂；
    ///     2) Bootstrap.Awake 装配阶段实例化 UIService，纳入统一 Init/Tick/Dispose 生命周期。
    ///
    /// 新增外部模块照此模式：模块内建一个 *ModuleServiceBootstrap.cs，登记自己的服务工厂。
    /// </summary>
    internal static class UiServiceBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RegisterService()
        {
            Bootstrap.AddModuleService(() => new UIService());
        }
    }
}
