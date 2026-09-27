using Template.Core.Logging;
using Template.Core.Services;
using UnityEngine;

namespace Template.Config
{
    /// <summary>
    /// 配置库装配器：把 Inspector 上指定的 <see cref="ConfigDatabase"/> 交给 <see cref="ConfigService"/>。
    ///
    /// 为什么需要这么一道：模板禁用 <c>Resources.Load</c>，运行期拿到资产只能靠"显式引用"。
    /// 挂启动场景（Boot）是零魔术的做法；阶段 3.3 接入 Addressables 后，配置库改为按 address 加载，
    /// 本组件就可以撤掉（服务自己加载），业务代码不受影响。
    ///
    /// 用法：Boot 场景建一个空物体 → 挂本组件 → 把配置库资产拖到 Database 字段。
    /// </summary>
    [DefaultExecutionOrder(-50)] // 在 Bootstrap(-100) 之后、普通业务脚本之前
    public sealed class ConfigInstaller : MonoBehaviour
    {
        [SerializeField] private ConfigDatabase _database;

        /// <summary>运行时替换（例如异步加载完配置库后由加载流程调用）。</summary>
        public void SetDatabase(ConfigDatabase database) => _database = database;

        private void Start() // 服务访问约定：在 Start（而非 Awake）
        {
            if (_database == null)
            {
                Log.Error("Config", "ConfigInstaller 未指定配置库：请在 Inspector 里拖入 ConfigDatabase 资产");
                return;
            }

            if (ServiceLocator.TryGet<ConfigService>(out ConfigService service))
                service.SetDatabase(_database);
            else
                Log.Error("Config", "ConfigService 未注册（Bootstrap 未自举？）——配置库装配失败");
        }
    }
}
