using System;

namespace Template.Config.EditorTools
{
    /// <summary>
    /// CSV 类型 ↔ CLR 类型 ↔ 生成代码类型名的**唯一映射表**（校验 / 代码生成 / 资产导入三处共用，
    /// 避免各写一份规则而慢慢跑偏）。
    ///
    /// 支持的类型：
    /// <list type="bullet">
    /// <item><c>int</c> / <c>float</c> / <c>bool</c> / <c>string</c> —— 基础类型</item>
    /// <item>其它表的**行类型名**（如 <c>ItemConfig</c>）—— 外键，CSV 里写目标行的 id</item>
    /// </list>
    /// </summary>
    public static class ConfigTypeMap
    {
        /// <summary>行类型后缀：表 Item → 行类型 ItemConfig。</summary>
        public const string RowTypeSuffix = "Config";

        /// <summary>表类型后缀：表 Item → 表类型 ItemTable。</summary>
        public const string TableTypeSuffix = "Table";

        public static bool IsBuiltin(string csvType)
            => csvType == "int" || csvType == "float" || csvType == "bool" || csvType == "string";

        /// <summary>是否是外键（写的是别的表的行类型名）。</summary>
        public static bool IsRowReference(string csvType)
            => !string.IsNullOrEmpty(csvType)
               && csvType.EndsWith(RowTypeSuffix, StringComparison.Ordinal)
               && csvType.Length > RowTypeSuffix.Length;

        /// <summary>外键指向的表名（ItemConfig → Item）。</summary>
        public static string ReferencedTable(string rowType)
            => rowType.Substring(0, rowType.Length - RowTypeSuffix.Length);

        /// <summary>行类型名（Item → ItemConfig）。</summary>
        public static string RowTypeOf(string tableName) => tableName + RowTypeSuffix;

        /// <summary>表类型名（Item → ItemTable）。</summary>
        public static string TableTypeOf(string tableName) => tableName + TableTypeSuffix;

        /// <summary>基础类型对应的 CLR 类型；外键/未知返回 null。</summary>
        public static Type ClrTypeOf(string csvType)
        {
            switch (csvType)
            {
                case "int": return typeof(int);
                case "float": return typeof(float);
                case "bool": return typeof(bool);
                case "string": return typeof(string);
                default: return null;
            }
        }

        /// <summary>把 CSV 文本解析成基础类型的值（调用前请先用 <see cref="ConfigValidator"/> 校验）。</summary>
        public static bool TryConvert(string csvType, string value, out object result)
        {
            result = null;
            switch (csvType)
            {
                case "int":
                    if (int.TryParse(value, out int i)) { result = i; return true; }
                    return false;
                case "float":
                    if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float f)) { result = f; return true; }
                    return false;
                case "bool":
                    if (bool.TryParse(value, out bool b)) { result = b; return true; }
                    return false;
                case "string":
                    result = value ?? string.Empty;
                    return true;
                default:
                    return false;
            }
        }
    }
}
