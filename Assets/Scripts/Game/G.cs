using System;
using Template.Assets;
using Template.Audio;
using Template.CameraSystem;
using Template.Config;
using Template.Core.App;
using Template.Core.Eventing;
using Template.Core.Logging;
using Template.Core.Pooling;
using Template.Core.Services;
using Template.Core.Timing;
using Template.Input;
using Template.Save;
using Template.Settings;
using Template.UI;
using Template.Vfx;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Template.Diagnostics; // 调试模块只在编辑器/开发构建里存在，所以这个 using 也必须包在守卫里
#endif

namespace Jam
{
    /// <summary>
    /// 底座服务的<b>短入口</b>：所有 <c>using Template.*</c> 都收在这一个文件里，
    /// 你的游戏脚本从此**一个 using 都不用写**（只要把脚本放进 namespace Jam，或者写一行 using Jam）。
    ///
    /// 用法：
    /// <code>
    /// using UnityEngine;          // 唯一需要的 using（MonoBehaviour / Vector3 等）
    ///
    /// namespace Jam               // 和本文件同一个命名空间 → G 直接可见
    /// {
    ///     public sealed class Enemy : MonoBehaviour
    ///     {
    ///         private void Start()
    ///         {
    ///             G.Info("Enemy", "敌人出现");
    ///             G.Pools.Spawn("bullet", transform.position, Quaternion.identity);
    ///             G.Audio.PlaySoundAt(hitClip, transform.position);
    ///             G.Time.Delay(2f, () => Destroy(gameObject));
    ///             G.Publish(new ScoreGainedEvent(10));
    ///         }
    ///     }
    /// }
    /// </code>
    ///
    /// 两条注意：
    /// <list type="bullet">
    /// <item><b>别把服务存进 static 字段</b>：无域重载（Enter Play Mode Options）时静态字段会跨 Play 存活，
    /// 里面会留着上一次 Play 的旧实例。高频使用时存到<b>实例字段</b>（或方法开头的局部变量）里。</item>
    /// <item>想改名字/删掉它都行：把 <c>G</c> 换成你喜欢的名字（如 <c>Kit</c>）全局替换即可；
    /// 不用这套入口也完全可以，直接 <c>ServiceLocator.Get&lt;AudioService&gt;()</c> 一样能用。</item>
    /// </list>
    /// </summary>
    public static class G
    {
        // ── 服务（每次访问 = 一次容器查询，很便宜；要每帧用就自己存到字段里）──

        public static TimeService Time => ServiceLocator.Get<TimeService>();
        public static PoolService Pools => ServiceLocator.Get<PoolService>();
        public static UIService Ui => ServiceLocator.Get<UIService>();
        public static AudioService Audio => ServiceLocator.Get<AudioService>();
        public static SaveService Save => ServiceLocator.Get<SaveService>();
        public static ConfigService Config => ServiceLocator.Get<ConfigService>();
        public static SettingsService Settings => ServiceLocator.Get<SettingsService>();
        public static GameStateService State => ServiceLocator.Get<GameStateService>();
        public static SceneFlowService Scene => ServiceLocator.Get<SceneFlowService>();
        public static AssetService Assets => ServiceLocator.Get<AssetService>();
        public static InputService Input => ServiceLocator.Get<InputService>();
        public static CameraService Cam => ServiceLocator.Get<CameraService>();
        public static VfxService Vfx => ServiceLocator.Get<VfxService>();

        /// <summary>取任意服务（等价于 ServiceLocator.Get&lt;T&gt;()，这里只是省得再写一个 using）。</summary>
        public static T Get<T>() where T : class => ServiceLocator.Get<T>();

        /// <summary>取可选服务（缺席不抛异常，返回 false）。</summary>
        public static bool TryGet<T>(out T service) where T : class => ServiceLocator.TryGet(out service);

        // ── 事件：读写都走它，订阅记得退订（见 G.Unsubscribe）──

        public static void Publish<T>(T evt) where T : struct => EventBus<T>.Publish(evt);

        /// <summary>
        /// 订阅并返回退订令牌。注意它的类型是**嵌套在泛型类里的** <c>EventBus&lt;T&gt;.Subscription</c>
        /// （事件总线里没有顶层的 Subscription 类型），所以用 <c>var</c> 接最省事：
        /// <c>using var sub = G.Subscribe&lt;E&gt;(OnE);</c> —— 作用域结束自动退订；
        /// 或存成字段，在 OnDestroy 里 <c>sub.Dispose()</c>（也可以直接 <c>G.Unsubscribe&lt;E&gt;(OnE)</c>）。
        /// </summary>
        public static EventBus<T>.Subscription Subscribe<T>(Action<T> handler) where T : struct
            => EventBus<T>.Subscribe(handler);

        public static void Unsubscribe<T>(Action<T> handler) where T : struct
            => EventBus<T>.Unsubscribe(handler);

        // ── 日志转发 ──
        // ⚠ `#line hidden` 不能删：日志门面靠"栈上没有带源码信息的转发帧"来实现
        //    Console 双击跳到调用点 / 正文自动附带 (文件.cs:行号)。
        //    少了它，双击会停在本文件的这一行，而不是你自己的代码（CLAUDE.md 的已知坑里写了原因）。
#line hidden
        public static void Verbose(string tag, string format, params object[] args) => Log.Verbose(tag, format, args);
        public static void Info(string tag, object message) => Log.Info(tag, message);
        public static void Info(string tag, string format, params object[] args) => Log.Info(tag, format, args);
        public static void Warn(string tag, object message) => Log.Warn(tag, message);
        public static void Warn(string tag, string format, params object[] args) => Log.Warn(tag, format, args);
        public static void Error(string tag, object message) => Log.Error(tag, message);
        public static void Error(string tag, string format, params object[] args) => Log.Error(tag, format, args);
        public static void Error(string tag, Exception exception) => Log.Error(tag, exception);
#line default

        // ── 调试面板（Play 中按 F1）：调用点不用写 #if，守卫在下面这两段里面 ──

        /// <summary>给自己的系统在 F1 面板加一节（内容随便写什么）；调试模块缺席时返回 false。</summary>
        public static bool PanelSection(string title, Func<string> content, int order = 120)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (ServiceLocator.TryGet(out DiagnosticsService diag))
            {
                diag.RegisterSection(new DiagnosticsSection(title, content, order));
                return true;
            }
#endif
            return false;
        }

        /// <summary>给 F1 面板的命令控制台加一条作弊命令（如 <c>G.Cheat("give", "give &lt;n&gt;", a =&gt; "已加钱")</c>）。</summary>
        public static bool Cheat(string name, string help, Func<string[], string> handler)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (ServiceLocator.TryGet(out DiagnosticsService diag))
                return diag.Console.Register(name, help, handler);
#endif
            return false;
        }
    }
}
