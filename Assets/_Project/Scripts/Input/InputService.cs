using System;
using System.Collections.Generic;
using Template.Core.App;
using Template.Core.Eventing;
using Template.Core.Logging;
using Template.Core.Services;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Template.Input
{
    /// <summary>
    /// 输入服务（New Input System）：进程级输入枢纽。UI 动作映射由模板内置且常驻；
    /// Gameplay 动作由游戏层 AttachMap 附加（类型无关：模板不预写任何游戏动作）。
    ///
    /// 结构（代码构建，无 .inputactions 资产 —— 零外部依赖，clone 即用）：
    ///     [内置] UI 映射 —— 永远启用：Navigate/Submit/Cancel/Point/Click/ScrollWheel/
    ///     MiddleClick/RightClick，动作名与标准 UI 资产一致（可在编辑器里用同名资产替换）。
    ///     同时把它指派给自动创建的 EventSystem（InputSystemUIInputModule）。
    ///
    /// 暂停屏蔽：订阅 GamePhaseChangedEvent —— 进入 Paused 自动禁用全部已附加的 Gameplay 映射，
    /// 离开 Paused 自动恢复；UI 映射不受影响（暂停菜单仍是 UI）。禁用/启用由本服务统一做，
    /// 游戏层不要自行调用 map.Enable()/Disable()（会与本服务状态脱节）。
    ///
    /// 方案检测：Tick 轮询最近一次产生输入的设备（键鼠/手柄/触摸），变化时发布
    /// InputSchemeChangedEvent —— UI 层据其切换按键提示图标。
    ///
    /// 取消键：UI 模块订阅 CancelPressed 纯 C# 事件（本服务转接），无需引用
    /// UnityEngine.InputSystem 程序集（asmdef 引用不可传递）；CancelAction 保留公开供玩法层直接使用。
    ///
    /// 环境前提：Project Settings → Player → Active Input Handling 必须为 Both 或 Input System
    /// （模板项目首次 clone 后需手动切一次；旧输入模式运行会抛错属预期）。
    /// </summary>
    public sealed class InputService : IGameService, ITickable
    {
        private readonly InputActionAsset _asset;
        private readonly InputActionMap _uiMap;
        private readonly InputAction _cancelAction;

        // 游戏层附加的 Gameplay 映射（暂停屏蔽名单）
        private readonly List<InputActionMap> _attachedMaps = new List<InputActionMap>();

        private GameStateService _gameState;
        private EventSystem _eventSystem;
        private bool _eventSystemCreated; // 仅在自建时置位：Dispose 不得销毁场景自备的 EventSystem
        private InputScheme _scheme = InputScheme.Unknown;

        public InputScheme CurrentScheme => _scheme;

        /// <summary>UI Cancel 动作（Esc / 手柄 B）。玩法层可直接使用。</summary>
        public InputAction CancelAction => _cancelAction;

        /// <summary>
        /// UI 取消键信号（Esc / 手柄 B 按下）—— 本服务把 InputAction 事件转成纯 C# 事件，
        /// 上层模块订阅它无需引用 UnityEngine.InputSystem 程序集（asmdef 引用不可传递）。
        /// </summary>
        public event Action CancelPressed;

        /// <summary>构造期即构建动作资产 —— 与两段式 Init 顺序无关，任何服务在 Init 中读取动作都安全。</summary>
        public InputService()
        {
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _uiMap = new InputActionMap("UI");
            _asset.AddActionMap(_uiMap);

            // 显式声明期望控件类型（1.7.0 参数名 expectedControlLayout），与 .inputactions 资产同构 ——
            // 它不只是给编辑器看的：动作声明的类型与实际绑定到的控件不符会抛 InvalidOperationException
            //（如 Vector2 动作读到 ButtonControl），把 uGUI 输入模块直接读崩。
            // 另外 AddAction 的 type 默认值是 default(InputActionType)=Value，触发器语义动作必须显式传 Button，
            // 否则会悄悄变成"值变化即触发"（引擎对 Button 动作会隐式按 "Button" 限定候选，这里一并写明自解释）。
            InputAction navigate = _uiMap.AddAction("Navigate", InputActionType.Value, expectedControlLayout: "Vector2");
            InputAction point = _uiMap.AddAction("Point", InputActionType.Value, expectedControlLayout: "Vector2");
            InputAction scroll = _uiMap.AddAction("ScrollWheel", InputActionType.Value, expectedControlLayout: "Vector2");
            InputAction submit = _uiMap.AddAction("Submit", InputActionType.Button, expectedControlLayout: "Button");
            _cancelAction = _uiMap.AddAction("Cancel", InputActionType.Button, expectedControlLayout: "Button");
            InputAction click = _uiMap.AddAction("Click", InputActionType.Button, expectedControlLayout: "Button");
            InputAction middleClick = _uiMap.AddAction("MiddleClick", InputActionType.Button, expectedControlLayout: "Button");
            InputAction rightClick = _uiMap.AddAction("RightClick", InputActionType.Button, expectedControlLayout: "Button");

            BuildUIBindings(navigate, point, scroll, submit, click, middleClick, rightClick);
        }

        private void BuildUIBindings(
            InputAction navigate, InputAction point, InputAction scroll,
            InputAction submit, InputAction click, InputAction middleClick, InputAction rightClick)
        {
            // Navigate：WASD / 方向键（2DVector 复合，绑定在动作上）+ 手柄摇杆 / 十字键
            // AddCompositeBinding 是 InputAction 的扩展（1.7.0 签名：action.AddCompositeBinding(composite, interactions, processors)）
            navigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            navigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            navigate.AddBinding("<Gamepad>/leftStick");
            navigate.AddBinding("<Gamepad>/dpad");

            point.AddBinding("<Mouse>/position");
            point.AddBinding("<Touchscreen>/position");

            scroll.AddBinding("<Mouse>/scroll");

            submit.AddBinding("<Keyboard>/enter");
            submit.AddBinding("<Keyboard>/space");
            submit.AddBinding("<Gamepad>/buttonSouth");

            _cancelAction.AddBinding("<Keyboard>/escape");
            _cancelAction.AddBinding("<Gamepad>/buttonEast");

            click.AddBinding("<Mouse>/leftButton");
            click.AddBinding("<Touchscreen>/press");
            middleClick.AddBinding("<Mouse>/middleButton");
            rightClick.AddBinding("<Mouse>/rightButton");
        }

        public void Init()
        {
            _gameState = ServiceLocator.Get<GameStateService>();
            EventBus<GamePhaseChangedEvent>.Subscribe(OnGamePhaseChanged); // 暂停屏蔽
            EnsureEventSystem();  // 阶段 2.1 约定的分工：EventSystem 由输入层创建
            _cancelAction.performed += OnCancelPerformed; // 转接成纯 C# 事件（CancelPressed）

            _asset.Enable();      // UI 映射常驻启用

            Log.Info("Input", "输入系统就绪：内置 UI 映射 {0} 个动作，EventSystem 已托管",
                _uiMap.actions.Count);
        }

        public void Dispose()
        {
            _cancelAction.performed -= OnCancelPerformed;
            EventBus<GamePhaseChangedEvent>.Unsubscribe(OnGamePhaseChanged);
            foreach (InputActionMap map in _attachedMaps)
                map.Disable();
            _attachedMaps.Clear();
            _asset.Disable();
            if (_eventSystemCreated && _eventSystem != null)
            {
                UnityEngine.Object.Destroy(_eventSystem.gameObject);
                _eventSystem = null;
                _eventSystemCreated = false;
            }
        }

        /// <summary>
        /// 附加一条 Gameplay 映射（游戏层在进入玩法时调用，如"角色操控"映射）。
        /// 附加即启用；暂停期间附加则保持禁用、恢复后自动启用。映射由游戏层持有/构建，
        /// 本服务只负责启停与暂停屏蔽 —— 勿在外部自行 Enable/Disable。
        /// </summary>
        public void AttachMap(InputActionMap map)
        {
            if (map == null)
            {
                Log.Error("Input", "AttachMap: 映射为空");
                return;
            }
            if (_attachedMaps.Contains(map))
            {
                Log.Warn("Input", "AttachMap: <{0}> 已附加（重复调用被忽略）", map.name);
                return;
            }

            _attachedMaps.Add(map);
            bool paused = _gameState != null && _gameState.Current == GamePhase.Paused;
            if (paused)
            {
                Log.Info("Input", "Gameplay 映射 <{0}> 已附加（当前暂停 → 保持禁用，恢复后自动启用）", map.name);
            }
            else
            {
                map.Enable();
                Log.Info("Input", "Gameplay 映射 <{0}> 已附加并启用（动作 {1} 个）", map.name, map.actions.Count);
            }
        }

        /// <summary>移除映射（游戏层退出玩法时调用）并停止其输入。返回是否确已移除。</summary>
        public bool DetachMap(InputActionMap map)
        {
            if (map == null || !_attachedMaps.Remove(map))
            {
                Log.Warn("Input", "DetachMap: <{0}> 不在附加名单中", map?.name ?? "(空)");
                return false;
            }
            map.Disable();
            Log.Info("Input", "Gameplay 映射 <{0}> 已移除并禁用", map.name);
            return true;
        }

        public void Tick()
        {
            // 方案检测：只认"本帧新按下"，避免长按反复判定
            InputScheme detected = _scheme;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                detected = InputScheme.Touch;
            else if (Gamepad.current != null && IsAnyGamepadButtonPressed(Gamepad.current))
                detected = InputScheme.Gamepad;
            else if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                detected = InputScheme.KeyboardMouse;
            else if (Mouse.current != null &&
                     (Mouse.current.leftButton.wasPressedThisFrame ||
                      Mouse.current.middleButton.wasPressedThisFrame ||
                      Mouse.current.rightButton.wasPressedThisFrame ||
                      Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f))
                detected = InputScheme.KeyboardMouse;

            if (detected == _scheme)
                return;
            _scheme = detected;
            EventBus<InputSchemeChangedEvent>.Publish(new InputSchemeChangedEvent(_scheme));
        }

        /// <summary>Gamepad 无聚合"本帧任意键按下"属性（1.7.0），逐常用按钮判定。</summary>
        private static bool IsAnyGamepadButtonPressed(Gamepad gamepad)
        {
            return gamepad.buttonSouth.wasPressedThisFrame
                || gamepad.buttonEast.wasPressedThisFrame
                || gamepad.buttonWest.wasPressedThisFrame
                || gamepad.buttonNorth.wasPressedThisFrame
                || gamepad.leftShoulder.wasPressedThisFrame
                || gamepad.rightShoulder.wasPressedThisFrame
                || gamepad.leftStickButton.wasPressedThisFrame
                || gamepad.rightStickButton.wasPressedThisFrame
                || gamepad.dpad.up.wasPressedThisFrame
                || gamepad.dpad.down.wasPressedThisFrame
                || gamepad.dpad.left.wasPressedThisFrame
                || gamepad.dpad.right.wasPressedThisFrame
                || gamepad.startButton.wasPressedThisFrame
                || gamepad.selectButton.wasPressedThisFrame;
        }

        /// <summary>把 InputAction.performed 转接成框架层纯 C# 事件（取消键），供上层模块订阅。</summary>
        private void OnCancelPerformed(InputAction.CallbackContext context)
        {
            CancelPressed?.Invoke();
        }

        /// <summary>暂停屏蔽：Paused 禁用全部 Gameplay 映射；离开 Paused 恢复。UI 映射永不进此名单。</summary>
        private void OnGamePhaseChanged(GamePhaseChangedEvent e)
        {
            if (_attachedMaps.Count == 0)
                return;

            if (e.Current == GamePhase.Paused)
            {
                SetAttachedEnabled(false);
                Log.Info("Input", "暂停：已自动禁用 {0} 个 Gameplay 映射（UI 映射不受影响，菜单仍可操作）",
                    _attachedMaps.Count);
            }
            else if (e.Previous == GamePhase.Paused)
            {
                SetAttachedEnabled(true);
                Log.Info("Input", "恢复：已自动重新启用 {0} 个 Gameplay 映射", _attachedMaps.Count);
            }
        }

        private void SetAttachedEnabled(bool enabled)
        {
            for (int i = 0; i < _attachedMaps.Count; i++)
            {
                if (enabled)
                    _attachedMaps[i].Enable();
                else
                    _attachedMaps[i].Disable();
            }
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                // 场景自备 EventSystem（少见）：尊重它，不重复创建；确认其挂载了 InputSystemUIInputModule
                _eventSystem = EventSystem.current;
                Log.Warn("Input", "场景已存在 EventSystem <{0}>，跳过创建（请确认其配置了 InputSystemUIInputModule）",
                    _eventSystem.name);
                return;
            }

            var go = new GameObject("[EventSystem]");
            UnityEngine.Object.DontDestroyOnLoad(go); // 全名限定：using System 引入了 System.Object
            go.SetActive(false); // 先静默装配，避免 module 在动作未赋值时就 OnEnable

            EventSystem eventSystem = go.AddComponent<EventSystem>();
            InputSystemUIInputModule module = go.AddComponent<InputSystemUIInputModule>();
            module.actionsAsset = _asset;
            module.move = InputActionReference.Create(_uiMap.FindAction("Navigate"));
            module.submit = InputActionReference.Create(_uiMap.FindAction("Submit"));
            module.cancel = InputActionReference.Create(_cancelAction);
            module.point = InputActionReference.Create(_uiMap.FindAction("Point"));
            module.leftClick = InputActionReference.Create(_uiMap.FindAction("Click"));
            module.scrollWheel = InputActionReference.Create(_uiMap.FindAction("ScrollWheel"));
            module.middleClick = InputActionReference.Create(_uiMap.FindAction("MiddleClick"));
            module.rightClick = InputActionReference.Create(_uiMap.FindAction("RightClick"));

            go.SetActive(true);
            _eventSystem = eventSystem;
            _eventSystemCreated = true;
            Log.Info("Input", "EventSystem 已创建（InputSystemUIInputModule ← 模板 UI 映射）");
        }
    }
}
