using System.Collections.Generic;
using System.Text;
using Template.Core.Logging;
using Template.Core.Services;
using Template.Core.Timing;
using UnityEngine;

namespace Template.Core.Pooling
{
    /// <summary>
    /// 对象池注册服务（全局）：按 key 管理一组 GameObject 池，并提供全局回收路由 ——
    /// 特效/子弹等池对象自己调 PoolService.Despawn(go)，无需知道它来自哪个池。
    ///
    /// 典型用法：
    ///     PoolService pools = ServiceLocator.Get&lt;PoolService&gt;();
    ///     pools.CreatePool("bullet", bulletPrefab, capacity: 100, prewarm: 20);
    ///     GameObject go = pools.Spawn("bullet", muzzle.position, muzzle.rotation);
    ///     pools.DespawnAfter(go, 2f);   // 延迟回收（走 TimeService，可取消式归池）
    /// </summary>
    public sealed class PoolService : IGameService
    {
        private readonly Dictionary<string, GameObjectPool> _pools = new Dictionary<string, GameObjectPool>();
        private Transform _root;
        private TimeService _time;

        public void Init()
        {
            _time = ServiceLocator.Get<TimeService>();

            var go = new GameObject("[ObjectPool]");
            Object.DontDestroyOnLoad(go);
            _root = go.transform;
        }

        public void Dispose()
        {
            foreach (GameObjectPool pool in _pools.Values)
                pool.Clear();
            _pools.Clear();
            if (_root != null)
                Object.Destroy(_root.gameObject);
        }

        /// <summary>当前池数量（统计）。</summary>
        public int PoolCount => _pools.Count;

        /// <summary>全部池的活跃实例总数（调试面板/性能监控用：卡顿时先看它涨不涨）。</summary>
        public int ActiveTotal
        {
            get
            {
                int total = 0;
                foreach (GameObjectPool pool in _pools.Values)
                    total += pool.ActiveCount;
                return total;
            }
        }

        /// <summary>全部池键（诊断用；这是活动集合，遍历期间勿增删池）。</summary>
        public ICollection<string> Keys => _pools.Keys;

        /// <summary>各池实例数快照（诊断/日志用，逐行：key 总量/空闲/活跃）。</summary>
        public string Dump()
        {
            if (_pools.Count == 0)
                return "（无对象池）";

            var sb = new StringBuilder();
            foreach (KeyValuePair<string, GameObjectPool> pair in _pools)
            {
                GameObjectPool pool = pair.Value;
                sb.Append(pair.Key)
                    .Append(": 总量 ").Append(pool.Created)
                    .Append("，空闲 ").Append(pool.IdleCount)
                    .Append("，活跃 ").Append(pool.ActiveCount)
                    .AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 创建池（幂等：key 已存在则直接返回旧池，仅当 prefab 不一致时告警）。
        /// </summary>
        public GameObjectPool CreatePool(string key, GameObject prefab, int capacity = 64, int prewarm = 0)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Error("Pool", "CreatePool: key 为空");
                return null;
            }
            if (prefab == null)
            {
                Log.Error("Pool", "CreatePool: prefab 为空 (key='{0}')", key);
                return null;
            }

            if (_pools.TryGetValue(key, out GameObjectPool existing))
            {
                if (!ReferenceEquals(existing.Prefab, prefab))
                    Log.Warn("Pool", "CreatePool: key '{0}' 已存在但 prefab 不同，返回旧池（若需重建请先 ClearPool）", key);
                return existing;
            }

            var pool = new GameObjectPool(prefab, _root, capacity, prewarm);
            _pools.Add(key, pool);
            Log.Verbose("Pool", "创建对象池 '{0}'（容量 {1}，预热 {2}）", key, capacity, prewarm);
            return pool;
        }

        public GameObjectPool GetPool(string key)
        {
            if (_pools.TryGetValue(key, out GameObjectPool pool))
                return pool;
            Log.Error("Pool", "GetPool: 未找到 key '{0}'，请先 CreatePool", key);
            return null;
        }

        public bool TryGetPool(string key, out GameObjectPool pool) => _pools.TryGetValue(key, out pool);

        /// <summary>释放某个池的全部空闲实例并移除注册。</summary>
        public void ClearPool(string key)
        {
            if (_pools.TryGetValue(key, out GameObjectPool pool))
            {
                pool.Clear();
                _pools.Remove(key);
                Log.Verbose("Pool", "清除对象池 '{0}'", key);
            }
        }

        /// <summary>出池快捷方式（世界位姿）。池不存在时输出错误并返回 null。</summary>
        public GameObject Spawn(string key, Vector3 position, Quaternion rotation = default, Transform parent = null)
        {
            GameObjectPool pool = GetPool(key);
            return pool != null ? pool.Spawn(position, rotation, parent) : null;
        }

        /// <summary>
        /// 全局回收：路由到实例所属的池（依据 PooledObject 标记）。
        /// 非池化对象会告警 —— 帮助提前发现“忘了走池”的散装实例化。
        /// </summary>
        public bool Despawn(GameObject instance)
        {
            if (instance == null)
                return false;
            if (!instance.TryGetComponent(out PooledObject marker) || marker.Owner == null)
            {
                Log.Warn("Pool", "Despawn: '{0}' 不是池化对象（没有 PooledObject 标记，检查是否漏了 CreatePool/Spawn）", instance.name);
                return false;
            }
            return marker.Owner.Despawn(instance);
        }

        /// <summary>
        /// 延迟回收：seconds 后自动归池（期间对象仍可正常使用）。
        /// 定时器按 clock 推进 —— 用 Scaled 则暂停期间不会回收（效果物留在原地），
        /// 通常特效/子弹回收用默认的 Unscaled 更符合直觉。
        /// </summary>
        public void DespawnAfter(GameObject instance, float seconds, TimerClock clock = TimerClock.Unscaled)
        {
            if (instance == null || seconds < 0f)
                return;
            _time.Delay(seconds, () =>
            {
                // Unity 假空检查：对象已被场景卸载销毁时静默跳过
                if (instance != null)
                    Despawn(instance);
            }, clock);
        }
    }
}
