namespace Template.UI
{
    /// <summary>
    /// 面板打开事件（由 UIService 在面板实例化并完成 OnOpened 回调后发布）。
    /// 音频系统可订阅它播放开屏音；输入系统可据此屏蔽下层输入（阶段 2.2 落地）。
    /// </summary>
    public struct UIPanelOpenedEvent
    {
        /// <summary>面板类型名（UiPanel.PanelName）。</summary>
        public string PanelName;

        /// <summary>所在层级。</summary>
        public UILayer Layer;

        public UIPanelOpenedEvent(string panelName, UILayer layer)
        {
            PanelName = panelName;
            Layer = layer;
        }
    }

    /// <summary>
    /// 面板关闭事件（UIService 在移除面板时发布；事件先于 GameObject 销毁，
    /// 监听者可读取面板残余数据，如确认框的最终选择）。
    /// </summary>
    public struct UIPanelClosedEvent
    {
        public string PanelName;
        public UILayer Layer;

        public UIPanelClosedEvent(string panelName, UILayer layer)
        {
            PanelName = panelName;
            Layer = layer;
        }
    }
}
