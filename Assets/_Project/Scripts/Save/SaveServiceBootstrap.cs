using Template.Core.App;
using UnityEngine;

namespace Template.Save
{
    /// <summary>
    /// Save 模块服务登记入口 —— 登记—装配两段式（与 UI / Input / Audio / Settings / Config 同模式）：
    /// 本文件在 AfterAssembliesLoaded（早于 Bootstrap 的 BeforeSceneLoad 自举）登记工厂；
    /// Bootstrap.Awake 装配阶段统一实例化并纳入生命周期（Init / Tick / Dispose）。
    ///
    /// 注意：分片注册（RegisterSection）要在各业务系统的 Boot 阶段做，本文件只登记服务本体。
    /// </summary>
    internal static class SaveServiceBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RegisterService()
        {
            Bootstrap.AddModuleService(() => new SaveService());
        }
    }
}
