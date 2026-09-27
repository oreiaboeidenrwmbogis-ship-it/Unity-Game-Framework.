using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Template.Assets;
using Template.Core.Logging;
using Template.Core.Pooling;
using Template.Core.Services;
using UnityEngine;

namespace Template.Vfx
{
    /// <summary>一次特效播放的句柄（可用来单独停止）。</summary>
    public readonly struct VfxHandle
    {
        public readonly string Address;
        public readonly GameObject Instance;

        public VfxHandle(string address, GameObject instance)
        {
            Address = address;
            Instance = instance;
        }

        public bool IsValid => Instance != null;
        public static VfxHandle Invalid => default;
    }

    /// <summary>
    /// 特效服务：一行播放、**自动回收**、实例走对象池（复用 <see cref="PoolService"/>，不另造池）。
    ///
    /// 用法（"PlayVFX(key, pos, rot)"）：
    /// <code>
    /// var vfx = ServiceLocator.Get&lt;VfxService&gt;();
    /// vfx.Play("Hit_Spark", transform.position);                     // 一行播放（需要时用 await PlayAsync 拿句柄）
    /// vfx.Play(myRuntimePrefab, pos, Quaternion.identity);           // 也可直接给预制体（程序生成的特效等）
    /// vfx.StopAll();                                                 // 切场景/退出兜底
    /// </code>
    ///
    /// 回收策略（<see cref="Tick"/> 每帧轮询）：
    /// <list type="bullet">
    /// <item>有粒子系统 → 全部不再存活时回收（含"起播当帧不回收"的保护，见 <see cref="VfxRecycleRule"/>）；</item>
    /// <item>没有粒子系统 → 按兜底时长回收，避免永久占着池。</item>
    /// </list>
    ///
    /// 两个入口的分工：**地址**（Addressables，正式项目用，资产按需加载 + 引用计数）与
    /// **预制体**（已经持有的引用/程序生成的特效，演示与调试用）。两者都进同一个池。
    /// </summary>
    public sealed class VfxService : IGameService, ITickable
    {
        private sealed class VfxPool
        {
            public string Key;         // PoolService 里的池键
            public string Address;     // 来源地址（直接给预制体的为 null）
            public GameObject Prefab;
            public bool OwnsAsset;     // 预制体是否由本服务从 AssetService 加载（决定释放时要不要 Release）
        }

        private readonly Dictionary<string, VfxPool> _poolsByKey = new Dictionary<string, VfxPool>(StringComparer.Ordinal);
        private readonly Dictionary<GameObject, float> _active = new Dictionary<GameObject, float>(); // 实例 → 生成时刻

        private PoolService _pools;
        private AssetService _assets;
        private int _poolCounter;

        /// <summary>当前正在播放的实例数。</summary>
        public int ActiveCount => _active.Count;

        public void Init()
        {
            _pools = ServiceLocator.Get<PoolService>();
            Log.Info("Vfx", "特效服务就绪：对象池复用 <{0}>，播完自动回收", nameof(PoolService));
        }

        public void Dispose()
        {
            if (_pools == null)
            {
                // Init 没跑过（服务装配中断）：只清自己的账，别去碰池
                _active.Clear();
                _poolsByKey.Clear();
                return;
            }

            StopAll();
            foreach (VfxPool pool in _poolsByKey.Values)
            {
                if (pool.OwnsAsset && _assets != null && !string.IsNullOrEmpty(pool.Address))
                    _assets.Release(pool.Address);
                _pools.ClearPool(pool.Key);
            }
            _poolsByKey.Clear();
        }

        // ── 播放 ──

        /// <summary>按地址播放（Addressables 加载预制体；首次调用会加载并建池）。返回句柄可单独停止。</summary>
        public async UniTask<VfxHandle> PlayAsync(string address, Vector3 position,
            Quaternion rotation = default, Transform parent = null)
        {
            if (string.IsNullOrEmpty(address))
            {
                Log.Error("Vfx", "PlayAsync：地址为空");
                return VfxHandle.Invalid;
            }

            VfxPool pool = await EnsureAddressPoolAsync(address);
            return pool == null ? VfxHandle.Invalid : SpawnFrom(pool, position, rotation, parent);
        }

        /// <summary>直接用一个预制体播放（已持有的引用 / 程序生成的特效；不经过 Addressables）。</summary>
        public VfxHandle Play(GameObject prefab, Vector3 position,
            Quaternion rotation = default, Transform parent = null)
        {
            if (prefab == null)
            {
                Log.Error("Vfx", "Play：预制体为空");
                return VfxHandle.Invalid;
            }

            VfxPool pool = EnsurePrefabPool(prefab, null);
            return SpawnFrom(pool, position, rotation, parent);
        }

        /// <summary>"一行播放"的地址版（不需要句柄时用它；加载失败已在内部记日志，这里丢弃结果）。</summary>
        public void Play(string address, Vector3 position, Quaternion rotation = default, Transform parent = null)
        {
            if (string.IsNullOrEmpty(address))
            {
                Log.Error("Vfx", "Play：地址为空");
                return;
            }
            PlayAsync(address, position, rotation, parent).Forget();
        }

        /// <summary>停止并回收某个实例。</summary>
        public void Stop(GameObject instance)
        {
            if (instance == null || !_active.Remove(instance))
                return;
            _pools.Despawn(instance);
        }

        /// <summary>停止并回收全部特效（切场景/退出兜底）。</summary>
        public void StopAll()
        {
            if (_active.Count == 0 || _pools == null)
            {
                _active.Clear();
                return;
            }
            var instances = new List<GameObject>(_active.Keys);
            _active.Clear();
            for (int i = 0; i < instances.Count; i++)
            {
                if (instances[i] != null)
                    _pools.Despawn(instances[i]);
            }
            Log.Info("Vfx", "已回收全部特效：{0} 个", instances.Count);
        }

        /// <summary>预热：预先加载并生成若干实例放进池（进战斗/进关卡前调用，避免首次播放卡一下）。</summary>
        public async UniTask<int> PrewarmAsync(string address, int count)
        {
            VfxPool pool = await EnsureAddressPoolAsync(address);
            if (pool == null || count <= 0)
                return 0;

            int made = 0;
            for (int i = 0; i < count; i++)
            {
                GameObject instance = _pools.Spawn(pool.Key, Vector3.zero, Quaternion.identity, null);
                _pools.Despawn(instance); // 立刻还回池里待用
                made++;
            }
            Log.Info("Vfx", "预热 <{0}>：{1} 个实例已就绪", address, made);
            return made;
        }

        // ── 回收 ──

        public void Tick()
        {
            if (_active.Count == 0)
                return;

            float now = Time.realtimeSinceStartup;
            List<GameObject> recycle = null;
            List<GameObject> vanished = null; // 被外部销毁的实例：只需从活跃表移除（没法再还池）

            foreach (KeyValuePair<GameObject, float> pair in _active)
            {
                GameObject instance = pair.Key;
                if (instance == null)
                {
                    (vanished ??= new List<GameObject>()).Add(instance);
                    continue;
                }

                ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(false);
                bool anyAlive = false;
                for (int i = 0; i < systems.Length; i++)
                {
                    if (systems[i].IsAlive(true))
                    {
                        anyAlive = true;
                        break;
                    }
                }

                if (VfxRecycleRule.ShouldRecycle(now - pair.Value, systems.Length > 0, anyAlive))
                    (recycle ??= new List<GameObject>()).Add(instance);
            }

            if (vanished != null)
            {
                for (int i = 0; i < vanished.Count; i++)
                    _active.Remove(vanished[i]); // 注意：不能用 Stop（它按实例查池，fake-null 查不到）
            }
            if (recycle == null)
                return;
            for (int i = 0; i < recycle.Count; i++)
                Stop(recycle[i]);
        }

        /// <summary>诊断：当前池与活跃实例概览（泄漏排查用）。</summary>
        public string Dump()
        {
            if (_poolsByKey.Count == 0)
                return "（还没有任何特效池）";

            var sb = new System.Text.StringBuilder();
            sb.Append(_poolsByKey.Count).Append(" 个特效池，活跃 ").Append(_active.Count).Append(" 个实例：");
            foreach (VfxPool pool in _poolsByKey.Values)
            {
                sb.AppendLine();
                string label = pool.Address ?? (pool.Prefab != null ? pool.Prefab.name : "(预制体已销毁)");
                sb.Append("  · ").Append(label).Append(" → 池 <").Append(pool.Key).Append('>');
            }
            return sb.ToString();
        }

        // ── 内部 ──

        private VfxHandle SpawnFrom(VfxPool pool, Vector3 position, Quaternion rotation, Transform parent)
        {
            GameObject instance = _pools.Spawn(pool.Key, position, rotation, parent);
            if (instance == null)
            {
                Log.Error("Vfx", "生成实例失败（池 <{0}>）", pool.Key);
                return VfxHandle.Invalid;
            }
            // 池化复用：显式重播一次 —— 不能依赖 playOnAwake 在"重新激活"时一定重启粒子
            // （回收时我们只 Deactivate，粒子状态可能还停在上次结束的位置）
            ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(false);
            for (int i = 0; i < systems.Length; i++)
                systems[i].Play(true);

            _active[instance] = Time.realtimeSinceStartup;
            return new VfxHandle(pool.Address, instance);
        }

        private async UniTask<VfxPool> EnsureAddressPoolAsync(string address)
        {
            if (_poolsByKey.TryGetValue("addr:" + address, out VfxPool cached))
                return cached;

            if (_assets == null)
                _assets = ServiceLocator.TryGet<AssetService>(out AssetService assets) ? assets : null;
            if (_assets == null)
            {
                Log.Error("Vfx", "AssetService 不可用，无法按地址加载特效 <{0}>（可改用 Play(prefab) 重载）", address);
                return null;
            }

            var prefab = await _assets.LoadAsync<GameObject>(address);
            if (prefab == null)
                return null;

            return EnsurePrefabPool(prefab, address);
        }

        private VfxPool EnsurePrefabPool(GameObject prefab, string address)
        {
            string key = address != null ? "addr:" + address : "prefab:" + prefab.GetInstanceID();
            if (_poolsByKey.TryGetValue(key, out VfxPool existing))
                return existing;

            var pool = new VfxPool
            {
                Key = "vfx_" + _poolCounter++,
                Address = address,
                Prefab = prefab,
                OwnsAsset = address != null,
            };
            _pools.CreatePool(pool.Key, prefab);
            _poolsByKey[key] = pool;
            Log.Verbose("Vfx", "已建特效池 <{0}>（{1}）", key, pool.Key);
            return pool;
        }
    }
}
