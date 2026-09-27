namespace Template.Input
{
    /// <summary>
    /// 输入方案切换事件（由 InputService 在检测到键鼠/手柄/触摸间切换时发布）。
    /// UI 提示图标系统订阅它切换显示："按 Enter 确认" ↔ "按 A 确认"。
    /// </summary>
    public struct InputSchemeChangedEvent
    {
        public InputScheme Scheme;

        public InputSchemeChangedEvent(InputScheme scheme)
        {
            Scheme = scheme;
        }
    }
}
