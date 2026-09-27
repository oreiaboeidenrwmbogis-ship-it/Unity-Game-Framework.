using System;
using System.Collections.Generic;
using System.Text;
using Template.Core.Logging;
using UnityEngine;

namespace Template.Core.Services
{
    /// <summary>
    /// 全局服务容器：服务注册表 + 查询入口（消灭满天飞的 GetInstance 单例）。
    ///
    /// 规则：
    /// 1. 服务只能由 Bootstrap 在启动阶段注册，注册完毕后自动 Lock()，运行期禁止注册（防止隐式依赖）；
    /// 2. 查询键 = 注册时的泛型类型，接口与具体类皆可 —— 测试时以接口类型注册 Mock 即可替换实现；
    /// 3. 业务代码优先通过构造/接口注入与事件通信，ServiceLocator 是兜底，不要到处 Get；
    /// 4. 程序域重载（或 Enter Play 无域重载模式）时自动重置，不会残留上一次 Play 的服务。
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private static bool _locked;

        public static bool IsLocked => _locked;

        /// <summary>程序域重载 / 每次进入 Play 前自动重置容器（SubsystemRegistration 在每次 Play 开始前执行）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnDomainReload()
        {
            _services.Clear();
            _locked = false;
        }

        /// <summary>
        /// 注册服务：以 typeof(T) 为键（T 可为接口或具体类）。调用方 Get 时必须使用同一类型。
        /// 已注册同类型时会覆盖并告警（通常是重复注册 bug）。
        /// </summary>
        public static void Register<T>(T instance) where T : class
        {
            if (instance == null)
            {
                Log.Error("ServiceLocator", "Register: 服务实例为空 <{0}>", typeof(T).Name);
                return;
            }
            if (_locked)
            {
                Log.Error("ServiceLocator",
                    "Register: 容器已锁定，禁止运行期注册 <{0}>（服务只能在启动阶段由 Bootstrap 注册）", typeof(T).Name);
                return;
            }
            RegisterInternal(instance, typeof(T));
        }

        /// <summary>
        /// 以实例的运行时类型为键注册（键 = instance.GetType()）。
        /// 专供 Bootstrap 注册 IGameService 列表：此时变量的静态类型是接口，
        /// 若走 Register&lt;T&gt; 会以接口类型作键，与调用方 Get&lt;具体类&gt;（如 Get&lt;TimeService&gt;）对不上。
        /// </summary>
        public static void RegisterInstance(object instance)
        {
            if (instance == null)
            {
                Log.Error("ServiceLocator", "RegisterInstance: 服务实例为空");
                return;
            }
            if (_locked)
            {
                Log.Error("ServiceLocator",
                    "RegisterInstance: 容器已锁定，禁止运行期注册 <{0}>（服务只能在启动阶段由 Bootstrap 注册）",
                    instance.GetType().Name);
                return;
            }
            RegisterInternal(instance, instance.GetType());
        }

        private static void RegisterInternal(object instance, Type key)
        {
            if (_services.ContainsKey(key))
                Log.Warn("ServiceLocator", "Register: <{0}> 重复注册，覆盖旧实例", key.Name);

            _services[key] = instance;
        }

        /// <summary>获取服务。未注册时输出错误并抛异常 —— 启动序 bug 越早暴露越好。</summary>
        public static T Get<T>() where T : class
        {
            if (_services.TryGetValue(typeof(T), out object service))
                return (T)service;

            Log.Error("ServiceLocator", "Get: 未注册服务 <{0}>。检查 Bootstrap 注册列表；场景脚本请在 Start（而非 Awake）访问服务", typeof(T).Name);
            throw new InvalidOperationException(
                $"[ServiceLocator] 未注册服务 <{typeof(T).Name}>。请在 Bootstrap.RegisterServices 中注册，或检查访问时机。");
        }

        /// <summary>安全的查询：不抛异常，常用于可选依赖。</summary>
        public static bool TryGet<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out object obj))
            {
                service = (T)obj;
                return true;
            }
            service = null;
            return false;
        }

        /// <summary>锁定容器（Bootstrap 完成初始化后调用）。仅测试与框架代码需要显式操作。</summary>
        public static void Lock() => _locked = true;

        /// <summary>已注册服务数（调试面板/启动自检用）。</summary>
        public static int ServiceCount => _services.Count;

        /// <summary>已注册服务的名称清单快照（诊断用，如 "12 项：TimeService、GameStateService、…"）。</summary>
        public static string Dump()
        {
            if (_services.Count == 0)
                return "（无已注册服务）";

            var sb = new StringBuilder();
            sb.Append(_services.Count).Append(" 项：");
            bool first = true;
            foreach (Type key in _services.Keys)
            {
                if (!first)
                    sb.Append('、');
                first = false;
                sb.Append(key.Name);
            }
            return sb.ToString();
        }
    }
}
