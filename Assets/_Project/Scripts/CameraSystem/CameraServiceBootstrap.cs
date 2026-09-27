using Template.Core.App;
using UnityEngine;

namespace Template.CameraSystem
{
    /// <summary>
    /// Camera 模块服务登记入口 —— 登记—装配两段式（与其它模块同模式）：
    /// 本文件在 AfterAssembliesLoaded（早于 Bootstrap 的 BeforeSceneLoad 自举）登记工厂；
    /// Bootstrap.Awake 装配阶段统一实例化并纳入生命周期（Init / Tick / Dispose）。
    /// </summary>
    internal static class CameraServiceBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RegisterService()
        {
            Bootstrap.AddModuleService(() => new CameraService());
        }
    }
}
