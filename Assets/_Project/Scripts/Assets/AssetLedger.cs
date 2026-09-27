using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Template.Assets
{
    /// <summary>账本里的一条记录：谁加载了什么、被引用几次、什么时候加载的。</summary>
    public sealed class AssetRecord
    {
        /// <summary>账本键：单资产是资源地址，整组是 "label:组名"，实例是 "instance:<实例ID>"。</summary>
        public string Key;

        /// <summary>所属 Label 组（按组加载时填；单资产为 null）。</summary>
        public string Label;

        /// <summary>加载时用的类型（同一地址被不同类型加载会报错）。</summary>
        public Type AssetType;

        /// <summary>Addressables 的非泛型句柄（释放时用）。</summary>
        public AsyncOperationHandle Handle;

        /// <summary>引用计数：加载几次就要释放几次。</summary>
        public int RefCount;

        /// <summary>加载时刻（Time.realtimeSinceStartup，诊断用）。</summary>
        public float LoadedAtRealtime;

        /// <summary>是否是"按 Label 整组"的记录。</summary>
        public bool IsLabelGroup => !string.IsNullOrEmpty(Label);
    }

    /// <summary>
    /// 资产账本（**纯逻辑，可单测**）：记录加载过的资产与引用次数，供
    /// <see cref="AssetService"/> 做释放决策与泄漏排查。
    ///
    /// 职责边界：真正的加载/卸载由 Addressables 负责，这里只做记账 ——
    /// 所以这一层能脱离 Unity 资源系统单测（引用计数、整组释放、诊断输出）。
    /// </summary>
    public sealed class AssetLedger
    {
        private readonly Dictionary<string, AssetRecord> _records = new Dictionary<string, AssetRecord>(StringComparer.Ordinal);

        /// <summary>已登记的记录数。</summary>
        public int Count => _records.Count;

        /// <summary>全部记录（诊断用）。</summary>
        public ICollection<AssetRecord> Records => _records.Values;

        /// <summary>按 label 前缀找组键。</summary>
        public static string LabelKey(string label) => "label:" + label;

        /// <summary>按实例找键。</summary>
        public static string InstanceKey(int instanceId) => "instance:" + instanceId;

        /// <summary>查找记录；不存在返回 null。</summary>
        public AssetRecord Find(string key)
            => key != null && _records.TryGetValue(key, out AssetRecord record) ? record : null;

        /// <summary>
        /// 登记一条新记录；同键已存在则引用计数 +1（并返回既有记录）。
        /// 语义：**调用方每次加载成功都要配一次 <see cref="Release"/>**。
        /// </summary>
        public AssetRecord AddOrAddRef(string key, string label, Type assetType, AsyncOperationHandle handle, float nowRealtime)
        {
            if (_records.TryGetValue(key, out AssetRecord existing))
            {
                existing.RefCount++;
                return existing;
            }

            var record = new AssetRecord
            {
                Key = key,
                Label = label,
                AssetType = assetType,
                Handle = handle,
                RefCount = 1,
                LoadedAtRealtime = nowRealtime,
            };
            _records[key] = record;
            return record;
        }

        /// <summary>引用计数 +1（命中缓存时用）。</summary>
        public bool AddRef(string key)
        {
            AssetRecord record = Find(key);
            if (record == null)
                return false;
            record.RefCount++;
            return true;
        }

        /// <summary>
        /// 释放一次引用。返回 true 表示**引用计数已归零、记录已移除**，并通过
        /// <paramref name="released"/> 交出该记录 —— 调用方此时才真正交给 Addressables 释放。
        /// 键不存在（或计数未归零）返回 false。
        /// </summary>
        public bool Release(string key, out AssetRecord released)
        {
            released = null;
            AssetRecord record = Find(key);
            if (record == null)
                return false;

            record.RefCount--;
            if (record.RefCount > 0)
                return false;

            _records.Remove(key);
            released = record;
            return true;
        }

        /// <summary>取出某个 Label 组的全部记录并从账本移除（整组释放用）。</summary>
        public List<AssetRecord> TakeLabelGroup(string label)
        {
            var taken = new List<AssetRecord>();
            var keys = new List<string>();
            foreach (KeyValuePair<string, AssetRecord> pair in _records)
            {
                if (string.Equals(pair.Value.Label, label, StringComparison.Ordinal))
                    keys.Add(pair.Key);
            }
            for (int i = 0; i < keys.Count; i++)
            {
                taken.Add(_records[keys[i]]);
                _records.Remove(keys[i]);
            }
            return taken;
        }

        /// <summary>按实例 ID 取记录并移除（释放 Addressables 实例用）。</summary>
        public bool TakeInstance(int instanceId, out AssetRecord record)
        {
            record = Find(InstanceKey(instanceId));
            if (record == null)
                return false;
            _records.Remove(record.Key);
            return true;
        }

        /// <summary>清空账本（服务销毁时用；不负责释放，调用方自己处理）。</summary>
        public List<AssetRecord> Clear()
        {
            var all = new List<AssetRecord>(_records.Values);
            _records.Clear();
            return all;
        }

        /// <summary>诊断输出：当前还挂着的资产（引用计数 &gt; 0 的全部在这里 —— 泄漏排查的第一现场）。</summary>
        public string Dump(float nowRealtime)
        {
            if (_records.Count == 0)
                return "（当前没有已加载资产）";

            var sb = new StringBuilder();
            sb.Append(_records.Count).Append(" 项：");
            foreach (AssetRecord record in _records.Values)
            {
                sb.AppendLine();
                sb.Append("  · ").Append(record.Key)
                  .Append(" [").Append(record.AssetType != null ? record.AssetType.Name : "?")
                  .Append("] ×").Append(record.RefCount)
                  .Append(" 已加载 ").Append((nowRealtime - record.LoadedAtRealtime).ToString("F1")).Append('s');
                if (record.IsLabelGroup)
                    sb.Append("（组 ").Append(record.Label).Append("）");
            }
            return sb.ToString();
        }
    }
}
