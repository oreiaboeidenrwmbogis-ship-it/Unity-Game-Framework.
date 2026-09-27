using System.Collections.Generic;
using Template.Core.Eventing;
using Template.Core.Logging;
using Template.Core.Services;
using Template.Input;
using UnityEngine;
using UnityEngine.UI;

namespace Template.UI
{
    /// <summary>
    /// UI 服务（核心控制器）：统一管理面板栈的打开 / 关闭 / 返回与遮罩，是界面入口的唯一通道。
    ///
    /// 结构（Init 时自动创建常驻根，DontDestroyOnLoad）：
    ///     [UI Root] (Canvas: Screen Space Overlay, 1920x1080 参考分辨率)
    ///     ├─ ScreenLayer  全屏页栈 —— 同一时刻仅栈顶可见（开新页隐藏旧页，关闭后恢复）
    ///     ├─ PopupLayer   弹窗栈 + 全屏遮罩（首个弹窗打开时亮起，遮住下层全部交互）
    ///     └─ OverlayLayer 悬浮提示 —— 不挡交互、不参与返回
    ///
    /// 打开时由 UIService 实例化预制体并托管（业务禁止自行 Instantiate/Destroy 面板，
    /// 只传 prefab 引用）。每个面板开/关发布强类型事件（UIPanelOpenedEvent/UIPanelClosedEvent），
    /// 音频、输入屏蔽等系统订阅事件各自响应 —— UIService 不认识任何具体业务面板。
    ///
    /// 返回语义（TryBack，输入层的 Esc / 手柄 B 经 Template.Input 的 UI Cancel 动作统一调它）：
    ///     Popup 非空 → 关闭顶层弹窗；否则 Screen 多于一层 → 关闭顶层页面；
    ///     只剩栈底全屏页 → 返回 false，由业务决策（回主菜单 / 退出游戏 / 提示存档）。
    ///
    /// 与输入系统的分工：EventSystem 与 UI 动作映射由 Template.Input 的 InputService 创建管理
    /// （阶段 2.2 起）。本服务只订阅 InputService.CancelPressed 事件转发给 TryBack ——
    /// UI 层既不认识具体按键，也不引用输入包（asmdef 引用不可传递，跨模块通信靠纯 C# 事件解耦）。
    ///     阶段 3 接入 Addressables 后，Open 增加按面板 ID 加载的重载，本类内部实现调整，API 不变。
    /// </summary>
    public sealed class UIService : IGameService
    {
        private readonly List<UiPanel> _screens = new List<UiPanel>();
        private readonly List<UiPanel> _popups = new List<UiPanel>();
        private readonly List<UiPanel> _overlays = new List<UiPanel>();

        private GameObject _root;
        private Image _popupMask; // 全屏遮罩：有弹窗时亮起（挡点击 + 视觉隔离）

        private RectTransform _screenLayer;
        private RectTransform _popupLayer;
        private RectTransform _overlayLayer;

        private InputService _input;     // 取消键联动（阶段 2.2）：UI Cancel（Esc/手柄 B）→ TryBack
        private bool _cancelHooked;

        public UiPanel TopScreen => _screens.Count > 0 ? _screens[_screens.Count - 1] : null;
        public UiPanel TopPopup => _popups.Count > 0 ? _popups[_popups.Count - 1] : null;
        public int ScreenCount => _screens.Count;
        public int PopupCount => _popups.Count;
        public bool IsAnyPopupOpen => _popups.Count > 0;

        public void Init()
        {
            if (_root != null)
                return; // 幂等（防御重复初始化）
            BuildRoot();

            // 取消键联动：InputService 把按键转成纯 C# 事件 CancelPressed（UI 侧不引用输入包），
            // 服务经登记—装配两段式保证先于任何 Init 完成注册 —— 可安全解析。
            _input = ServiceLocator.Get<InputService>();
            if (_input != null)
            {
                _input.CancelPressed += OnCancelPressed;
                _cancelHooked = true;
                Log.Info("UI", "取消键联动就绪：UI Cancel（Esc / 手柄 B）→ TryBack");
            }

            Log.Info("UI", "UI 框架就绪（三层：Screen/Popup/Overlay，常驻根）");
        }

        public void Dispose()
        {
            if (_cancelHooked && _input != null)
            {
                _input.CancelPressed -= OnCancelPressed;
                _cancelHooked = false;
            }
            CloseAll();
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }
        }

        /// <summary>
        /// 打开面板：克隆 prefab 到对应层并压栈。prefab 必须是资源引用（非场景内已托管实例，
        /// 传错会报错——面板生命周期由本服务管理）。面板通过 OnOpened 钩子完成自己的初始化。
        /// </summary>
        public UiPanel Open(UiPanel prefab)
        {
            if (prefab == null)
            {
                Log.Error("UI", "Open: 面板 prefab 为空（请传面板资源引用）");
                return null;
            }
            if (prefab.IsOpen)
            {
                Log.Error("UI", "Open: '{0}' 是已在栈中的实例而非 prefab。面板开合由 UIService 管理，" +
                    "请传 prefab 资源，不要自行实例化", prefab.PanelName);
                return null;
            }

            RectTransform layer = LayerRoot(prefab.Layer);
            if (layer == null)
            {
                Log.Error("UI", "Open: 层级 '{0}' 未初始化（UI 框架未就绪？）", prefab.Layer);
                return null;
            }

            UiPanel panel = Object.Instantiate(prefab, layer, false);
            panel.name = panel.PanelName; // 去掉 (Clone)，日志与 Hierarchy 更可读

            if (prefab.Layer == UILayer.Screen)
            {
                // 全屏页互斥：新页入栈前隐藏旧栈顶（仅隐藏，不入栈的关闭流程）
                if (TopScreen != null)
                {
                    TopScreen.NotifyHidden();
                    Log.Verbose("UI", "Screen '{0}' 被新页遮挡（隐藏，未关闭）", TopScreen.PanelName);
                }
                _screens.Add(panel);
            }
            else if (prefab.Layer == UILayer.Popup)
            {
                if (_popups.Count == 0)
                    SetMaskActive(true); // 首个弹窗：亮起遮罩
                _popups.Add(panel);
            }
            else
            {
                _overlays.Add(panel);
            }

            panel.transform.SetAsLastSibling(); // 后开者渲染在上
            panel.NotifyOpened();

            Log.Info("UI", "面板打开 <{0}>（层:{1}，Screen {2} / Popup {3}）",
                panel.PanelName, panel.Layer, _screens.Count, _popups.Count);
            EventBus<UIPanelOpenedEvent>.Publish(new UIPanelOpenedEvent(panel.PanelName, panel.Layer));
            return panel;
        }

        /// <summary>
        /// 关闭指定面板（任意层，不必是栈顶）。面板出栈后先回调 OnClosed 再销毁；
        /// 若关闭的是原本可见的全屏页，自动恢复被其遮挡的下一层页面。
        /// </summary>
        public bool Close(UiPanel panel)
        {
            if (panel == null)
                return false;

            List<UiPanel> stack = StackOf(panel);
            if (stack == null)
                return false;

            int index = stack.IndexOf(panel);
            if (index < 0)
            {
                Log.Warn("UI", "Close: 面板 '{0}' 不在任何栈中（已关闭或已被外部销毁？）", panel.PanelName);
                return false;
            }

            bool wasTop = index == stack.Count - 1;
            stack.RemoveAt(index);

            if (panel.Layer == UILayer.Popup && _popups.Count == 0)
                SetMaskActive(false); // 最后一个弹窗关闭：释放遮罩

            panel.NotifyClosed();

            // 关闭的是可见全屏页（原本的栈顶）→ 恢复新的栈顶
            if (wasTop && panel.Layer == UILayer.Screen && stack.Count > 0)
            {
                UiPanel below = stack[stack.Count - 1];
                below.NotifyShown();
                Log.Verbose("UI", "Screen '{0}' 关闭，恢复下层 '{1}'", panel.PanelName, below.PanelName);
            }

            Log.Info("UI", "面板关闭 <{0}>（层:{1}，Screen {2} / Popup {3}）",
                panel.PanelName, panel.Layer, _screens.Count, _popups.Count);
            EventBus<UIPanelClosedEvent>.Publish(new UIPanelClosedEvent(panel.PanelName, panel.Layer));
            Object.Destroy(panel.gameObject); // 延迟到帧末销毁：事件监听者此刻仍可读取面板数据
            return true;
        }

        /// <summary>
        /// 统一返回逻辑（Esc / 手柄 B / 返回手势入口）。返回是否成功：
        /// 有弹窗关弹窗，有上层页面关页面；只剩栈底全屏页时不做处理返回 false（业务自决）。
        /// </summary>
        public bool TryBack()
        {
            if (_popups.Count > 0)
            {
                Log.Verbose("UI", "TryBack → 关闭顶层弹窗 <{0}>", TopPopup.PanelName);
                return Close(TopPopup);
            }
            if (_screens.Count > 1)
            {
                Log.Verbose("UI", "TryBack → 关闭顶层页面 <{0}>", TopScreen.PanelName);
                return Close(TopScreen);
            }
            if (_screens.Count == 1)
            {
                Log.Info("UI", "TryBack: 已到栈底全屏页 <{0}>，由业务决策后续行为（回主菜单/退出）", TopScreen.PanelName);
                return false;
            }
            // 无面板可返回也要有痕迹：否则"按了取消键没反应"无法与"链路断"区分
            Log.Verbose("UI", "TryBack: 当前没有任何面板可返回（Screen/Popup 栈均为空）");
            return false;
        }

        /// <summary>关闭全部面板（Overlay → Popup → Screen，各自内部自顶向下）。场景切离/整屏重置时调用。</summary>
        public void CloseAll()
        {
            CloseStack(_popups);
            CloseStack(_screens);
            CloseStack(_overlays);
        }

        /// <summary>
        /// 清空一个层栈。防御"面板已被外部销毁但仍在栈里"的极端情况：
        /// Close 对已销毁（fake-null）面板直接返回 false，若循环只按 Count 判断就会原地空转 ——
        /// 表现为退出 Play 时编辑器卡死且**无任何日志**。故这里无论关闭成败，栈都必须出账。
        /// </summary>
        private void CloseStack(List<UiPanel> stack)
        {
            while (stack.Count > 0)
            {
                int last = stack.Count - 1;
                UiPanel panel = stack[last];
                if (panel != null && Close(panel))
                    continue; // Close 内部已出栈

                stack.RemoveAt(last); // 无法关闭：强制出栈，杜绝空转
                if (panel != null)
                    Log.Warn("UI", "CloseAll: 面板 '{0}' 无法正常关闭，已强制出栈", panel.PanelName);
                else
                    Log.Verbose("UI", "CloseAll: 跳过一个已被外部销毁的面板（拆除顺序不定，属正常）");
            }
        }

        /// <summary>查询指定面板是否正在显示（用于输入屏蔽等决策）。</summary>
        public bool IsPanelOpen(string panelName)
        {
            return FindIn(_screens, panelName) || FindIn(_popups, panelName) || FindIn(_overlays, panelName);
        }

        private void OnCancelPressed()
        {
            TryBack(); // 输入层职责到此为止；面板如何响应由 UI 栈返回语义决定
        }

        // ── 内部工具 ──

        private List<UiPanel> StackOf(UiPanel panel)
        {
            if (panel.Layer == UILayer.Screen) return _screens;
            if (panel.Layer == UILayer.Popup) return _popups;
            return _overlays;
        }

        private static bool FindIn(List<UiPanel> stack, string panelName)
        {
            for (int i = 0; i < stack.Count; i++)
            {
                if (stack[i] != null && stack[i].PanelName == panelName)
                    return true;
            }
            return false;
        }

        private RectTransform LayerRoot(UILayer layer)
        {
            if (layer == UILayer.Screen) return _screenLayer;
            if (layer == UILayer.Popup) return _popupLayer;
            return _overlayLayer;
        }

        private void SetMaskActive(bool on)
        {
            if (_popupMask != null)
                _popupMask.gameObject.SetActive(on);
        }

        private void BuildRoot()
        {
            _root = new GameObject("[UI Root]");
            Object.DontDestroyOnLoad(_root);
            _root.AddComponent<RectTransform>(); // uGUI 根必须是 RectTransform

            Canvas canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _root.AddComponent<GraphicRaycaster>();

            _screenLayer = CreateLayer("ScreenLayer", _root.transform);
            _popupLayer = CreateLayer("PopupLayer", _root.transform);
            _overlayLayer = CreateLayer("OverlayLayer", _root.transform);

            // 弹窗遮罩：PopupLayer 内首个子物体（渲染在弹窗之下、Screen 之上）
            var maskGo = new GameObject("Mask");
            maskGo.transform.SetParent(_popupLayer, false);
            RectTransform maskRt = maskGo.AddComponent<RectTransform>();
            StretchFull(maskRt);
            _popupMask = maskGo.AddComponent<Image>();
            _popupMask.color = new Color(0f, 0f, 0f, 0.45f);
            _popupMask.raycastTarget = true; // 挡住对下层的一切点击
            maskGo.SetActive(false);
        }

        private static RectTransform CreateLayer(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            StretchFull(rt);
            return rt;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
