using System;
using System.IO;
using UnityEngine;

namespace Template.Core.Logging
{
    /// <summary>
    /// 全局日志处理器 —— 接管所有 Debug 输出（含引擎与第三方插件代码，只要走 UnityEngine.Debug 都会被收编），
    /// 由 <see cref="Log"/> 在程序域重载时自动安装，业务代码无需手动创建。
    ///
    /// 职责：
    /// 1. 运行时级别过滤：低于 <see cref="Log.MinLevel"/> 的输出直接丢弃；
    /// 2. 文件落盘：Error（或全部级别）写入 persistentDataPath/Logs，用于真机排查；
    /// 3. 转交原始 handler（编辑器 Console / 平台日志），保持默认行为不变。
    ///
    /// 红线：本类自身任何 IO 失败都只降级、绝不抛出 —— 日志系统不允许破坏游戏运行。
    /// </summary>
    public sealed class GameLogHandler : ILogHandler
    {
        // ↓↓↓ 与 Log.cs 同理：本类是转发层，整段标记为"隐藏行"后，Console 双击会跳过它落到调用点。
        //     少标这一处就会前功尽弃 —— 它会变成栈上"第一个有源码信息的帧"，双击就停在这行。
        //     顺带好处：引擎与第三方插件直接 Debug.Log 的日志（绕过 Log 门面）也能跳对地方。
#line hidden
        private readonly object _sync = new object();
        private readonly ILogHandler _fallback;

        private StreamWriter _writer;
        private bool _writeAllLevels;

        public GameLogHandler(ILogHandler fallback)
        {
            // Debug.unityLogger.logHandler 在安装前即为非空的默认 handler，作为兜底输出
            _fallback = fallback;
        }

        /// <summary>打开（或切换）文件输出，幂等。</summary>
        public void SetFileOutput(string directory, bool includeAllLevels)
        {
            lock (_sync)
            {
                CloseWriter();
                try
                {
                    Directory.CreateDirectory(directory);
                    string fileName = string.Format("log-{0:yyyyMMdd-HHmmss}.log", DateTime.Now);
                    _writer = new StreamWriter(Path.Combine(directory, fileName), append: true)
                    {
                        // AutoFlush：崩溃 / 强杀前日志已落盘；Error 低频场景开销可忽略
                        AutoFlush = true,
                    };
                    _writeAllLevels = includeAllLevels;
                    _writer.WriteLine("=== session start {0:yyyy-MM-dd HH:mm:ss} ===", DateTime.Now);
                }
                catch (Exception ex)
                {
                    // 文件不可用（无权限 / 磁盘满等）→ 静默降级，仅保留控制台警告
                    _writer = null;
                    if (_fallback != null)
                        _fallback.LogFormat(LogType.Warning, null,
                            "GameLogHandler: 无法打开日志文件 {0}", ex.Message);
                }
            }
        }

        // 本方法是转发层，对读日志的人没有价值：类体已整段 #line hidden（见类首注释），
        // Console 双击会跳过它落到调用点；[HideInCallstack] 则让 Console 详情区在开启
        // "Strip logging callstack" 时更干净。
        [HideInCallstack]
        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            LogLevel level = ToLogLevel(logType);
            if (level < Log.MinLevel)
                return;

            string message = args == null || args.Length == 0
                ? format ?? string.Empty
                : string.Format(format, args);

            WriteToFile(level, message);

            // 以 {0} 单参数转发，避免二次插值格式不一致
            if (_fallback != null)
                _fallback.LogFormat(logType, context, "{0}", message);
        }

        [HideInCallstack]
        public void LogException(Exception exception, UnityEngine.Object context)
        {
            WriteToFile(LogLevel.Error, exception == null ? "<null exception>" : exception.ToString());
            if (_fallback != null)
                _fallback.LogException(exception, context);
        }

        private void WriteToFile(LogLevel level, string message)
        {
            if (_writer == null)
                return;
            if (!_writeAllLevels && level < LogLevel.Error)
                return;

            string line = string.Format("[{0:HH:mm:ss.fff}] [{1}] {2}",
                DateTime.Now, LevelName(level), message);

            lock (_sync)
            {
                try
                {
                    _writer.WriteLine(line);
                }
                catch (Exception ex)
                {
                    // IO 异常（磁盘满等）后放弃文件日志，避免反复失败拖垮帧率
                    CloseWriter();
                    if (_fallback != null)
                        _fallback.LogFormat(LogType.Warning, null,
                            "GameLogHandler: 日志写入失败，文件输出已关闭: {0}", ex.Message);
                }
            }
        }

        private void CloseWriter()
        {
            if (_writer == null)
                return;
            try
            {
                _writer.Dispose();
            }
            catch
            {
                // 忽略关闭阶段的 IO 异常
            }
            _writer = null;
        }

        private static LogLevel ToLogLevel(LogType logType)
        {
            switch (logType)
            {
                case LogType.Warning: return LogLevel.Warning;
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert: return LogLevel.Error;
                default: return LogLevel.Info; // LogType.Log
            }
        }

        private static string LevelName(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Verbose: return "DEBUG";
                case LogLevel.Info: return "INFO";
                case LogLevel.Warning: return "WARN";
                default: return "ERROR";
            }
        }
#line default
    }
}
