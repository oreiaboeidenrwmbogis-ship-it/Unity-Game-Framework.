using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Template.Core.Logging;
using Template.Core.Services;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Template.Assets
{
    /// <summary>
    /// 资源服务：Addressables 的统一入口。业务只跟它打交道，好处是
    /// **引用计数与"按 Label 整组释放"集中在一处**（切关卡整组卸载靠这个），
    /// 并且能随时把"现在还挂着哪些资产"打出来排查泄漏（<see cref="Dump"/>）。
    ///
    /// 用法：
    /// <code>
    /// var assets = ServiceLocator.Get&lt;AssetService&gt;();
    /// var prefab = await assets.LoadAsync&lt;GameObject&gt;("Enemy_Goblin");      // 引用计数 +1
    /// assets.Release("Enemy_Goblin");                                      // 引用计数 -1，归零才真释放
    /// var uiAssets = await assets.LoadLabelAsync&lt;Sprite&gt;("ui_mainmenu");     // 整组
    /// assets.ReleaseLabel("ui_mainmenu");                                  // 整组卸载
    /// </code>
    ///
    /// 语义约定（很重要，避免"用着用着被释放"）：
    /// <list type="bullet">
    /// <item>同地址重复加载 → 返回同一份资产并把引用计数 +1（**每次加载都要配一次 Release**）；</item>
    /// <item>同一地址用不同类型加载 → 报错返回 null（不做隐式转换，避免静默类型错乱）；</item>
    /// <item>只登记**加载完成**的句柄，所以命中缓存时无需再 await（返回值可直接用）。</item>
    /// </list>
    ///
    /// 铁律 4 落地：异步统一走 UniTask（`await assets.LoadAsync&lt;T&gt;(addr)`）；
    /// 禁用 Resources.Load（本服务是唯一入口）。
    /// </summary>
    public sealed class AssetService : IGameService
    {
        private readonly AssetLedger _ledger = new AssetLedger();

        /// <summary>当前挂着（引用计数 &gt; 0）的资产记录。</summary>
        public ICollection<AssetRecord> Loaded => _ledger.Records;

        /// <summary>当前挂着的资产数。</summary>
        public int LoadedCount => _ledger.Count;

        public void Init()
        {
            Log.Info("Assets", "资源服务就绪：Addressables 统一入口（引用计数 + Label 整组释放）");
        }

        public void Dispose()
        {
            // 退出/换服务时兜底释放，避免"看着没了其实还挂着"的伪泄漏日志
            try
            {
                ReleaseAll();
            }
            catch (Exception ex)
            {
                Log.Error("Assets", "Dispose 释放资产失败：{0}", ex.Message);
            }
        }

        // ── 单资产 ──

        /// <summary>按地址异步加载资产；失败记日志并返回 null（不抛异常，业务按"加载不到"处理）。</summary>
        public async UniTask<T> LoadAsync<T>(string address) where T : class
        {
            if (string.IsNullOrEmpty(address))
            {
                Log.Error("Assets", "LoadAsync：地址为空");
                return null;
            }

            AssetRecord cached = _ledger.Find(address);
            if (cached != null)
            {
                if (cached.AssetType != typeof(T))
                {
                    Log.Error("Assets", "同一地址被两种类型加载：'{0}' 已按 {1} 加载，这次按 {2}",
                        address, cached.AssetType.Name, typeof(T).Name);
                    return null;
                }
                _ledger.AddRef(address);
                return cached.Handle.Result as T; // 只登记已完成的句柄，这里必然已就绪
            }

            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(address);
            if (!await AwaitHandle(handle, address))
                return null;

            _ledger.AddOrAddRef(address, null, typeof(T), handle, Time.realtimeSinceStartup);
            Log.Verbose("Assets", "已加载 <{0}>（{1}）", address, typeof(T).Name);
            return handle.Result;
        }

        /// <summary>释放一次加载。引用计数归零时真正交给 Addressables 卸载。</summary>
        public bool Release(string address)
        {
            if (_ledger.Release(address, out AssetRecord released))
            {
                Addressables.Release(released.Handle);
                Log.Verbose("Assets", "已卸载 <{0}>", address);
                return true;
            }

            if (_ledger.Find(address) == null)
                Log.Warn("Assets", "Release：'{0}' 不在账本里（没加载过、或已被释放）", address);
            return false;
        }

        // ── 整组（Label） ──

        /// <summary>按 Label 整组加载（关卡/ui/audio 这类分组）；失败返回空列表并记日志。</summary>
        public async UniTask<IList<T>> LoadLabelAsync<T>(string label) where T : class
        {
            if (string.IsNullOrEmpty(label))
            {
                Log.Error("Assets", "LoadLabelAsync：label 为空");
                return Array.Empty<T>();
            }

            string key = AssetLedger.LabelKey(label);
            AssetRecord cached = _ledger.Find(key);
            if (cached != null)
            {
                _ledger.AddRef(key);
                return cached.Handle.Result as IList<T> ?? (IList<T>)Array.Empty<T>();
            }

            AsyncOperationHandle<IList<T>> handle = Addressables.LoadAssetsAsync<T>(label, null);
            if (!await AwaitHandle(handle, label))
                return Array.Empty<T>();

            _ledger.AddOrAddRef(key, label, typeof(T), handle, Time.realtimeSinceStartup);
            Log.Info("Assets", "已按组加载 <{0}>：{1} 个 {2}", label, handle.Result.Count, typeof(T).Name);
            return handle.Result;
        }

        /// <summary>整组释放（切关卡时"整组卸载"就靠它）；返回释放的项数。</summary>
        public int ReleaseLabel(string label)
        {
            List<AssetRecord> records = _ledger.TakeLabelGroup(label);
            for (int i = 0; i < records.Count; i++)
                Addressables.Release(records[i].Handle);

            if (records.Count == 0)
                Log.Warn("Assets", "ReleaseLabel：'{0}' 不在账本里（没按组加载过、或已释放）", label);
            else
                Log.Info("Assets", "已整组卸载 <{0}>：{1} 项", label, records.Count);
            return records.Count;
        }

        // ── 实例化（预制体） ──

        /// <summary>按地址异步实例化预制体；记得用 <see cref="ReleaseInstance"/> 回收（或交给对象池托管）。</summary>
        public async UniTask<GameObject> InstantiateAsync(string address, Transform parent = null)
        {
            if (string.IsNullOrEmpty(address))
            {
                Log.Error("Assets", "InstantiateAsync：地址为空");
                return null;
            }

            AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(address, parent);
            if (!await AwaitHandle(handle, address))
                return null;

            GameObject instance = handle.Result;
            _ledger.AddOrAddRef(AssetLedger.InstanceKey(instance.GetInstanceID()), null,
                typeof(GameObject), handle, Time.realtimeSinceStartup);
            return instance;
        }

        /// <summary>回收一个由 <see cref="InstantiateAsync"/> 创建的实例。</summary>
        public bool ReleaseInstance(GameObject instance)
        {
            if (instance == null)
                return false;

            if (!_ledger.TakeInstance(instance.GetInstanceID(), out _))
            {
                Log.Warn("Assets", "ReleaseInstance：这个实例不是通过 AssetService 创建的 <{0}>", instance.name);
                return false;
            }
            Addressables.ReleaseInstance(instance);
            return true;
        }

        // ── 诊断 / 兜底 ──

        /// <summary>列出当前挂着的资产（泄漏排查：切了关卡之后这里应该干净）。</summary>
        public string Dump() => _ledger.Dump(Time.realtimeSinceStartup);

        /// <summary>释放全部已加载资产（切场景/退出兜底）；返回释放项数。</summary>
        public int ReleaseAll()
        {
            List<AssetRecord> records = _ledger.Clear();
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Handle.IsValid())
                    Addressables.Release(records[i].Handle);
            }
            if (records.Count > 0)
                Log.Info("Assets", "已释放全部已加载资产：{0} 项", records.Count);
            return records.Count;
        }

        /// <summary>等待一个 Addressables 句柄；失败/异常都记日志并返回 false（调用方负责收尾）。</summary>
        private static async UniTask<bool> AwaitHandle<T>(AsyncOperationHandle<T> handle, string key)
        {
            try
            {
                await handle.ToUniTask();
            }
            catch (Exception ex)
            {
                Log.Error("Assets", "加载异常 <{0}>：{1}", key, ex.Message);
                if (handle.IsValid())
                    Addressables.Release(handle);
                return false;
            }

            if (handle.Status == AsyncOperationStatus.Succeeded)
                return true;

            Log.Error("Assets", "加载失败 <{0}>：{1}", key,
                handle.OperationException != null ? handle.OperationException.Message : "未知原因");
            if (handle.IsValid())
                Addressables.Release(handle);
            return false;
        }
    }
}
