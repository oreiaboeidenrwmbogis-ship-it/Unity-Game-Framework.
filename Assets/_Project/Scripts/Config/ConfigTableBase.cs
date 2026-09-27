using System.Collections.Generic;
using Template.Core.Logging;
using UnityEngine;

namespace Template.Config
{
    /// <summary>
    /// 配置表基类：一张表 = 一个 ScriptableObject 资产（内含全部行）+ 运行期 id 索引。
    ///
    /// 具体表类型由配置表管线自动生成（<c>ItemTable : ConfigTableBase&lt;ItemConfig&gt;</c>），
    /// 行数据由导入工具从 CSV 写入 —— 业务代码只读不写。
    ///
    /// 查找语义：
    /// <list type="bullet">
    /// <item><see cref="Get"/> 找不到返回 null（不抛异常：配置缺失是数据问题，不是逻辑问题，
    /// 由调用方决定降级还是报错；导入阶段已用校验把"引用不存在的 id"挡在门外）。</item>
    /// <item><see cref="Require"/> 找不到会记一条 Error 日志并返回 null —— 用于"这里必须有"的场合，
    /// 让配置漏项在 Console 里显形而不是静默变 null。</item>
    /// </list>
    /// </summary>
    public abstract class ConfigTableBase<TRow> : ScriptableObject where TRow : class, IConfigRow
    {
        [SerializeField] private List<TRow> _rows = new List<TRow>();
        [SerializeField] private string _sourceCsv; // 来源 CSV 路径（导入时写入，便于排查）

        private Dictionary<int, TRow> _index;

        /// <summary>全部行（导入顺序）。</summary>
        public IReadOnlyList<TRow> All => _rows;

        /// <summary>行数。</summary>
        public int Count => _rows.Count;

        /// <summary>来源 CSV（相对 Assets 的路径）。</summary>
        public string SourceCsv => _sourceCsv;

        /// <summary>按 id 取行；不存在返回 null。</summary>
        public TRow Get(int id) => TryGet(id, out TRow row) ? row : null;

        /// <summary>按 id 取行；不存在记一条 Error 日志并返回 null（"这里必须有"的场合用它）。</summary>
        public TRow Require(int id)
        {
            if (TryGet(id, out TRow row))
                return row;
            Log.Error("Config", "{0} 缺少 id={1} 的配置行（检查 CSV 是否漏项/未导入）", GetType().Name, id);
            return null;
        }

        /// <summary>按 id 查询（不产生日志）。</summary>
        public bool TryGet(int id, out TRow row)
        {
            EnsureIndex();
            return _index.TryGetValue(id, out row);
        }

        public bool Contains(int id)
        {
            EnsureIndex();
            return _index.ContainsKey(id);
        }

        private void EnsureIndex()
        {
            if (_index != null)
                return;
            _index = new Dictionary<int, TRow>(_rows.Count);
            for (int i = 0; i < _rows.Count; i++)
            {
                TRow row = _rows[i];
                if (row != null)
                    _index[row.Id] = row; // 重复 id 在导入时已被拒绝，这里取后者不会发生
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 导入工具专用：整体替换行数据（并让 id 索引失效重建）。业务代码不要调用。
        /// 参数用非泛型 <see cref="System.Collections.IEnumerable"/>：导入工具手里只有 object 行，
        /// 这样它不必为每张表构造泛型 List（反射拼泛型集合又慢又脆）。
        /// </summary>
        public void EditorReplaceRows(System.Collections.IEnumerable rows, string sourceCsv)
        {
            _rows = new List<TRow>();
            if (rows != null)
            {
                foreach (object row in rows)
                {
                    if (row is TRow typed)
                        _rows.Add(typed);
                }
            }
            _sourceCsv = sourceCsv;
            _index = null;
        }
#endif
    }
}
