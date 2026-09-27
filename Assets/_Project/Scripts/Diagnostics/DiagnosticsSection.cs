// 整个 Diagnostics 模块都被这行守卫包住 —— 正式包里本文件编译为空（见 README 阶段 4.11）。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Text;

namespace Template.Diagnostics
{
    /// <summary>
    /// 分节的默认实现：注册一个"取文本"的委托即可，业务模块不必为几行统计专门写一个类。
    ///
    /// 用法（模块自己的 Init 里，一行接入调试面板）：
    /// <code>
    /// if (ServiceLocator.TryGet(out DiagnosticsService diag))
    ///     diag.RegisterSection(new DiagnosticsSection("背包", () => bag.Describe(), order: 120));
    /// </code>
    /// 注意 <c>TryGet</c>：调试模块是**可整体摘除**的，模块不能硬依赖它。
    /// </summary>
    public sealed class DiagnosticsSection : IDiagnosticsSection
    {
        private readonly Func<string> _content;

        /// <param name="title">标题（独占一行）。</param>
        /// <param name="content">正文提供者；返回多行文本时逐行缩进显示；返回 null/空 显示"（无）"。</param>
        /// <param name="order">排序权重，小的在前（业务建议 ≥ 100）。</param>
        public DiagnosticsSection(string title, Func<string> content, int order = 100)
        {
            Title = title ?? "(未命名)";
            _content = content;
            Order = order;
        }

        public string Title { get; }

        public int Order { get; }

        public void Append(StringBuilder sb)
        {
            sb.Append(Title).AppendLine();

            string content = _content?.Invoke();
            if (string.IsNullOrEmpty(content))
            {
                sb.AppendLine("  （无）");
                return;
            }

            // 逐行缩进：面板里"标题—正文"的层级一眼可辨（内容来自各模块的 Dump，行数不定）
            string[] lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                sb.Append("  ").AppendLine(line);
            }
        }
    }
}
#endif
