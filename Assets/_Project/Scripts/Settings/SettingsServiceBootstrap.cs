using Template.Core.App;
using UnityEngine;

namespace Template.Settings
{
    /// <summary>
    /// Settings 模块服务登记入口 —— 登记—装配两段式（与 Template.UI / Input / Audio 同模式）：
    /// 本文件在 AfterAssembliesLoaded（早于 Bootstrap 的 BeforeSceneLoad 自举）登记工厂；
    /// Bootstrap.Awake 装配阶段统一实例化并纳入生命周期（Init / Tick / Dispose）。
    ///
    /// 与其他模块的 Init 顺序无关：设置是**懒加载**的，任何服务在自己的 Init 里 Get 都能拿到
    /// 已加载的值（首访触发加载），无需人工保证本服务先 Init。
    /// </summary>
    internal static class SettingsServiceBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RegisterService()
        {
            Bootstrap.AddModuleService(() => new SettingsService());
        }
    }
}
