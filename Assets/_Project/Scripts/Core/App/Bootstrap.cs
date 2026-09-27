using System;
using System.Collections.Generic;
using System.Text;
using Template.Core.Logging;
using Template.Core.Pooling;
using Template.Core.Services;
using Template.Core.Timing;
using UnityEngine;

namespace Template.Core.App
{
    /// <summary>
    /// 应用启动器（[Core Bootstrap]，进程内唯一常驻对象，DontDestroyOnLoad）。
    ///
    /// 职责：
    /// 1. 自举：程序域加载后、任何场景对象 Awake 之前（BeforeSceneLoad）创建本体并完成服务注册与初始化；
    /// 2. 两阶段初始化：先全量注册服务（顺序即依赖顺序，Time 最先），再按序 Init（此时可安全 Get 任何服务）；
    /// 3. 每帧驱动 ITickable 服务（异常隔离：单个服务崩溃不影响其他服务）；
    /// 4. 进程/Play 结束逆序 Dispose。
    ///
    /// 场景内代码约定：在 Start（而非 Awake）中 ServiceLocator.Get&lt;T&gt;() 访问服务。
    /// 新增 Core 服务：在 <see cref="RegisterServices"/> 末尾追加；新增外部模块服务：见 <see cref="AddModuleService"/>。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class Bootstrap : MonoBehaviour
    {
        /// <summary>常驻单例。场景代码一般用不到它 —— 请通过 ServiceLocator.Get&lt;T&gt;() 获取服务。</summary>
        public static Bootstrap Instance { get; private set; }

        private readonly List<IGameService> _services = new List<IGameService>();

        // 外部模块（UI/输入/音频/游戏系统，各自独立 asmdef 并引用 Core）的服务工厂。
        // 原因：Core 不能反向引用上层模块（asmdef 循环依赖），模块服务不在硬编码注册表里，
        // 而由各模块在“更早的初始化档位”登记，Bootstrap 装配时统一实例化 —— 见 AddModuleService。
        private static readonly List<Func<IGameService>> _moduleFactories = new List<Func<IGameService>>();

        /// <summary>
        /// 自举：BeforeSceneLoad 触发（早于场景内任何组件的 Awake/Start）。
        /// 因此无需专门的 _Boot 场景也能在任何场景直接 Play —— 框架先于场景就绪。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoBoot()
        {
            if (Instance != null)
                return; // 已自举（防御重复）

            var go = new GameObject("[Core Bootstrap]");
            go.AddComponent<Bootstrap>();
        }

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject); // 防御：同一帧出现第二个 Bootstrap
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            RegisterServices();     // Core 服务（顺序即依赖顺序，Time 最先）
            AppendModuleServices(); // 外部模块登记的服务（见 AddModuleService）
            InitializeServices();   // 两阶段第二段：全量注册完成后按序 Init
            ServiceLocator.Lock();
            LogBootstrapSummary();
        }

        /// <summary>
        /// 模块服务登记（供外部程序集调用，避免 Core → 模块的循环引用）。
        /// 调用时机：在模块的静态初始化里，且必须早于 Bootstrap 的 BeforeSceneLoad 自举 ——
        /// 请用 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        /// （更早档位亦可；勿用同档的 BeforeSceneLoad，档内顺序无保证）。
        /// 登记的工厂在 Bootstrap.Awake 装配阶段被实例化并纳入统一生命周期（Init/Tick/逆序 Dispose）。
        /// </summary>
        public static void AddModuleService(Func<IGameService> factory)
        {
            if (factory == null)
            {
                Log.Error("Bootstrap", "AddModuleService: 工厂为空（模块登记代码有误）");
                return;
            }
            _moduleFactories.Add(factory);
        }

        /// <summary>把外部模块登记的服务工厂实例化并追加进生命周期列表。</summary>
        private void AppendModuleServices()
        {
            if (_moduleFactories.Count == 0)
                return;
            for (int i = 0; i < _moduleFactories.Count; i++)
            {
                try
                {
                    AddService(_moduleFactories[i]());
                }
                catch (Exception ex)
                {
                    Log.Error("Bootstrap", "模块服务工厂执行失败: {0}", ex);
                    throw; // 启动阶段失败立即暴露（fail fast）
                }
            }
            _moduleFactories.Clear(); // 装配即清空：防止无域重载模式下重复 Play 时二次登记堆积
        }

        /// <summary>
        /// 服务注册表 —— 顺序即依赖顺序（Time 最先：多数服务的延迟回收/超时依赖它）。
        /// Core 程序集内的服务在此追加。
        /// </summary>
        private void RegisterServices()
        {
            AddService(new TimeService());
            AddService(new GameStateService());
            AddService(new SceneFlowService());
            AddService(new PoolService());

            // 说明：外部模块（Template.UI 等）的服务不写在这里（会造成 Core → 模块的
            // 循环程序集引用），由模块内静态初始化调用 AddModuleService 登记（见 AppendModuleServices）。
            // 若两个模块服务之间存在 Init 顺序依赖，在该模块的工厂注释里写明并人工保证登记顺序。
        }

        /// <summary>
        /// 加入生命周期列表（决定 Init/Tick/Dispose 顺序），并注册进 ServiceLocator 供全局查询。
        /// 注意必须用 RegisterInstance（键 = 运行时类型）：列表变量的静态类型是 IGameService，
        /// 若以接口类型注册，场景代码 Get&lt;TimeService&gt;() 将永远查不到。
        /// </summary>
        private void AddService(IGameService service)
        {
            _services.Add(service);
            ServiceLocator.RegisterInstance(service);
        }

        /// <summary>两阶段第二段：按注册顺序 Init（全部已注册，Init 里可安全解析任何依赖）。</summary>
        private void InitializeServices()
        {
            for (int i = 0; i < _services.Count; i++)
            {
                IGameService service = _services[i];
                try
                {
                    service.Init();
                }
                catch (Exception ex)
                {
                    Log.Error("Bootstrap", "服务 Init 异常 <{0}>: {1}", service.GetType().Name, ex);
                    throw; // 启动阶段失败要立即暴露（fail fast），不允许带病运行
                }
            }
        }

        private void Update()
        {
            for (int i = 0; i < _services.Count; i++)
            {
                IGameService service = _services[i];
                if (service is ITickable tickable)
                {
                    try
                    {
                        tickable.Tick();
                    }
                    catch (Exception ex)
                    {
                        // 运行期异常隔离：记录并跳过，不让一个服务拖垮整帧
                        Log.Error("Bootstrap", "服务 Tick 异常 <{0}>: {1}", service.GetType().Name, ex);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;
            Instance = null;

            // 逆序 Dispose（先销毁依赖方，再销毁被依赖方）
            for (int i = _services.Count - 1; i >= 0; i--)
            {
                IGameService service = _services[i];
                try
                {
                    service.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error("Bootstrap", "服务 Dispose 异常 <{0}>: {1}", service.GetType().Name, ex);
                }
            }
            _services.Clear();
        }

        private void LogBootstrapSummary()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _services.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(_services[i].GetType().Name);
            }
            Log.Info("Bootstrap", "核心框架就绪：已注册 {0} 项服务 [{1}]（事件中心/服务定位器/日志已可用）",
                _services.Count, sb);
        }
    }
}
