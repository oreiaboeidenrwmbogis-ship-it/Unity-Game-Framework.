// 整个 Diagnostics 模块都被这行守卫包住 —— 正式包里本文件编译为空（见 README 阶段 4.11）。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Text;
using Template.Assets;
using Template.Audio;
using Template.CameraSystem;
using Template.Config;
using Template.Core.Eventing;
using Template.Core.Pooling;
using Template.Core.Services;
using Template.Core.Timing;
using Template.Input;
using Template.Save;
using Template.Settings;
using Template.Vfx;

namespace Template.Diagnostics
{
    /// <summary>
    /// 预置分节的装配处 —— 把各模块的公开统计接口翻译成面板上的一节。
    ///
    /// 设计要点（否则这一堆引用会很脏）：
    /// <list type="bullet">
    /// <item>每个分节都是<b>可选</b>的：服务缺席（模块被摘除 / 未注册）就静默跳过，
    /// 面板照常工作，不用任何 <c>#if</c> 或空判断散落在业务代码里；</item>
    /// <item>这里只读公开 API（<c>Dump()</c> / 计数属性），不碰任何模块内部状态 ——
    /// 与"业务代码只调 Dump 排查"是同一个契约；</item>
    /// <item>想给自己游戏的系统加一节，不需要改本文件：在系统初始化时
    /// <c>diag.RegisterSection(new DiagnosticsSection("我的系统", () =&gt; ...))</c> 即可。</item>
    /// </list>
    /// </summary>
    internal static class DiagnosticsSections
    {
        public static void RegisterBuiltIn(DiagnosticsService diag)
        {
            // ── Core：对象池 / 定时器 / 事件总线 / 服务容器 ──

            Add<PoolService>(diag, "对象池", 10, pools =>
                "共 " + pools.PoolCount + " 个池，活跃 " + pools.ActiveTotal + " 个实例\n" + pools.Dump());

            Add<TimeService>(diag, "定时器", 15, time =>
                "待处理 " + time.TimerCount + " 个（Scaled 时钟随暂停一起冻结）");

            diag.RegisterSection(new DiagnosticsSection("事件总线", () =>
                "监听者 " + EventBus.TotalHandlerCount + " 个 / " + EventBus.TrackedEventTypeCount
                + " 种类型，累计发布 " + EventBus.TotalPublishCount + " 次\n" + EventBus.DumpHandlers(), 20));

            diag.RegisterSection(new DiagnosticsSection("服务容器", () => ServiceLocator.Dump(), 25));

            // ── 各模块 ──

            Add<ConfigService>(diag, "配置表", 30, config =>
            {
                if (!config.IsReady || config.Database == null)
                    return "未装配（ConfigService.SetDatabase 之前）";

                var tables = config.Database.Tables;
                var sb = new StringBuilder();
                sb.Append(tables.Count).Append(" 张表");
                for (int i = 0; i < tables.Count; i++)
                {
                    sb.Append(i == 0 ? "：" : "、");
                    sb.Append(tables[i] != null ? tables[i].name : "(空)");
                }
                return sb.ToString();
            });

            Add<SaveService>(diag, "存档", 40, save =>
            {
                int[] slots = save.ListSlots();
                var sb = new StringBuilder();
                sb.Append("存储 ").Append(save.StorageDescription).Append("，").Append(slots.Length).Append(" 个槽位");
                for (int i = 0; i < slots.Length; i++)
                {
                    sb.AppendLine();
                    sb.Append(save.TryGetSlotInfo(slots[i], out SaveSlotInfo info) ? info.ToString() : "槽位 " + slots[i] + "：读取失败");
                }
                return sb.ToString();
            });

            Add<AssetService>(diag, "资源账本", 45, assets =>
                "已加载 " + assets.LoadedCount + " 项\n" + assets.Dump());

            Add<VfxService>(diag, "特效", 50, vfx =>
                "活跃 " + vfx.ActiveCount + " 个\n" + vfx.Dump());

            Add<AudioService>(diag, "音频", 55, audio =>
                "Master " + Percent(audio.GetVolume(AudioBus.Master))
                + " / BGM " + Percent(audio.GetVolume(AudioBus.Bgm))
                + " / SFX " + Percent(audio.GetVolume(AudioBus.Sfx))
                + " / Voice " + Percent(audio.GetVolume(AudioBus.Voice))
                + "，BGM " + (audio.IsBgmPlaying ? "播放中" : "停止"));

            Add<SettingsService>(diag, "设置", 60, settings =>
                "已加载 " + settings.Count + " 项（改动合并落盘，见 persistentDataPath/settings.json）");

            Add<InputService>(diag, "输入", 65, input =>
                "方案 " + input.CurrentScheme + "，内置 UI 映射常驻启用"
                + "（UI Cancel → UIService.TryBack，Gameplay 映射随暂停自动屏蔽）");

            Add<CameraService>(diag, "相机", 70, camera =>
                "跟随相机 " + camera.CameraCount + " 台，创伤 " + camera.Trauma.ToString("F2")
                + "，主相机 " + (camera.MainCamera != null ? camera.MainCamera.name : "未接管"));
        }

        /// <summary>服务存在才注册分节（调试模块对业务模块是"只读旁观者"，缺席即跳过）。</summary>
        private static void Add<TService>(DiagnosticsService diag, string title, int order,
            Func<TService, string> describe) where TService : class
        {
            if (!ServiceLocator.TryGet(out TService service))
                return;

            diag.RegisterSection(new DiagnosticsSection(title, () => describe(service), order));
        }

        private static string Percent(float linear) => (linear * 100f).ToString("F0") + "%";
    }
}
#endif
