using System;
using System.Collections.Generic;
using System.Text;

namespace Template.Config.EditorTools
{
    /// <summary>
    /// CSV 编码检查（纯逻辑，可单测）。
    ///
    /// ⚠ 只能做"能可靠做的那半"：非法 UTF-8 字节序列能抓，**但 GBK 字节有时恰好是合法 UTF-8**
    /// （例如"木剑"的 GBK 字节 C4 BE BD A3 解出来是两个合法字符），这一类无法从字节层面可靠识别 ——
    /// 所以唯一可靠的约定是：**CSV 一律存成 UTF-8**。
    /// </summary>
    public static class CsvEncodingCheck
    {
        /// <summary>是否是 UTF-8 BOM（Excel 的「CSV UTF-8」会写入，不剥掉会污染第一个字段名）。</summary>
        public static bool HasBom(byte[] bytes)
            => bytes != null && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        /// <summary>是否是合法 UTF-8 字节序列（严格解码，非法序列会抛异常）。</summary>
        public static bool IsValidUtf8(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return true;
            try
            {
                new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
                return true;
            }
            catch (ArgumentException)
            {
                return false; // DecoderFallbackException 继承自 ArgumentException
            }
        }
    }

    /// <summary>一张 CSV 表的原始内容（纯数据，无 Unity 依赖 → 可直接单测）。</summary>
    public sealed class CsvTable
    {
        /// <summary>表名 = CSV 文件名（不含扩展名），如 "Item"。</summary>
        public string Name;

        /// <summary>来源路径（相对 Assets，写进生成物注释与日志）。</summary>
        public string SourcePath;

        /// <summary>第一行：字段名。</summary>
        public string[] Fields;

        /// <summary>第二行：类型（int/float/bool/string，或外键用的行类型名如 ItemConfig）。</summary>
        public string[] Types;

        /// <summary>第三行起：数据行。</summary>
        public List<string[]> Rows = new List<string[]>();

        public int ColumnCount => Fields != null ? Fields.Length : 0;

        /// <summary>数据行在文件中的行号（用于报错定位；第 1、2 行是字段与类型）。</summary>
        public int LineNumberOf(int rowIndex) => rowIndex + 3;
    }

    /// <summary>
    /// CSV 解析器（表定义约定）：
    /// <code>
    /// id,name,price        ← 第 1 行：字段名
    /// int,string,int       ← 第 2 行：类型
    /// 1001,木剑,30          ← 第 3 行起：数据
    /// </code>
    /// 支持：字段用双引号包裹（可含逗号、换行，"" 表示一个引号）、CRLF/LF 混用、
    /// 空行与 <c>#</c> 开头的注释行（便于在表里写备注）。
    ///
    /// 结构与类型是否"合法"不在本类判断 —— 那是 <see cref="ConfigValidator"/> 的职责，
    /// 解析器只负责把文本拆成行列。
    /// </summary>
    public static class ConfigCsvParser
    {
        public static bool TryParse(string name, string sourcePath, string text, out CsvTable table, out string error)
        {
            table = null;
            error = null;

            List<string[]> rows = Tokenize(text);
            var data = new List<string[]>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                string[] row = rows[i];
                if (row.Length == 1 && string.IsNullOrWhiteSpace(row[0]))
                    continue;                                   // 空行
                if (row.Length > 0 && row[0].TrimStart().StartsWith("#"))
                    continue;                                   // 注释行
                data.Add(row);
            }

            if (data.Count < 2)
            {
                error = $"{name}.csv：至少要有两行 —— 第 1 行字段名、第 2 行类型（当前只有 {data.Count} 行有效内容）";
                return false;
            }

            var result = new CsvTable
            {
                Name = name,
                SourcePath = sourcePath,
                Fields = TrimAll(data[0]),
                Types = TrimAll(data[1]),
            };

            if (result.Fields.Length != result.Types.Length)
            {
                error = $"{name}.csv：字段名 {result.Fields.Length} 列、类型 {result.Types.Length} 列，数量不一致";
                return false;
            }

            for (int i = 2; i < data.Count; i++)
            {
                if (data[i].Length != result.ColumnCount)
                {
                    error = $"{name}.csv 第 {i + 1} 行：{data[i].Length} 列，期望 {result.ColumnCount} 列（字段名/类型行对不上）";
                    return false;
                }
                result.Rows.Add(TrimAll(data[i]));
            }

            table = result;
            return true;
        }

        /// <summary>按 CSV 规则切分成行列（引号内的逗号/换行不算分隔）。</summary>
        internal static List<string[]> Tokenize(string text)
        {
            var rows = new List<string[]>();
            var fields = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            bool fieldStarted = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"'); // 转义的引号
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        fieldStarted = true;
                        break;
                    case ',':
                        fields.Add(field.ToString());
                        field.Length = 0;
                        fieldStarted = false;
                        break;
                    case '\r':
                        break; // 统一按 \n 断行
                    case '\n':
                        fields.Add(field.ToString());
                        rows.Add(fields.ToArray());
                        fields.Clear();
                        field.Length = 0;
                        fieldStarted = false;
                        break;
                    default:
                        field.Append(c);
                        fieldStarted = true;
                        break;
                }
            }

            if (fieldStarted || field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                rows.Add(fields.ToArray());
            }
            return rows;
        }

        private static string[] TrimAll(string[] row)
        {
            var result = new string[row.Length];
            for (int i = 0; i < row.Length; i++)
                result[i] = row[i]?.Trim();
            return result;
        }
    }
}
