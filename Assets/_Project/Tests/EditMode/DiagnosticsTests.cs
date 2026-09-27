using System.Text;
using NUnit.Framework;
using Template.Diagnostics;

namespace Template.Tests
{
    /// <summary>
    /// 调试面板的纯逻辑部分（阶段 4.11）：帧率采样器 / 命令控制台 / 分节格式。
    /// 面板本体（uGUI 搭建）不进单测 —— 它是视图；这里测的是"数字算得对不对、命令解析得对不对"。
    /// </summary>
    public class FrameRateSamplerTests
    {
        private const float SixtyFps = 1f / 60f;
        private const float ThirtyFps = 1f / 30f;

        [Test]
        public void Sample_SteadySixtyFps_ReportsSixty()
        {
            var sampler = new FrameRateSampler(120);
            for (int i = 0; i < 120; i++)
                sampler.Sample(SixtyFps);

            Assert.IsTrue(sampler.IsStable, "填满窗口后读数应稳定");
            Assert.AreEqual(60f, sampler.Fps, 0.5f);
            Assert.AreEqual(16.67f, sampler.AverageFrameMs, 0.1f);
        }

        [Test]
        public void Sample_BeforeWindowFilled_UsesAvailableFrames()
        {
            var sampler = new FrameRateSampler(120);
            sampler.Sample(SixtyFps);

            Assert.AreEqual(1, sampler.SampleCount, "窗口没满也要能给读数（面板刚打开时不能是 0）");
            Assert.IsFalse(sampler.IsStable);
            Assert.AreEqual(60f, sampler.Fps, 0.5f);
        }

        [Test]
        public void WorstFrameMs_CatchesSingleSpike_ThatAverageHides()
        {
            var sampler = new FrameRateSampler(120);
            for (int i = 0; i < 119; i++)
                sampler.Sample(SixtyFps);
            sampler.Sample(0.1f); // 一帧 100ms 的尖峰（GC / 加载 / 实例化）

            Assert.AreEqual(100f, sampler.WorstFrameMs, 0.5f, "最差帧必须看见尖峰");
            Assert.AreEqual(10f, sampler.WorstFps, 0.5f);
            Assert.Greater(sampler.Fps, 55f, "平均值会被尖峰轻微拉低，但不该崩塌 —— 两者结合才看得出问题类型");
        }

        [Test]
        public void Sample_WindowSlides_ForgetsOldFrames()
        {
            var sampler = new FrameRateSampler(120);
            for (int i = 0; i < 120; i++)
                sampler.Sample(SixtyFps);
            Assert.AreEqual(60f, sampler.Fps, 0.5f);

            for (int i = 0; i < 120; i++)
                sampler.Sample(ThirtyFps); // 旧帧被全部挤出窗口

            Assert.AreEqual(30f, sampler.Fps, 0.5f);
            Assert.AreEqual(33.33f, sampler.WorstFrameMs, 0.1f);
        }

        [Test]
        public void Sample_InvalidDelta_IsIgnored()
        {
            var sampler = new FrameRateSampler(10);
            sampler.Sample(0f);
            sampler.Sample(-1f);
            sampler.Sample(float.NaN);              // NaN 与任何数比较都是 false —— 只写 `<= 0f` 会漏掉它
            sampler.Sample(float.PositiveInfinity); // ±∞ 会一瞬间把平均值与最差值污染成 NaN/∞
            sampler.Sample(float.NegativeInfinity);

            Assert.AreEqual(0, sampler.SampleCount, "无效帧间隔不参与统计（否则会污染平均与最差值）");
            Assert.AreEqual(0f, sampler.Fps, "读数必须是 0，而不是 NaN（面板显示 FPS NaN 等于调试工具自己坏了）");
            Assert.AreEqual(0f, sampler.WorstFrameMs);
        }

        [Test]
        public void Reset_ClearsWindow()
        {
            var sampler = new FrameRateSampler(10);
            for (int i = 0; i < 10; i++)
                sampler.Sample(ThirtyFps);

            sampler.Reset();

            Assert.AreEqual(0, sampler.SampleCount);
            Assert.AreEqual(0f, sampler.Fps);
            Assert.AreEqual(0f, sampler.WorstFrameMs);
        }

        [Test]
        public void Ctor_InvalidWindow_ClampsToOne()
        {
            var sampler = new FrameRateSampler(0);
            Assert.AreEqual(1, sampler.Window);

            sampler.Sample(SixtyFps);
            sampler.Sample(ThirtyFps); // 窗口只有 1 帧：后到的覆盖先到的
            Assert.AreEqual(30f, sampler.Fps, 0.5f);
        }
    }

    /// <summary>
    /// 命令控制台：模板只提供"注册—解析—执行"机制（玩法命令由游戏层注册，模板不预写）。
    /// 这里盯住三件事：参数传对了、未知命令不炸、单条命令抛异常不拖垮面板。
    /// </summary>
    public class DiagnosticsConsoleTests
    {
        [Test]
        public void Execute_RegisteredCommand_PassesArgsAndReturnsResult()
        {
            var console = new DiagnosticsConsole();
            string[] received = null;
            console.Register("give", "give <id> [n]", args =>
            {
                received = args;
                return "已发放 " + args[0];
            });

            string result = console.Execute("give sword 3");

            Assert.AreEqual("已发放 sword", result);
            CollectionAssert.AreEqual(new[] { "sword", "3" }, received, "第 0 个 token 是命令名，参数从 1 开始");
            StringAssert.Contains("已发放 sword", console.OutputText, "结果要回显到输出区，面板才看得见");
            StringAssert.Contains("> give sword 3", console.OutputText, "输入本身也要留痕");
        }

        [Test]
        public void Execute_IsCaseInsensitive()
        {
            var console = new DiagnosticsConsole();
            console.Register("Give", "help", _ => "ok");

            Assert.AreEqual("ok", console.Execute("GIVE"));
            Assert.AreEqual("ok", console.Execute("give"));
        }

        [Test]
        public void Execute_UnknownCommand_ReturnsHintInsteadOfThrowing()
        {
            var console = new DiagnosticsConsole();

            string result = console.Execute("nonsense");

            StringAssert.Contains("未找到命令", result);
            StringAssert.Contains("help", result, "要顺便告诉玩家怎么查命令");
        }

        [Test]
        public void Execute_EmptyOrWhitespace_IsIgnored()
        {
            var console = new DiagnosticsConsole();
            console.Write("原有内容");

            Assert.AreEqual(string.Empty, console.Execute(null));
            Assert.AreEqual(string.Empty, console.Execute("   "));
            Assert.AreEqual("原有内容", console.OutputText, "空输入不该污染输出区");
        }

        [Test]
        public void Execute_HandlerThrows_IsCaughtAndReported()
        {
            var console = new DiagnosticsConsole();
            console.Register("boom", "boom —— 故意报错", _ => throw new System.InvalidOperationException("炸了"));

            string result = console.Execute("boom");

            StringAssert.Contains("执行异常", result);
            StringAssert.Contains("炸了", result);
        }

        [Test]
        public void Execute_HandlerReturnsNull_WritesNothingExtra()
        {
            var console = new DiagnosticsConsole();
            console.Register("quiet", "quiet", _ => null);

            Assert.AreEqual(string.Empty, console.Execute("quiet"));
            StringAssert.Contains("> quiet", console.OutputText);
        }

        [Test]
        public void Help_ListsEveryRegisteredCommand()
        {
            var console = new DiagnosticsConsole();
            console.Register("give", "give <id> —— 发物品", _ => null);
            console.Register("kill", "kill <n> —— 清怪", _ => null);

            StringAssert.Contains("give", console.HelpText);
            StringAssert.Contains("kill", console.HelpText);
            StringAssert.Contains("help", console.HelpText, "内置命令也要出现在帮助里");

            string result = console.Execute("help");
            StringAssert.Contains("give", result);
        }

        [Test]
        public void Register_DuplicateName_OverwritesAndReportsFalse()
        {
            var console = new DiagnosticsConsole();
            int before = console.CommandCount;
            console.Register("give", "v1", _ => "v1");

            Assert.IsFalse(console.Register("give", "v2", _ => "v2"), "重名是覆盖而不是新增");
            Assert.AreEqual(before + 1, console.CommandCount);
            Assert.AreEqual("v2", console.Execute("give"));
        }

        [Test]
        public void Register_InvalidInput_IsRejected()
        {
            var console = new DiagnosticsConsole();
            int before = console.CommandCount;

            Assert.IsFalse(console.Register(null, "help", _ => "x"));
            Assert.IsFalse(console.Register("  ", "help", _ => "x"));
            Assert.IsFalse(console.Register("ok", "help", null));
            Assert.AreEqual(before, console.CommandCount);
        }

        [Test]
        public void Unregister_RemovesCommand()
        {
            var console = new DiagnosticsConsole();
            console.Register("give", "help", _ => "ok");

            Assert.IsTrue(console.Unregister("give"));
            Assert.IsFalse(console.Unregister("give"), "重复注销幂等");
            StringAssert.Contains("未找到命令", console.Execute("give"));
        }

        [Test]
        public void Output_KeepsOnlyLastLines()
        {
            var console = new DiagnosticsConsole(maxOutputLines: 3);
            for (int i = 1; i <= 5; i++)
                console.Write("行" + i);

            string output = console.OutputText;
            StringAssert.DoesNotContain("行1", output, "超出的旧行要被丢掉（输出区有固定高度）");
            StringAssert.DoesNotContain("行2", output);
            StringAssert.Contains("行3", output);
            StringAssert.Contains("行5", output);
        }

        [Test]
        public void Clear_WipesOutput()
        {
            var console = new DiagnosticsConsole();
            console.Write("一些内容");

            console.Execute("clear");

            Assert.AreEqual(string.Empty, console.OutputText, "clear 连自己那行 \"> clear\" 也一起清掉");
        }
    }

    /// <summary>分节格式：标题独占一行、正文逐行缩进、空内容显示"（无）"（面板里不能出现空白块）。</summary>
    public class DiagnosticsSectionTests
    {
        [Test]
        public void Append_IndentsEveryContentLine()
        {
            var section = new DiagnosticsSection("对象池", () => "共 2 个池\ncube: 活跃 3");

            var sb = new StringBuilder();
            section.Append(sb);

            Assert.AreEqual("对象池\n  共 2 个池\n  cube: 活跃 3\n", sb.ToString().Replace("\r\n", "\n"));
        }

        [Test]
        public void Append_EmptyContent_ShowsPlaceholder()
        {
            var section = new DiagnosticsSection("控制台", () => string.Empty);

            var sb = new StringBuilder();
            section.Append(sb);

            StringAssert.Contains("（无）", sb.ToString());
        }

        [Test]
        public void Ctor_NullTitle_FallsBackToPlaceholder()
        {
            var section = new DiagnosticsSection(null, () => "x");
            Assert.AreEqual("(未命名)", section.Title);
        }
    }
}
