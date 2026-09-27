// 整个 Diagnostics 模块都被这行守卫包住 —— 正式包里本文件编译为空（见 README 阶段 4.11）。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Template.Core.App;
using Template.Core.Logging;
using Template.Core.Services;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Template.Diagnostics
{
    /// <summary>
    /// 调试面板视图（代码构建，零美术资产）。
    ///
    /// <para>
    /// 结构：头部实时摘要（FPS/内存/时间/阶段）→ 可滚动报告区（各服务分节 + 控制台回显）→
    /// 命令输入行 → 操作按钮行（时间缩放 / 暂停 / 日志级别 / 清空 / 报告入 Console）。
    /// </para>
    /// <para>
    /// **自带独立画布（这是刻意的，不是偷懒）**：面板用 <see cref="CanvasScaler.ScaleMode.ConstantPixelSize"/>
    /// 挂在屏幕空间，**不挂在游戏的 UI 画布下**。原因：游戏画布一般用 ScaleWithScreenSize 适配分辨率，
    /// 小窗口下整块 UI 被等比缩小 —— 而 uGUI 的字体是**按画布缩放倍率栅格化**的，
    /// 调试面板的字会跟着变成一团马赛克（实测：1280×720 窗口下 15px 正文被压到 10px，中文基本不可读）。
    /// 独立画布 + 1:1 像素，分辨率再小也是清晰的；顺带它也就不依赖 <c>UIService</c> 与 UILayer，
    /// 项目换 UI 方案（甚至整个删掉 uGUI 面板框架）时调试面板照样能用。
    /// </para>
    /// <para>
    /// 面板**不参与**任何 UI 返回栈：Esc 不会被它抢走（关闭用 F1 或右上角 ×），
    /// 点击只在面板自身矩形内被吃掉，不挡游戏的 UI 与玩法操作。
    /// </para>
    /// <para>
    /// 注意本类与 Demo 面板的差异：<c>Template.Diagnostics</c> 不是"仅编辑器"程序集（开发构建里也要有面板），
    /// 因此**不受**"文件名 = 类名的 MonoBehaviour 不能 AddComponent"那条坑的约束。
    /// </para>
    /// </summary>
    public sealed class DebugPanel : MonoBehaviour
    {
        /// <summary>主开关热键（文本形式，日志与提示文案共用）。</summary>
        public const string ToggleKeyText = "F1";

        /// <summary>备用开关热键。</summary>
        public const string ToggleKeyAltText = "`";

        /// <summary>面板刷新间隔（秒，真实时间）。4 次/秒足够看出趋势，又不会把调试工具本身变成性能问题。</summary>
        private const float RefreshInterval = 0.25f;

        // 尺寸单位 = 屏幕物理像素（ConstantPixelSize）：不用迁就任何分辨率适配，
        // 只要屏幕不是特别小就放得下；字体也就能按"看着舒服"来定，而不是按设计分辨率折算。
        private const float PanelWidth = 960f;
        private const float PanelHeight = 640f;
        private const float Margin = 16f;

        private static readonly Color BackColor = new Color(0.05f, 0.06f, 0.08f, 0.96f);
        private static readonly Color PanelColor = new Color(0.11f, 0.13f, 0.17f);
        private static readonly Color TextColor = new Color(0.88f, 0.92f, 0.96f);
        private static readonly Color DimColor = new Color(0.58f, 0.66f, 0.76f);
        private static readonly Color ButtonColor = new Color(0.19f, 0.24f, 0.31f);

        private static Font _font; // 引擎内置字体（缓存一次；调试面板不引入美术资产）

        private DiagnosticsService _service;
        private GameStateService _gameState;

        private Text _stats;
        private Text _body;
        private Text _logButtonLabel;
        private Text _pauseButtonLabel;
        private InputField _input;
        private ScrollRect _scroll;

        private float _lastRefresh;
        private string _lastOutput;
        private bool _helpMode;      // 报告区切成"用法说明"（见 BuildHelpText）
        private bool _helpShownOnce; // 本次运行是否已经教过一遍用法（教过就不再自动弹）

        /// <summary>面板当前是否显示。</summary>
        public bool IsVisible => gameObject.activeSelf;

        /// <summary>
        /// 构建面板本体（含自带画布与全部子物体），返回时处于**隐藏**状态。
        /// 这里一次性建完并把引用接上即可 —— 面板不走 UIService 的"模板 → Instantiate"那套，
        /// 因此没有"子物体引用不会被克隆过去"的问题（那正是 Demo 面板要把构建放 OnOpened 的原因）。
        /// </summary>
        public static DebugPanel Create(DiagnosticsService service)
        {
            var go = new GameObject(nameof(DebugPanel));
            Object.DontDestroyOnLoad(go); // 面板跨场景常驻（调试工具不该被切场景弄丢）

            // 自带画布：ConstantPixelSize + scaleFactor 1 = 面板尺寸就是物理像素，字永远清晰
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);   // 屏幕右上角
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            rt.anchoredPosition = new Vector2(-20f, -20f);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760; // 盖在游戏 UI 之上（调试面板必须是最后被看到的那个）

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            go.AddComponent<GraphicRaycaster>();

            Image bg = go.AddComponent<Image>();
            bg.color = BackColor;
            bg.raycastTarget = true; // 面板自身吃掉点击，不穿透到游戏

            DebugPanel panel = go.AddComponent<DebugPanel>();
            panel._service = service;
            ServiceLocator.TryGet(out panel._gameState);
            panel.BuildContent();
            panel.Refresh();

            go.SetActive(false); // 建好先收起，等 Open/热键
            return panel;
        }

        /// <summary>显示面板（幂等）。本次运行第一次打开时先给用法说明，之后每次打开都直接看数据。</summary>
        public void Show()
        {
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            if (EventSystem.current == null)
                Log.Warn("Diagnostics", "场景里没有 EventSystem —— 面板能看但按钮/输入框点不动（检查输入模块是否创建了 EventSystem）");

            _helpMode = !_helpShownOnce; // 第一次打开先教用法，读完了点 ? 或 × 就行
            _helpShownOnce = true;
            _lastOutput = null; // 强制下次刷新重新判断要不要滚到底

            Refresh();
        }

        /// <summary>隐藏面板（幂等；只 SetActive(false)，不销毁 —— 下次打开是同一个实例）。</summary>
        public void Hide()
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_service == null)
                return;
            if (Time.unscaledTime - _lastRefresh < RefreshInterval)
                return; // 节流：每帧重建整段文本会把调试工具本身变成性能问题

            Refresh();
        }

        // ── 刷新 ──

        private void Refresh()
        {
            _lastRefresh = Time.unscaledTime;

            _stats.text = _service.BuildSummary();
            _body.text = _helpMode ? BuildHelpText() : _service.BuildReport(); // 报告末节是控制台回显

            _logButtonLabel.text = "日志:" + _service.MinLevel;
            _pauseButtonLabel.text = _gameState != null && _gameState.Current == GamePhase.Paused ? "继续" : "暂停";

            // 控制台有新内容就滚到底（面板打开时的那次也算）—— 否则敲完命令看不到结果，
            // 或得自己往下翻。没新内容时不动滚动位置，免得跟用户的滚动操作打架。
            string output = _service.Console.OutputText;
            if (!string.Equals(output, _lastOutput))
            {
                _lastOutput = output;
                if (!_helpMode)
                    ScrollToBottom(); // 看说明时别跳走
            }
        }

        /// <summary>切换"用法说明"页（报告区内容在报告与说明之间切换）。</summary>
        private void ToggleHelp()
        {
            _helpMode = !_helpMode;
            Refresh();
            if (_helpMode && _scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                _scroll.verticalNormalizedPosition = 1f; // 说明从头看起
            }
        }

        /// <summary>
        /// 面板自带的用法说明 —— 调试工具的第一用户是"第一次打开它的人"，
        /// 看不懂的面板等于不存在（这页就是为此存在的，别删）。
        /// </summary>
        private string BuildHelpText()
        {
            var sb = new System.Text.StringBuilder(2048);
            sb.AppendLine("这个面板是干什么的");
            sb.AppendLine("  卡顿 / 内存一直涨 / 对象没释放时，先看这里 —— 不用改代码，也不用连 Profiler。");
            sb.AppendLine("  它把各模块自己统计的公开数字汇总到一页：谁在涨、谁没释放，一眼能看出来。");
            sb.AppendLine();
            sb.AppendLine("看什么（顶上那行就是最要紧的几个数）");
            sb.AppendLine("  FPS       平滑帧率（最近 " + _service.Sampler.Window + " 帧的平均）—— 掉没掉看它");
            sb.AppendLine("  最差帧    这段窗口里最长的一帧 —— 平均值看不出来的偶发卡顿，看它");
            sb.AppendLine("  内存/GC   GC 次数一直涨 = 每帧都在产生垃圾（掉帧最常见的根源）");
            sb.AppendLine("  下面各节  对象池活跃数、事件监听者数、资源账本、存档槽位……都是实时值；");
            sb.AppendLine("            卡住时盯住「只涨不跌」的那个数字，基本就是它没释放。");
            sb.AppendLine();
            sb.AppendLine("点什么");
            sb.AppendLine("  1.0× / 0.5× / 0.25× / 2.0×   改时间缩放（试子弹时间、慢动作调试）");
            sb.AppendLine("  暂停 / 继续                  走 GameStateService 的暂停（就是游戏里那种暂停）");
            sb.AppendLine("  日志:xxx                     循环切换日志过滤级别（Verbose 全看 / Error 只看报错）");
            sb.AppendLine("  清空 / 报告                  清空命令回显 / 把整份报告打到 Console（方便转给别人看）");
            sb.AppendLine("  ? / ×                        切回报告 / 关闭面板（关闭也可以按 " + ToggleKeyText + "）");
            sb.AppendLine();
            sb.AppendLine("敲什么（点一下输入框就能打字，回车执行）");
            sb.AppendLine("  help                 列出全部命令");
            sb.AppendLine("  dump                 把报告打到 Console");
            sb.AppendLine("  timescale 0.5        设时间缩放；loglevel warn 切日志级别");
            sb.AppendLine("  Demo 里还注册了：demo.shake 0.5 / demo.spawn 5 / demo.save 2 —— 可以试试");
            sb.AppendLine();
            sb.AppendLine("给自己游戏接进来（不用改面板本体）");
            sb.AppendLine("  加一节：diag.RegisterSection(new DiagnosticsSection(\"背包\", () => bag.Describe(), 120));");
            sb.AppendLine("  加命令：diag.Console.Register(\"give\", \"give <id> —— 发物品\", a => { 发(a[0]); return \"OK\"; });");
            sb.AppendLine("  ⚠ 模板只提供机制：玩法命令（无敌 / 加钱 / 跳关）由你自己的游戏层注册才有意义。");
            sb.AppendLine();
            sb.AppendLine("原理一句话");
            sb.AppendLine("  面板不认识任何业务模块：模块把自己的状态注册成「分节」或命令，面板只负责排版与刷新");
            sb.AppendLine("  （所以删掉整个 Diagnostics 目录，其它模块也不会有任何感觉）。");
            return sb.ToString();
        }

        /// <summary>把报告区滚到底部（命令输出在末节，执行完要让用户直接看到结果）。</summary>
        private void ScrollToBottom()
        {
            if (_scroll == null)
                return;
            Canvas.ForceUpdateCanvases();            // 文本刚被替换：先让布局算完，滚动位置才生效
            _scroll.verticalNormalizedPosition = 0f; // 0 = 内容底部（内容 pivot 在顶部）
        }

        // ── 交互 ──

        private void OnCommandSubmitted(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            _service.Console.Execute(line); // 解析/执行/回显都在 Console（纯逻辑，有单测）
            _input.text = string.Empty;
            Refresh(); // Refresh 检测到回显变化会自动滚到底
        }

        // ── 构建 ──

        private void BuildContent()
        {
            MakeText(transform, "调试面板", 26, TextColor, Margin, 8f, 220f, 32f, TextAnchor.MiddleLeft);
            MakeText(transform, ToggleKeyText + " 开关 · ? 看用法 · × 关闭 · 独立画布（原生像素，不参与 Esc 返回）",
                14, DimColor, 150f, 16f, 700f, 22f, TextAnchor.MiddleLeft);

            Button help = MakeButton(transform, "?", 22, PanelWidth - 96f, 8f, 36f, 32f, TextAnchor.MiddleCenter);
            help.onClick.AddListener(ToggleHelp);

            Button close = MakeButton(transform, "×", 26, PanelWidth - 52f, 8f, 36f, 32f, TextAnchor.MiddleCenter);
            close.onClick.AddListener(() => _service.Close());

            _stats = MakeText(transform, string.Empty, 18, TextColor,
                Margin, 46f, PanelWidth - Margin * 2f, 50f, TextAnchor.UpperLeft);

            MakeRect(transform, "Divider", Margin, 100f, PanelWidth - Margin * 2f, 1f)
                .gameObject.AddComponent<Image>().color = new Color(0.25f, 0.30f, 0.38f);

            BuildScrollView(transform, Margin, 106f, PanelWidth - Margin * 2f, 440f);
            BuildCommandInput(transform, Margin, 554f, PanelWidth - Margin * 2f, 34f);
            BuildButtonRow(transform, Margin, 598f);
        }

        /// <summary>可滚动报告区（ScrollRect + RectMask2D 视口 + 自适应高度的内容文本）。</summary>
        private void BuildScrollView(Transform parent, float x, float y, float width, float height)
        {
            var scrollGo = new GameObject("ScrollView");
            scrollGo.transform.SetParent(parent, false);
            TopLeftRect(scrollGo, x, y, width, height);
            Image bg = scrollGo.AddComponent<Image>();
            bg.color = PanelColor; // 同时充当滚动事件的射线目标（子文本都不吃射线）

            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(scrollGo.transform, false);
            RectTransform viewportRt = viewportGo.AddComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(8f, 8f);
            viewportRt.offsetMax = new Vector2(-8f, -8f);
            viewportGo.AddComponent<RectMask2D>(); // 裁剪：超出视口的内容不显示也不吃点击

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            RectTransform contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);  // 顶部对齐，高度由 ContentSizeFitter 撑开
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;

            _body = contentGo.AddComponent<Text>();
            _body.font = BuiltinFont;
            _body.fontSize = 17; // 独立画布下这是真实像素：中文 17px 看着舒服，再小就该费眼了
            _body.color = TextColor;
            _body.alignment = TextAnchor.UpperLeft;
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow; // 必须 Overflow，否则测不出真实高度
            _body.raycastTarget = false;
            _body.supportRichText = false;

            ContentSizeFitter fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll = scrollGo.AddComponent<ScrollRect>();
            _scroll.viewport = viewportRt;
            _scroll.content = contentRt;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 40f;
        }

        /// <summary>命令输入行：InputField（回车提交）+ 执行按钮。**不自动聚焦** —— 免得面板一开就吞掉游戏键盘输入。</summary>
        private void BuildCommandInput(Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject("CommandInput");
            go.transform.SetParent(parent, false);
            TopLeftRect(go, x, y, width - 110f, height);
            Image bg = go.AddComponent<Image>();
            bg.color = PanelColor;

            Text text = MakeText(go.transform, string.Empty, 17, TextColor, 0f, 0f, 0f, 0f, TextAnchor.MiddleLeft);
            StretchInside(text.rectTransform, 10f, 5f);

            Text placeholder = MakeText(go.transform, "输入命令后回车（help 查看全部命令）", 17,
                new Color(0.42f, 0.48f, 0.57f), 0f, 0f, 0f, 0f, TextAnchor.MiddleLeft);
            StretchInside(placeholder.rectTransform, 10f, 5f);

            _input = go.AddComponent<InputField>();
            _input.targetGraphic = bg;
            _input.textComponent = text;
            _input.placeholder = placeholder;
            _input.lineType = InputField.LineType.SingleLine;
            _input.onSubmit.AddListener(OnCommandSubmitted);

            Button run = MakeButton(parent, "执行", 17, x + width - 100f, y, 100f, height, TextAnchor.MiddleCenter);
            run.onClick.AddListener(() => OnCommandSubmitted(_input.text));
        }

        /// <summary>操作按钮行（宽度 100/100/100/100/100/170/100/100 + 7 个 8px 间隔 = 926，内宽 928）。</summary>
        private void BuildButtonRow(Transform parent, float x, float y)
        {
            const float h = 28f;
            float cx = x;

            Button scale1 = MakeButton(parent, "1.0×", 17, cx, y, 100f, h, TextAnchor.MiddleCenter);
            scale1.onClick.AddListener(() => _service.SetTimeScale(1f));
            cx += 108f;

            Button scaleHalf = MakeButton(parent, "0.5×", 17, cx, y, 100f, h, TextAnchor.MiddleCenter);
            scaleHalf.onClick.AddListener(() => _service.SetTimeScale(0.5f));
            cx += 108f;

            Button scaleQuarter = MakeButton(parent, "0.25×", 17, cx, y, 100f, h, TextAnchor.MiddleCenter);
            scaleQuarter.onClick.AddListener(() => _service.SetTimeScale(0.25f));
            cx += 108f;

            Button scale2 = MakeButton(parent, "2.0×", 17, cx, y, 100f, h, TextAnchor.MiddleCenter);
            scale2.onClick.AddListener(() => _service.SetTimeScale(2f));
            cx += 108f;

            Button pause = MakeButton(parent, "暂停", 17, cx, y, 100f, h, TextAnchor.MiddleCenter);
            pause.onClick.AddListener(() => _service.TogglePause());
            _pauseButtonLabel = pause.GetComponentInChildren<Text>(); // 文案随阶段在 Refresh 里更新
            cx += 108f;

            Button log = MakeButton(parent, "日志", 17, cx, y, 170f, h, TextAnchor.MiddleCenter);
            log.onClick.AddListener(() => _service.CycleLogLevel());
            _logButtonLabel = log.GetComponentInChildren<Text>();
            cx += 178f;

            Button clear = MakeButton(parent, "清空", 17, cx, y, 100f, h, TextAnchor.MiddleCenter);
            clear.onClick.AddListener(() =>
            {
                _service.Console.Execute("clear");
                Refresh();
            });
            cx += 108f;

            Button report = MakeButton(parent, "报告", 17, cx, y, 100f, h, TextAnchor.MiddleCenter);
            report.onClick.AddListener(() => _service.Console.Execute("dump"));
        }

        // ── uGUI 搭建小工具（面板专用；正式 UI 请用预制体 + 美术资产）──

        private static Font BuiltinFont
        {
            get
            {
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); // 引擎内置字体，零资产依赖
                return _font;
            }
        }

        private static RectTransform TopLeftRect(GameObject go, float x, float y, float width, float height)
        {
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); // 以面板左上角为原点，向下为正 y
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
            return rt;
        }

        private static RectTransform MakeRect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return TopLeftRect(go, x, y, width, height);
        }

        private static Text MakeText(Transform parent, string content, int fontSize, Color color,
            float x, float y, float width, float height, TextAnchor align)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            TopLeftRect(go, x, y, width, height);

            Text text = go.AddComponent<Text>();
            text.font = BuiltinFont;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;   // 文本不吃射线，事件穿透到面板/滚动区
            text.supportRichText = false; // 调试文本里可能出现 <...>，别被当成富文本标签
            return text;
        }

        private static Button MakeButton(Transform parent, string label, int fontSize, float x, float y,
            float width, float height, TextAnchor align)
        {
            var go = new GameObject("Button-" + label);
            go.transform.SetParent(parent, false);
            TopLeftRect(go, x, y, width, height);

            Image bg = go.AddComponent<Image>();
            bg.color = ButtonColor;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = bg; // 默认 ColorTint：悬停/按下有反馈

            Text text = MakeText(go.transform, label, fontSize, TextColor, 0f, 0f, width, height, align);
            if (align == TextAnchor.MiddleCenter)
                StretchInside(text.rectTransform, 2f, 0f);

            return button;
        }

        private static void StretchInside(RectTransform rt, float padX, float padY)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
        }
    }
}
#endif
