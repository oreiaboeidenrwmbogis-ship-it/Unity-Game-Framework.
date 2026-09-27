using System;
using System.IO;
using UnityEngine;

namespace Template.Core.Logging
{
    /// <summary>
    /// 全局日志门面 —— 模板内所有日志输出的唯一入口，业务代码禁止直接调用 Debug.Log。
    ///
    /// 设计要点：
    /// 1. 分级 + 标签：Verbose / Info / Warn / Error 四级，每条日志带分类标签（如 "Save"、"Input"），
    ///    编辑器 Console 中标签以统一颜色高亮，方便按模块检索。
    /// 2. 运行时过滤：Log.MinLevel 全局生效，同时作用于引擎与第三方代码的 Debug 输出（见 GameLogHandler）。
    /// 3. 编译期剥离：Verbose / Info 在正式包（非编辑器、非开发构建）中为空实现；
    ///    如需连同调用点的字符串插值开销一起移除，打包时注入 TEMPLATE_STRIP_LOGS 宏（构建管线阶段实现）。
    /// 4. 错误落盘：正式包默认把 Error 写入 persistentDataPath/Logs（真机崩溃排查）；编辑器可手动开启。
    /// 5. **双击日志直接跳到你的调用点**：本文件的实现（连同 GameLogHandler）整段包在
    ///    <c>#line hidden</c> 里 —— 运行时不给这些帧记录源码信息，而 Unity Console 双击时会跳过
    ///    "没有源码信息"的帧，于是落在你的代码上。等价于"把门面编成无 pdb 的 DLL"，但源码仍在工程里。
    ///    （注意 <c>[HideInCallstack]</c> **做不到**这件事：官方说它只影响 Console 详情区的显示。）
    ///    另外每条日志正文前还会附带调用点 —— 日志被复制出 Console 后（贴给别人、写进文件）仍能定位。
    ///
    /// 用法示例：
    ///     Log.Info("Inventory", $"拾取了物品 {itemId}");
    ///     Log.Warn("Save", "存档写入失败，已降级为自动存档");
    ///     Log.Error("Save", exception);
    ///     Log.Verbose("Pool", "ObjectPool 溢出，临时实例化", this);
    /// </summary>
    public static class Log
    {
        // ↓↓↓ 门面实现全部标记为"隐藏行"：运行时不给这些帧记录源码信息，于是
        //     Unity Console 双击一条日志时会**跳过本文件的帧**，直接落到你的调用点。
        //     这是官方论坛里"把日志门面编成无 pdb 的 DLL"的等价做法，但源码仍然留在工程里。
        //     （[HideInCallstack] 做不到这件事：官方说它只管 Console 详情区的显示。）
        //     代价：本文件内断点/单步不生效 —— 没人会去调试日志门面，换来的是每天双击几十次的顺手。
#line hidden
        private static GameLogHandler _handler;

        /// <summary>
        /// 运行时最低输出级别，低于它的日志被丢弃（对引擎 / 第三方 Debug.Log 同样生效）。
        /// 默认 Verbose = 全量输出；日常开发调到 Info 即可屏蔽高频细节。
        /// </summary>
        public static LogLevel MinLevel { get; set; } = LogLevel.Verbose;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            _handler = new GameLogHandler(Debug.unityLogger.logHandler);
            Debug.unityLogger.logHandler = _handler;

#if !UNITY_EDITOR
            // 正式包默认记录 Error 到本地文件，用于真机问题排查
            EnableFileOutput(null, includeAllLevels: false);
#endif
        }

        private static void EnsureHandler()
        {
            if (_handler == null)
            {
                _handler = new GameLogHandler(Debug.unityLogger.logHandler);
                Debug.unityLogger.logHandler = _handler;
            }
        }

        /// <summary>
        /// 打开文件输出（幂等，重复调用会切换目录并新建文件）。
        /// </summary>
        /// <param name="directory">自定义输出目录；null 表示默认目录 persistentDataPath/Logs。</param>
        /// <param name="includeAllLevels">
        /// true = 记录全部级别（调试用，注意 IO 开销）；false = 仅记录 Error（默认行为，开销可忽略）。
        /// </param>
        public static void EnableFileOutput(string directory = null, bool includeAllLevels = true)
        {
            EnsureHandler();
            string dir = directory ?? Path.Combine(Application.persistentDataPath, "Logs");
            _handler.SetFileOutput(dir, includeAllLevels);
        }

        #region Verbose / Info —— 编辑器与开发构建可用；正式包编译为空壳（零日志 IO）
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [HideInCallstack]
        public static void Verbose(string tag, object message, UnityEngine.Object context = null)
            => Write(LogLevel.Verbose, tag, message?.ToString(), null, null, context);

        [HideInCallstack]
        public static void Verbose(string tag, string format, params object[] args)
            => Write(LogLevel.Verbose, tag, null, format, args, null);

        [HideInCallstack]
        public static void Info(string tag, object message, UnityEngine.Object context = null)
            => Write(LogLevel.Info, tag, message?.ToString(), null, null, context);

        [HideInCallstack]
        public static void Info(string tag, string format, params object[] args)
            => Write(LogLevel.Info, tag, null, format, args, null);
#else
        // 空壳：保留签名使调用方代码在正式包可编译。
        // 注意：C# 在调用点先求值参数（如 $"..." 插值会分配字符串）；若需连该开销一并移除，
        // 请在打包时注入 TEMPLATE_STRIP_LOGS 脚本宏（阶段 5.5 构建管线会自动完成）。
        public static void Verbose(string tag, object message, UnityEngine.Object context = null) { }
        public static void Verbose(string tag, string format, params object[] args) { }
        public static void Info(string tag, object message, UnityEngine.Object context = null) { }
        public static void Info(string tag, string format, params object[] args) { }
#endif
        #endregion

        #region Warn / Error —— 所有构建均保留（线上问题排查仍需要它们）

        [HideInCallstack]
        public static void Warn(string tag, object message, UnityEngine.Object context = null)
            => Write(LogLevel.Warning, tag, message?.ToString(), null, null, context);

        [HideInCallstack]
        public static void Warn(string tag, string format, params object[] args)
            => Write(LogLevel.Warning, tag, null, format, args, null);

        [HideInCallstack]
        public static void Error(string tag, object message, UnityEngine.Object context = null)
            => Write(LogLevel.Error, tag, message?.ToString(), null, null, context);

        [HideInCallstack]
        public static void Error(string tag, string format, params object[] args)
            => Write(LogLevel.Error, tag, null, format, args, null);

        [HideInCallstack]
        public static void Error(string tag, Exception exception, UnityEngine.Object context = null)
        {
            if (exception == null)
                return;
            Write(LogLevel.Error, tag, exception.ToString(), null, null, context);
        }

        #endregion

        #region 调用点定位（仅编辑器 / 开发构建）

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 是否在每条日志正文前附带调用点，形如 <c>(DemoRunner.cs:412) </c>。编辑器/开发构建默认开。
        ///
        /// 为什么要自己抓栈：**Unity 的 <c>[HideInCallstack]</c> 救不了双击跳转** ——
        /// 它只把方法从 Console 的"详情区"里去掉（而且还要在 Console 菜单里手动打开
        /// "Strip logging callstack" 才生效），官方原话是 "solely hidden from the visual console window,
        /// not the actual callstacks"；双击仍然落在门面上。把出处直接写进消息正文，
        /// 双击跳哪儿、日志被复制到哪里（贴给别人、写进文件）都不影响。
        ///
        /// 代价：每条**实际输出**的日志抓一次调用栈（被 MinLevel 挡掉的不抓）。
        /// 高频日志多的时候可以 <c>Log.IncludeCallSite = false;</c> 关掉。
        /// </summary>
        public static bool IncludeCallSite { get; set; } = true;

        /// <summary>日志门面自己的命名空间 —— 这些帧一律跳过（含 GameLogHandler 等转发层）。</summary>
        private const string LogNamespace = "Template.Core.Logging";

        /// <summary>取第一条"不属于日志门面"的调用帧，返回 "(文件.cs:行号) "；找不到返回空串。</summary>
        private static string CallSite()
        {
            // 注意：这里不能写 using System.Diagnostics（会与 UnityEngine.Debug 撞名 → CS0104），
            // 所以这几个类型一律用全限定名。
            var trace = new System.Diagnostics.StackTrace(1, true); // 1 = 跳过 CallSite 自己这一帧

            for (int i = 0; i < trace.FrameCount; i++)
            {
                System.Diagnostics.StackFrame frame = trace.GetFrame(i);
                System.Reflection.MethodBase method = frame.GetMethod();
                Type declaring = method != null ? method.DeclaringType : null;
                if (declaring == null || declaring.Namespace == LogNamespace)
                    continue; // 门面自身（含转发层）不是我们要找的调用点

                // 符号解析是惰性的：只有走到这一步，才为"这一帧"付出解析代价
                string file = frame.GetFileName();
                if (string.IsNullOrEmpty(file))
                    continue; // 没有调试信息的帧（引擎内部 / 第三方 DLL）跳过

                return "(" + Path.GetFileName(file) + ":" + frame.GetFileLineNumber() + ") ";
            }
            return string.Empty;
        }
#endif

        #endregion

        /// <summary>
        /// 唯一写入口：级别过滤 → 字符串格式化 → 送 GameLogHandler 统一输出。
        /// 本方法所在区间被 <c>#line hidden</c> 覆盖（双击跳转靠它，见类注释第 5 条），
        /// <c>[HideInCallstack]</c> 只是让 Console 详情区在开启 "Strip logging callstack" 时更干净。
        /// </summary>
        [HideInCallstack]
        private static void Write(LogLevel level, string tag, string message, string format, object[] args,
            UnityEngine.Object context)
        {
            if (level < MinLevel)
                return;

            // 仅在真正输出时格式化，避免无谓的 string.Format 分配
            if (args != null && args.Length > 0)
                message = string.Format(format ?? message, args);
            else if (message == null)
                message = format ?? string.Empty;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (IncludeCallSite)
                message = CallSite() + message;
#endif

            EnsureHandler();

            string text = Colorize(tag, message);
            switch (level)
            {
                case LogLevel.Warning:
                    // 显式 (object) 转型：规避 ILogger 的 (LogType, object, Object) 与 (LogType, string, object) 重载歧义
                    Debug.unityLogger.Log(LogType.Warning, (object)text, context);
                    break;
                case LogLevel.Error:
                    Debug.unityLogger.Log(LogType.Error, (object)text, context);
                    break;
                default:
                    Debug.unityLogger.Log(LogType.Log, (object)text, context);
                    break;
            }
        }

        /// <summary>编辑器 Console 支持富文本：标签统一高亮便于检索；正式包输出纯文本。</summary>
        private static string Colorize(string tag, string message)
        {
#if UNITY_EDITOR
            return string.IsNullOrEmpty(tag)
                ? message
                : string.Format("<color=#7FB3D5>[{0}]</color> {1}", tag, message);
#else
            return string.IsNullOrEmpty(tag) ? message : "[" + tag + "] " + message;
#endif
        }
#line default
        // ↑↑↑ 隐藏行结束。之后再加的方法若也属于门面转发，请一并放进上面的区间里。
    }
}
