using UnityEngine;

namespace Template.UI
{
    /// <summary>
    /// 面板基类（视图层约定）。业务面板继承它，声明自己的层级并实现生命周期钩子；
    /// 不要自己管理开合 —— 统一交给 <see cref="UIService"/>（Open/Close/返回都由它驱动）。
    ///
    /// 面板资源约定：面板做成预制体（Prefab 根节点挂 UiPanel 子类组件），
    /// 调用 UIService.Open(prefab) 打开 —— 阶段 3 接入 Addressables 后改为按 ID 加载，业务代码不变。
    /// </summary>
    public abstract class UiPanel : MonoBehaviour
    {
        /// <summary>面板所在层级（默认全屏页）。Popup/Overlay 面板覆写此项。</summary>
        public virtual UILayer Layer => UILayer.Screen;

        /// <summary>面板唯一标识：默认取类型名，事件与日志用它区分面板。重名会相互干扰，勿覆写为重复值。</summary>
        public virtual string PanelName => GetType().Name;

        /// <summary>是否处于打开状态（入栈 = true；出栈/销毁 = false）。</summary>
        public bool IsOpen { get; private set; }

        /// <summary>当前是否可见（Screen 被上层页面隐藏时为 false，但 IsOpen 仍为 true）。</summary>
        public bool IsVisible => gameObject.activeSelf;

        // ── 生命周期钩子（UIService 驱动，子类按需覆写）──

        /// <summary>打开完成：已入栈且可见。在此初始化面板内容、订阅业务事件。</summary>
        protected virtual void OnOpened() { }

        /// <summary>关闭中：已出栈，事件尚未发布，GameObject 即将销毁。在此释放订阅、回写结果。</summary>
        protected virtual void OnClosed() { }

        /// <summary>被上层 Screen 遮挡而隐藏（非关闭）。</summary>
        protected virtual void OnHidden() { }

        /// <summary>重新回到栈顶恢复可见。</summary>
        protected virtual void OnShown() { }

        // ── 以下由 UIService 调用（internal），业务代码不直接触发 ──

        internal void NotifyOpened()
        {
            IsOpen = true;
            gameObject.SetActive(true);
            OnOpened();
        }

        internal void NotifyClosed()
        {
            IsOpen = false;
            OnClosed();
        }

        internal void NotifyShown()
        {
            gameObject.SetActive(true);
            OnShown();
        }

        internal void NotifyHidden()
        {
            gameObject.SetActive(false);
            OnHidden();
        }
    }
}
