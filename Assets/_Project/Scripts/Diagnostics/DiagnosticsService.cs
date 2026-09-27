// 整个 Diagnostics 模块都被这行守卫包住 —— 正式包（非编辑器、非开发构建）里本文件编译为空：
// 调试面板连同其字符串/UI 开销一起从包里消失，与 Log.Verbose/Info 用的是同一个条件。
// 因此**引用本模块的程序集必须同样带守卫，或本身是编辑器专用程序集**（如 Template.Demo）。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Text;
using Template.Core.App;
using Template.Core.Logging;
using Template.Core.Services;
using Template.Core.Timing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;

namespace Template.Diagnostics
{
    /// <summary>
    /// 调试面板服务（阶段 4.11）：把前几个阶段散落各处的 <c>Dump()</c> / 计数接口收口成**一个运行期面板**。
    ///
    /// 它提供三样东西：
    /// <list type="number">
    /// <item><b>看得见</b>：FPS / 帧时间 / 最差帧 / 内存 / GC / 各服务状态，一个热键（默认 F1）呼出；</item>
    /// <item><b>摸得着</b>：时间缩放、暂停、日志级别、命令控制台 —— 不改代码就能复现与实验；</item>
    /// <item><b>可扩展</b>：模块注册一个 <see cref="IDiagnosticsSection"/> 就出现在面板里，
    /// 面板本体不认识任何业务模块（<see cref="DiagnosticsSections"/> 只是"预置分节"的装配处）。</item>
    /// </list>
    ///
    /// 剔除策略（路线图 4.11 要求"Release 包完全剔除"）：本模块所有文件都以
    /// <c>#if UNITY_EDITOR || DEVELOPMENT_BUILD</c> 包住 —— 正式包里编译为空程序集，零残留。
    /// 同时它是**可整体摘除**的可选模块：删掉 <c>Scripts/Diagnostics</c> 目录即可，没有任何模块反向依赖它。
    ///
    /// 使用：
    /// <code>
    /// var diag = ServiceLocator.Get&lt;DiagnosticsService&gt;();
    /// diag.Toggle();                                                   // 开关面板（或按 F1）
    /// diag.RegisterSection(new DiagnosticsSection("背包", () =&gt; bag.Describe()));
    /// diag.Console.Register("give", "give &lt;id&gt; —— 发物品", args =&gt; Give(args[0]));
    /// </code>
    /// </summary>
    public sealed class DiagnosticsService : IGameService, ITickable
    {
        /// <summary>日志标签（与各模块一致）。</summary>
        public const string Tag = "Diagnostics";

        /// <summary>循环切换日志级别时的顺序。</summary>
        private static readonly LogLevel[] LevelCycle =
        {
            LogLevel.Verbose, LogLevel.Info, LogLevel.Warning, LogLevel.Error,
        };

        private readonly FrameRateSampler _sampler = new FrameRateSampler();
        private readonly List<IDiagnosticsSection> _sections = new List<IDiagnosticsSection>();
        private readonly StringBuilder _report = new StringBuilder(4096);

        private TimeService _time;
        private GameStateService _gameState;

        private DebugPanel _panel; // 面板视图（自带独立画布，按需创建、常驻、开关只切换激活状态）

        /// <summary>帧率采样器（面板头部与业务监控共用）。</summary>
        public FrameRateSampler Sampler => _sampler;

        /// <summary>命令控制台（注册表 + 回显缓冲，纯逻辑）。</summary>
        public DiagnosticsConsole Console { get; } = new DiagnosticsConsole(maxOutputLines: 12);

        /// <summary>F1/F2 热键开关（游戏想自己用这些键时置 false）。</summary>
        public bool HotkeyEnabled { get; set; } = true;

        /// <summary>面板当前是否显示。</summary>
        public bool IsOpen => _panel != null && _panel.IsVisible;

        /// <summary>已注册分节数。</summary>
        public int SectionCount => _sections.Count;

        public void Init()
        {
            _time = ServiceLocator.Get<TimeService>();
            _gameState = ServiceLocator.Get<GameStateService>();

            DiagnosticsSections.RegisterBuiltIn(this);
            // 控制台回显作为最后一节（Order 999）：命令结果就在报告末尾，面板执行完自动滚到底
            RegisterSection(new DiagnosticsSection("控制台（输入 help 查看命令）", () => Console.OutputText, 999));
            RegisterBuiltInCommands();

            Log.Info(Tag, "调试面板就绪：按 {0} 开关（也可用 {1}）；已接入 {2} 个分节、{3} 条命令",
                DebugPanel.ToggleKeyText, DebugPanel.ToggleKeyAltText, _sections.Count, Console.CommandCount);
        }

        public void Dispose()
        {
            if (_panel != null)
            {
                UnityEngine.Object.Destroy(_panel.gameObject);
                _panel = null;
            }
            _sections.Clear();
        }

        /// <summary>每帧采样真实帧间隔（不受 timeScale 影响：暂停时也要反映真实性能）。</summary>
        public void Tick()
        {
            _sampler.Sample(Time.unscaledDeltaTime);
            if (HotkeyEnabled)
                CheckHotkey();
        }

        // ── 面板开关 ──

        /// <summary>打开面板（幂等）。首次打开时才创建面板本体（没人开过就不占资源）。</summary>
        public bool Open()
        {
            if (IsOpen)
                return true;

            if (_panel == null)
                _panel = DebugPanel.Create(this); // 自带独立画布：不受游戏 UI 的屏幕缩放影响，字体始终清晰

            _panel.Show();
            Log.Info(Tag, "调试面板已打开（{0} 关闭）\n{1}", DebugPanel.ToggleKeyText, BuildSummary());
            return true;
        }

        /// <summary>关闭面板（幂等；只隐藏不销毁，下次打开是同一个实例）。</summary>
        public void Close()
        {
            if (_panel == null)
                return;

            _panel.Hide();
            Log.Verbose(Tag, "调试面板已关闭");
        }

        /// <summary>开关面板（热键与按钮都走它）。</summary>
        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        // ── 分节 ──

        /// <summary>注册分节（按 Order 插入到正确位置；同 Order 保持注册先后）。</summary>
        public void RegisterSection(IDiagnosticsSection section)
        {
            if (section == null)
            {
                Log.Warn(Tag, "RegisterSection: 分节为空");
                return;
            }
            for (int i = 0; i < _sections.Count; i++)
            {
                if (string.Equals(_sections[i].Title, section.Title, StringComparison.Ordinal))
                {
                    Log.Warn(Tag, "RegisterSection: 分节 '{0}' 已存在，替换旧实现", section.Title);
                    _sections.RemoveAt(i);
                    break;
                }
            }

            int index = _sections.Count;
            for (int i = 0; i < _sections.Count; i++)
            {
                if (section.Order < _sections[i].Order)
                {
                    index = i;
                    break;
                }
            }
            _sections.Insert(index, section);
        }

        /// <summary>注销分节（幂等）。</summary>
        public bool UnregisterSection(IDiagnosticsSection section)
        {
            if (section == null)
                return false;
            for (int i = 0; i < _sections.Count; i++)
            {
                if (ReferenceEquals(_sections[i], section))
                {
                    _sections.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 拼装完整报告（面板主体与 <c>dump</c> 命令共用）。
        /// 逐节 try/catch 隔离：某个模块的分节抛异常，只影响它自己那一节。
        /// </summary>
        public string BuildReport()
        {
            _report.Clear();
            for (int i = 0; i < _sections.Count; i++)
            {
                IDiagnosticsSection section = _sections[i];
                try
                {
                    section.Append(_report);
                }
                catch (Exception ex)
                {
                    _report.AppendLine(section.Title + "（本节读取异常：" + ex.Message + "）");
                }
                _report.AppendLine();
            }
            return _report.ToString();
        }

        /// <summary>面板头部的两行实时摘要（刷新频率与面板一致）。</summary>
        public string BuildSummary()
        {
            long managed = GC.GetTotalMemory(false);
            var sb = new StringBuilder(160);
            sb.AppendFormat("FPS {0:F1}（平均帧 {1:F1}ms · 最差帧 {2:F1}ms，近 {3} 帧）",
                _sampler.Fps, _sampler.AverageFrameMs, _sampler.WorstFrameMs, _sampler.SampleCount);
            sb.AppendLine();
            sb.AppendFormat("内存 {0:F1}MB · 托管堆 {1:F1}MB · GC {2} 次 · 时间 {3:F2}× · 当前阶段 {4} · 已渲染 {5} 帧",
                Profiler.GetTotalAllocatedMemoryLong() / 1048576f,
                managed / 1048576f, GC.CollectionCount(0),
                Time.timeScale, _gameState != null ? _gameState.Current.ToString() : "?",
                Time.frameCount);
            return sb.ToString();
        }

        // ── 运行期开关（面板按钮与命令都走这里，逻辑只有一份）──

        /// <summary>设置时间缩放（暂停中会被 TimeService 拒绝并告警）。</summary>
        public void SetTimeScale(float scale)
        {
            if (_time == null)
                return;
            if (_time.IsFrozen)
            {
                Log.Warn(Tag, "时间已冻结（暂停中），时间缩放已忽略 —— 先恢复游戏再改");
                return;
            }
            _time.SetTimeScale(scale);
        }

        /// <summary>
        /// 暂停 / 继续。**走 GameStateService 而不是直接 Freeze**：
        /// 暂停在模板里是"游戏阶段"（Paused）的语义，直接改 timeScale 会让状态机与时间不同步。
        /// </summary>
        public void TogglePause()
        {
            if (_gameState == null)
                return;

            GamePhase phase = _gameState.Current;
            if (phase == GamePhase.Paused)
                _gameState.RequestTransition(GamePhase.Gameplay);
            else if (phase == GamePhase.Gameplay)
                _gameState.RequestTransition(GamePhase.Paused);
            else
                Log.Warn(Tag, "当前阶段 {0} 不支持暂停（暂停是 Gameplay 阶段的语义）", phase);
        }

        /// <summary>当前日志最低输出级别（直接映射 Log.MinLevel）。</summary>
        public LogLevel MinLevel
        {
            get => Log.MinLevel;
            set
            {
                Log.MinLevel = value;
                // 播报用 Warn 而不是 Error：这是"设置变更"不是"出错"（用 Error 会在 Console 里
                // 红成一片，让人以为调试面板自己报错了）。调到 Error 时 Warn 会被自己屏蔽 ——
                // 那也是对的：此刻用户要的就是"除了报错什么都别看"。
                Log.Warn(Tag, "日志级别 → {0}", value);
            }
        }

        /// <summary>循环切换日志级别（Verbose → Info → Warn → Error → Verbose）。</summary>
        public void CycleLogLevel()
        {
            int index = Array.IndexOf(LevelCycle, MinLevel);
            MinLevel = LevelCycle[(index + 1) % LevelCycle.Length];
        }

        /// <summary>热键（每帧轮询）。</summary>
        private void CheckHotkey()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.f1Key.wasPressedThisFrame || keyboard.backquoteKey.wasPressedThisFrame)
                Toggle();
        }

        /// <summary>预置命令：dump / timescale / loglevel（help、clear 由 Console 自带）。</summary>
        private void RegisterBuiltInCommands()
        {
            Console.Register("dump", "dump —— 把完整诊断报告打到 Console",
                _ =>
                {
                    Log.Info(Tag, "诊断报告：\n{0}", BuildReport().TrimEnd());
                    return "报告已输出到 Console";
                });

            Console.Register("timescale", "timescale <倍率> —— 设置时间缩放（如 timescale 0.25）",
                args =>
                {
                    if (args.Length == 0)
                        return "当前时间缩放 " + Time.timeScale.ToString("F2") + "×；用法：timescale 0.25";

                    if (!float.TryParse(args[0], out float scale))
                        return "无法解析倍率 '" + args[0] + "'";

                    SetTimeScale(scale);
                    return "时间缩放 → " + scale.ToString("F2") + "×";
                });

            Console.Register("loglevel", "loglevel <verbose|info|warn|error> —— 切换日志级别",
                args =>
                {
                    if (args.Length == 0)
                        return "当前日志级别 " + MinLevel + "；用法：loglevel warn";

                    switch (args[0].ToLowerInvariant())
                    {
                        case "verbose": MinLevel = LogLevel.Verbose; break;
                        case "info": MinLevel = LogLevel.Info; break;
                        case "warn": MinLevel = LogLevel.Warning; break;
                        case "error": MinLevel = LogLevel.Error; break;
                        default: return "未知级别 '" + args[0] + "'（verbose|info|warn|error）";
                    }
                    return "日志级别 → " + MinLevel;
                });
        }
    }
}
#endif
