using Template.Core.App;
using UnityEngine;

namespace Template.Input
{
    /// <summary>
    /// Input 模块服务登记入口 —— 登记—装配两段式（与 Template.UI 的 UiServiceBootstrap 同模式，
    /// 详见 Bootstrap.AddModuleService 注释）：本文件在 AfterAssembliesLoaded（早于 Bootstrap 的
    /// BeforeSceneLoad 自举）登记工厂；Bootstrap.Awake 装配阶段统一实例化并纳入生命周期。
    /// </summary>
    internal static class InputServiceBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RegisterService()
        {
            Bootstrap.AddModuleService(() => new InputService());
        }
    }
}
