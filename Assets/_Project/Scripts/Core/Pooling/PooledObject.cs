using UnityEngine;

namespace Template.Core.Pooling
{
    /// <summary>
    /// 池实例标记（由池在 Spawn 时自动挂到实例上），记录所属池，
    /// 供 <see cref="PoolService.Despawn(GameObject)"/> 实现“不知道属于哪个池也能回收”的全局路由。
    /// 业务代码无需手动挂载/操作，只读 Owner 用于自查。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PooledObject : MonoBehaviour
    {
        public GameObjectPool Owner { get; internal set; }
    }
}
