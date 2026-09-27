namespace Template.Core.Pooling
{
    /// <summary>
    /// 池对象生命周期回调。约定：实现挂在预制体【根节点】的组件上，
    /// Spawn/Despawn 时被所属池各调用一次，用于重置/启动逻辑。
    /// </summary>
    public interface IPoolable
    {
        /// <summary>出池（SetActive(true) 之后）。在此做每次取出都需要的前置（如重置属性、播放音效）。</summary>
        void OnSpawn();

        /// <summary>归池（SetActive(false) 之前）。在此清理状态，保证下次出池是“干净的”。</summary>
        void OnDespawn();
    }
}
