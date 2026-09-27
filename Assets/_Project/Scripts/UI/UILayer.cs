namespace Template.UI
{
    /// <summary>
    /// 面板层级 —— 决定渲染顺序、遮罩与返回逻辑。
    /// 层级间的关系（自下而上）：Screen → Popup → Overlay。
    /// </summary>
    public enum UILayer
    {
        /// <summary>
        /// 全屏页面（主菜单、设置、结算）。同一时刻仅栈顶可见：
        /// 打开新 Screen 自动隐藏旧栈顶，关闭后自动恢复。
        /// </summary>
        Screen,

        /// <summary>
        /// 模态弹窗（确认框、商店）。打开时自动弹出半透明遮罩阻断对下层的一切点击；
        /// 返回键优先关闭 Popup，而不是下层页面。
        /// </summary>
        Popup,

        /// <summary>
        /// 悬浮提示（Toast、飘字、血条）。常驻显示、不挡交互、不参与返回逻辑。
        /// </summary>
        Overlay,
    }
}
