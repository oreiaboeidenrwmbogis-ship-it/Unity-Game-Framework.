using System;
using System.Collections.Generic;
using Template.Core.Logging;
using UnityEngine;

namespace Template.Core.Pooling
{
    /// <summary>
    /// GameObject 对象池：负责实例化 / 激活 / 回收 / 预热，是子弹、特效、UI 节点的高性能来源。
    ///
    /// 语义：
    /// - 空闲实例休眠在 _root 下（SetActive(false)）；取出后由调用方管理父级与位置；
    /// - 出池挂 PooledObject 标记并回调 IPoolable.OnSpawn()；归池回调 OnDespawn() 后休眠；
    /// - 容量策略：空闲耗尽时临时实例化（首次告警），该实例 Despawn 时直接销毁，总量被容量压住；
    /// - Despawn 幂等：已在池中的实例重复回收会被忽略。
    ///
    /// 注意：池不负责场景切换时的整体回收（那是资源层/场景流转的职责，阶段 3 统一处理）；
    /// 切换场景前请用 <see cref="Clear"/> 释放空闲实例（活跃实例随场景卸载销毁）。
    /// </summary>
    public sealed class GameObjectPool
    {
        private readonly GameObject _prefab;
        private readonly Transform _root;
        private readonly int _capacity;
        private readonly Stack<GameObject> _idle = new Stack<GameObject>();
        private int _created; // 池管控实例总数（临时实例销毁时回落）
        private int _active;  // 当前活跃（借出中）数量
        private bool _overflowWarned;

        public GameObject Prefab => _prefab;
        public int Created => _created;
        public int IdleCount => _idle.Count;
        public int ActiveCount => _active;

        /// <param name="prefab">实例化模板（运行时克隆它，不移动原物）。</param>
        /// <param name="root">空闲容器（一般传 PoolService 创建的常驻根）。</param>
        /// <param name="capacity">容量下限建议值：池空时允许临时实例化，超出的实例回收时销毁。</param>
        /// <param name="prewarm">预热数量（进入战斗前预生成，避免开战瞬间卡顿）。</param>
        public GameObjectPool(GameObject prefab, Transform root, int capacity = 64, int prewarm = 0)
        {
            _prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
            _root = root;
            if (capacity < 1)
                capacity = 64;
            _capacity = capacity;

            int warmCount = Mathf.Clamp(prewarm, 0, capacity);
            for (int i = 0; i < warmCount; i++)
            {
                GameObject go = CreateHiddenInstance();
                _idle.Push(go);
            }
            _created = warmCount;
            if (prewarm > capacity)
                Log.Warn("Pool", "对象池 '{0}' 预热数量 {1} 超过容量 {2}，已截断", prefab.name, prewarm, capacity);
        }

        /// <summary>出池（位置/旋转取模板默认值）。</summary>
        public GameObject Spawn()
            => Spawn(_prefab.transform.position, _prefab.transform.rotation, null);

        /// <summary>出池并设置世界位置/旋转与父级（parent 为 null 时置于场景根）。</summary>
        public GameObject Spawn(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            GameObject go;
            if (_idle.Count > 0)
            {
                go = _idle.Pop();
                Transform t = go.transform;
                t.SetParent(parent, false); // worldPositionStays=false：随后显式设置世界位姿
                t.SetPositionAndRotation(position, rotation);
                go.SetActive(true);
            }
            else
            {
                if (_created >= _capacity)
                {
                    if (!_overflowWarned)
                    {
                        Log.Warn("Pool", "对象池 '{0}' 已满（容量 {1}），开始临时实例化（回收时将销毁）", _prefab.name, _capacity);
                        _overflowWarned = true;
                    }
                }
                go = UnityEngine.Object.Instantiate(_prefab, position, rotation, parent);
                go.SetActive(true); // Instantiate 会继承模板激活状态，此处强制出池即激活
                _created++;
            }
            _active++;

            PooledObject marker = go.GetComponent<PooledObject>();
            if (marker == null)
            {
                marker = go.AddComponent<PooledObject>();
            }
            marker.Owner = this;

            go.GetComponent<IPoolable>()?.OnSpawn();
            return go;
        }

        /// <summary>
        /// 归池。返回是否成功回收。
        /// 已休眠（重复回收）、不属于本池、或已被销毁的实例返回 false。
        /// </summary>
        public bool Despawn(GameObject instance)
        {
            if (instance == null)
                return false;

            PooledObject marker = instance.GetComponent<PooledObject>();
            if (marker == null || marker.Owner != this)
            {
                Log.Warn("Pool", "Despawn: '{0}' 不属于对象池 '{1}'", instance.name, _prefab.name);
                return false;
            }

            // 幂等：父级已是空闲容器且休眠中 → 已在池内
            if (!instance.activeSelf && instance.transform.parent == _root)
                return false;

            instance.GetComponent<IPoolable>()?.OnDespawn();

            if (_created > _capacity)
            {
                // 超容量临时实例 → 直接销毁
                _created--;
                _active--;
                UnityEngine.Object.Destroy(instance);
                return true;
            }

            instance.transform.SetParent(_root, false);
            instance.SetActive(false);
            _idle.Push(instance);
            _active--;
            return true;
        }

        /// <summary>释放全部空闲实例（清空缓存）。活跃实例不受影响。</summary>
        public void Clear()
        {
            int cleared = 0;
            while (_idle.Count > 0)
            {
                GameObject go = _idle.Pop();
                if (go != null)
                    UnityEngine.Object.Destroy(go);
                cleared++;
            }
            _created -= cleared;
        }

        private GameObject CreateHiddenInstance()
        {
            GameObject go = UnityEngine.Object.Instantiate(_prefab, _root, false);
            go.SetActive(false);
            return go;
        }
    }
}
