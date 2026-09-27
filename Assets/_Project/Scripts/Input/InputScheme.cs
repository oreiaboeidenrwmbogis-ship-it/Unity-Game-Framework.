namespace Template.Input
{
    /// <summary>
    /// 当前输入方案（最近一次产生输入的动作来自哪种设备）。
    /// UI 层据此切换按键提示图标（"按 A" vs "按 Enter"）—— 见 InputSchemeChangedEvent。
    /// </summary>
    public enum InputScheme
    {
        /// <summary>尚无输入（启动初期）。</summary>
        Unknown,

        /// <summary>键鼠（Keyboard / Mouse）。</summary>
        KeyboardMouse,

        /// <summary>手柄 / 键盘式手柄模拟。</summary>
        Gamepad,

        /// <summary>触摸屏。</summary>
        Touch,
    }
}
