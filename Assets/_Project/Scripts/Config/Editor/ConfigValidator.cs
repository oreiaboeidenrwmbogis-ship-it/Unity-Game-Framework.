using System;
using System.Collections.Generic;

namespace Template.Config.EditorTools
{
    /// <summary>
    /// 导入前校验（纯逻辑，可单测）。规则不通过时导入**整体终止**、不产出任何资产 ——
    /// 带着错误数据进运行期，比编译报错难查得多：
    /// <list type="number">
    /// <item>必须有 <c>id</c> 列且为 int（主键）</item>
    /// <item>每个列的类型必须可识别（基础类型，或指向存在的表的外键）</item>
    /// <item>id 唯一（重复会打印冲突的两行行号）</item>
    /// <item>每个值能被列类型解析（含浮点数用不变文化解析，避免小数点跟着系统区域变）</item>
    /// <item>外键值必须指向目标表里存在的 id（空值表示"无引用"，允许）</item>
    /// </list>
    /// </summary>
    public static class ConfigValidator
    {
        public static List<string> Validate(IReadOnlyList<CsvTable> tables)
        {
            var errors = new List<string>();
            var byName = new Dictionary<string, CsvTable>(StringComparer.Ordinal);

            for (int i = 0; i < tables.Count; i++)
            {
                CsvTable table = tables[i];
                if (byName.ContainsKey(table.Name))
                    errors.Add($"表名重复：{table.Name}（CSV 文件名必须唯一）");
                else
                    byName[table.Name] = table;
            }

            var idIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (CsvTable table in byName.Values)
                idIndexByName[table.Name] = Array.IndexOf(table.Fields, "id");

            foreach (CsvTable table in byName.Values)
                ValidateTable(table, byName, idIndexByName, errors);

            return errors;
        }

        private static void ValidateTable(CsvTable table, Dictionary<string, CsvTable> byName,
            Dictionary<string, int> idIndexByName, List<string> errors)
        {
            int idIndex = idIndexByName[table.Name];
            if (idIndex < 0)
            {
                errors.Add($"{table.Name}.csv：缺少主键列 'id'");
                return; // 没有主键，后续校验没有意义
            }
            if (table.Types[idIndex] != "int")
                errors.Add($"{table.Name}.csv：主键 'id' 的类型必须是 int（当前 {table.Types[idIndex]}）");

            for (int c = 0; c < table.ColumnCount; c++)
            {
                string type = table.Types[c];
                if (ConfigTypeMap.IsBuiltin(type))
                    continue;
                if (ConfigTypeMap.IsRowReference(type))
                {
                    string referenced = ConfigTypeMap.ReferencedTable(type);
                    if (!byName.ContainsKey(referenced))
                        errors.Add($"{table.Name}.csv 列 '{table.Fields[c]}'：外键类型 {type} 指向的表 {referenced} 不存在" +
                                   $"（需要有 {referenced}.csv）");
                    continue;
                }
                errors.Add($"{table.Name}.csv 列 '{table.Fields[c]}'：未知类型 '{type}'" +
                           "（支持 int / float / bool / string，或其它表的行类型名如 ItemConfig）");
            }

            var seenIds = new Dictionary<int, int>(); // id → 首次出现的行号
            for (int r = 0; r < table.Rows.Count; r++)
            {
                string[] row = table.Rows[r];
                int line = table.LineNumberOf(r);

                if (!int.TryParse(row[idIndex], out int id))
                {
                    errors.Add($"{table.Name}.csv 第 {line} 行：主键 id '{row[idIndex]}' 不是整数");
                    continue;
                }
                if (seenIds.TryGetValue(id, out int firstLine))
                {
                    errors.Add($"{table.Name}.csv 第 {line} 行：主键 id {id} 与第 {firstLine} 行重复");
                    continue;
                }
                seenIds[id] = line;

                for (int c = 0; c < table.ColumnCount; c++)
                {
                    if (c == idIndex)
                        continue;
                    string type = table.Types[c];
                    string value = row[c];

                    if (ConfigTypeMap.IsBuiltin(type))
                    {
                        if (!ConfigTypeMap.TryConvert(type, value, out _))
                            errors.Add($"{table.Name}.csv 第 {line} 行 列 '{table.Fields[c]}'：'{value}' 无法解析为 {type}");
                        continue;
                    }
                    if (!ConfigTypeMap.IsRowReference(type) || !byName.ContainsKey(ConfigTypeMap.ReferencedTable(type)))
                        continue; // 类型问题已在上面报过

                    if (string.IsNullOrEmpty(value))
                        continue; // 空 = 无引用

                    CsvTable target = byName[ConfigTypeMap.ReferencedTable(type)];
                    if (!int.TryParse(value, out int refId))
                    {
                        errors.Add($"{table.Name}.csv 第 {line} 行 列 '{table.Fields[c]}'：外键 '{value}' 不是整数 id");
                        continue;
                    }
                    if (!ContainsId(target, idIndexByName[target.Name], refId))
                        errors.Add($"{table.Name}.csv 第 {line} 行 列 '{table.Fields[c]}'：外键 {value} 在 {target.Name} 表里不存在");
                }
            }
        }

        private static bool ContainsId(CsvTable table, int idIndex, int id)
        {
            if (idIndex < 0)
                return false;
            for (int r = 0; r < table.Rows.Count; r++)
            {
                if (int.TryParse(table.Rows[r][idIndex], out int value) && value == id)
                    return true;
            }
            return false;
        }
    }
}
