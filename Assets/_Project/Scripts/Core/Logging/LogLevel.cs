namespace Template.Core.Logging
{
    /// <summary>
    /// 日志级别。数值越大越重要；级别过滤语义为“低于 MinLevel 的日志被丢弃”。
    /// </summary>
    public enum LogLevel
    {
        /// <summary>开发调试期的高频细节输出（每帧 / 每次事件），正式包应剥离。</summary>
        Verbose = 0,

        /// <summary>常规信息，记录关键流程节点（场景切换、存档完成等）。</summary>
        Info = 1,

        /// <summary>警告：行为异常但可恢复，游戏能继续运行。</summary>
        Warning = 2,

        /// <summary>错误：功能失效或数据损坏，需要关注与排查。</summary>
        Error = 3,
    }
}
