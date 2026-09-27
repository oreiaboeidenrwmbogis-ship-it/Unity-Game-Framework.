using System;
using System.Collections.Generic;
using Template.Core.Logging;
using UnityEngine;

namespace Template.Config
{
    /// <summary>
    /// 配置库：持有本项目全部配置表的引用（由导入工具自动维护，不要手工增删）。
    ///
    /// 为什么不放在 Resources / 不写死路径：模板铁律**禁用 Resources.Load**，
    /// 运行期拿到资产只能靠"显式引用"——所以由 <see cref="ConfigInstaller"/>（Boot 场景）
    /// 或将来的 Addressables 加载结果把本资产交给 <see cref="ConfigService"/>。
    /// </summary>
    public sealed class ConfigDatabase : ScriptableObject
    {
        [SerializeField] private List<ScriptableObject> _tables = new List<ScriptableObject>();

        /// <summary>全部表（顺序与导入一致）。</summary>
        public IReadOnlyList<ScriptableObject> Tables => _tables;

        /// <summary>按表类型取表；不存在返回 null。</summary>
        public TTable GetTable<TTable>() where TTable : ScriptableObject
        {
            for (int i = 0; i < _tables.Count; i++)
            {
                if (_tables[i] is TTable table)
                    return table;
            }
            return null;
        }

        /// <summary>按表类型取表；不存在记一条 Error 日志（排查"忘导入/表名改了"用）。</summary>
        public TTable RequireTable<TTable>() where TTable : ScriptableObject
        {
            TTable table = GetTable<TTable>();
            if (table == null)
                Log.Error("Config", "配置库里没有表 <{0}>（重新运行 Tools/配置表/全部导入）", typeof(TTable).Name);
            return table;
        }

        /// <summary>该行类型所属的表（导入工具解析外键时用）。</summary>
        public ScriptableObject GetTableForRowType(Type rowType)
        {
            for (int i = 0; i < _tables.Count; i++)
            {
                ScriptableObject table = _tables[i];
                if (table == null)
                    continue;
                for (Type t = table.GetType(); t != null; t = t.BaseType)
                {
                    if (t.IsGenericType && t.GetGenericArguments()[0] == rowType)
                        return table;
                }
            }
            return null;
        }

#if UNITY_EDITOR
        /// <summary>导入工具专用：整体替换表清单。</summary>
        public void EditorReplaceTables(List<ScriptableObject> tables) => _tables = tables ?? new List<ScriptableObject>();
#endif
    }
}
