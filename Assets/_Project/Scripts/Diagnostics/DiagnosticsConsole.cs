// 整个 Diagnostics 模块都被这行守卫包住 —— 正式包里本文件编译为空（见 README 阶段 4.11）。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Text;

namespace Template.Diagnostics
{
    /// <summary>
    /// 调试控制台（**纯逻辑**，无 Unity 依赖 —— 可 EditMode 单测）：命令注册表 + 输入解析 + 回显缓冲。
    ///
    /// 职责边界（重要）：模板**不预写任何玩法命令**（"无敌 / 加钱 / 跳任务"是具体游戏的东西，
    /// 塞进通用模板就是类型假设）。这里只提供"注册—解析—执行"的机制，命令由游戏层注册：
    /// <code>
    /// diag.Console.Register("give", "give &lt;id&gt; [n] —— 发物品", args =&gt; { ... return "已发放"; });
    /// </code>
    ///
    /// 执行约定：handler 返回的字符串就是显示在面板输出区的文本（返回 null 视为无输出）；
    /// handler 抛异常会被捕获并显示为一行错误，不会让面板或游戏挂掉。
    /// </summary>
    public sealed class DiagnosticsConsole
    {
        private static readonly char[] Separators = { ' ', '\t' };

        private struct Command
        {
            public string Name;
            public string Help;
            public Func<string[], string> Handler;
        }

        private readonly List<Command> _commands = new List<Command>();
        private readonly List<string> _output = new List<string>();
        private readonly int _maxOutputLines;

        /// <param name="maxOutputLines">输出区保留的行数（多出的旧行被丢弃）。</param>
        public DiagnosticsConsole(int maxOutputLines = 8)
        {
            _maxOutputLines = maxOutputLines < 1 ? 1 : maxOutputLines;

            Register("help", "help —— 列出全部命令", _ => HelpText);
            Register("clear", "clear —— 清空输出区", _ =>
            {
                _output.Clear();
                return null;
            });
        }

        /// <summary>已注册命令数（含内置 help / clear）。</summary>
        public int CommandCount => _commands.Count;

        /// <summary>输出区文本（供面板渲染）。</summary>
        public string OutputText => _output.Count == 0 ? string.Empty : string.Join("\n", _output);

        /// <summary>全部命令的帮助文本（一行一条，按注册顺序）。</summary>
        public string HelpText
        {
            get
            {
                var sb = new StringBuilder();
                for (int i = 0; i < _commands.Count; i++)
                    sb.AppendLine(_commands[i].Help);
                return sb.ToString().TrimEnd();
            }
        }

        /// <summary>
        /// 注册命令（名字大小写不敏感）。重名会覆盖旧命令并返回 false ——
        /// 覆盖而非拒绝，是为了支持"游戏重开一局时重新注册同名命令"。
        /// </summary>
        public bool Register(string name, string help, Func<string[], string> handler)
        {
            if (string.IsNullOrWhiteSpace(name) || handler == null)
                return false;

            string trimmed = name.Trim();
            for (int i = 0; i < _commands.Count; i++)
            {
                if (string.Equals(_commands[i].Name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    _commands[i] = new Command { Name = trimmed, Help = help ?? trimmed, Handler = handler };
                    return false;
                }
            }

            _commands.Add(new Command { Name = trimmed, Help = help ?? trimmed, Handler = handler });
            return true;
        }

        /// <summary>注销命令（幂等）。</summary>
        public bool Unregister(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            for (int i = 0; i < _commands.Count; i++)
            {
                if (string.Equals(_commands[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    _commands.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>往输出区写一行（游戏代码也可用它把自定义信息打到面板上）。</summary>
        public void Write(string message)
        {
            _output.Add(message ?? string.Empty);
            while (_output.Count > _maxOutputLines)
                _output.RemoveAt(0);
        }

        /// <summary>
        /// 执行一行输入：解析 → 执行 → 回显，返回本次输出（面板把它追加到输出区）。
        /// 空行直接忽略；未知命令给出提示而不是报错（敲错命令不该刷红字）。
        /// </summary>
        public string Execute(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            string trimmed = line.Trim();
            Write("> " + trimmed);

            string[] tokens = trimmed.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return string.Empty;

            Command command = Find(tokens[0]);
            if (command.Handler == null)
            {
                string miss = "未找到命令 '" + tokens[0] + "'（输入 help 查看全部命令）";
                Write(miss);
                return miss;
            }

            // 参数：第 0 个是命令名本身，其余原样交给 handler（引号等高级解析留给具体游戏按需实现）
            var args = new string[tokens.Length - 1];
            if (args.Length > 0)
                Array.Copy(tokens, 1, args, 0, args.Length);

            string result;
            try
            {
                result = command.Handler(args);
            }
            catch (Exception ex)
            {
                result = "命令 '" + command.Name + "' 执行异常: " + ex.Message;
            }

            if (!string.IsNullOrEmpty(result))
                Write(result);
            return result ?? string.Empty;
        }

        private Command Find(string name)
        {
            for (int i = 0; i < _commands.Count; i++)
            {
                if (string.Equals(_commands[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return _commands[i];
            }
            return default; // Handler == null 即"未找到"
        }
    }
}
#endif
