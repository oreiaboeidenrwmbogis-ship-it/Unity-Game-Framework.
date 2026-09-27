// 整个 Diagnostics 模块都被这行守卫包住 —— 正式包里本文件编译为空（见 README 阶段 4.11）。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;

namespace Template.Diagnostics
{
    /// <summary>
    /// 调试面板的一个"分节"：面板主体就是若干分节自上而下的拼装结果。
    ///
    /// 为什么做成接口：各模块的运行期状态（存档槽位 / 资源账本 / 特效池…）只有模块自己最清楚，
    /// 面板不该反过来认识每个模块的内部结构。于是模块按需注册一个分节即可出现在面板里 ——
    /// 这与存档的 <c>ISaveSection</c>、输入的 <c>AttachMap</c> 是同一套"模块提供、框架收口"的思路。
    ///
    /// 约定：
    /// <list type="bullet">
    /// <item>实现必须是**只读**的：面板以约 4 次/秒的频率刷新，分节里不要做写操作或有副作用的计算；</item>
    /// <item>实现不应抛异常，但框架仍会逐节隔离（一个分节出错不影响其它分节与面板本体）；</item>
    /// <item>每帧调用会有分配开销，若内容昂贵请自己在实现里做节流缓存。</item>
    /// </list>
    /// </summary>
    public interface IDiagnosticsSection
    {
        /// <summary>分节标题（面板里独占一行）。</summary>
        string Title { get; }

        /// <summary>排序权重：小的排前面（框架基础信息用 0~30，业务模块用 100 起）。</summary>
        int Order { get; }

        /// <summary>把本节的正文追加到构建器（多行请用 AppendLine）。</summary>
        void Append(StringBuilder sb);
    }
}
#endif
