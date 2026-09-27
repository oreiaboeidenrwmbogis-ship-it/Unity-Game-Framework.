using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Template.Core.Logging;
using UnityEditor;
using UnityEngine;

namespace Template.Config.EditorTools
{
    /// <summary>
    /// 配置表导入流水线（**两段式**）：
    /// <list type="number">
    /// <item>解析 + 校验全部 CSV → 生成强类型代码；代码有变化就写盘 → Unity 重编译 → 重编译完成后进入 ②</item>
    /// <item>把校验通过的数据写进 ScriptableObject 资产，并维护配置库（<see cref="ConfigDatabase"/>）</item>
    /// </list>
    /// 为什么要分两段：**新表得先有"生成的类型"才能 CreateInstance**，而新类型要等重编译才存在；
    /// 只改数据（代码没变）时直接跑 ②，不必等重编译 —— 日常改数值是毫秒级反馈。
    ///
    /// 校验不通过 → **整体终止**，既不写代码也不写资产（绝不把半套数据放进运行期）。
    /// </summary>
    public static class ConfigImporter
    {
        /// <summary>CSV 源目录。</summary>
        public const string CsvFolder = "Assets/_Project/Data/Configs";

        /// <summary>配置资产输出目录。</summary>
        public const string AssetFolder = "Assets/_Project/Data/Configs/Generated";

        /// <summary>配置库资产路径（由导入工具维护，不要手工挪动）。</summary>
        public const string DatabasePath = AssetFolder + "/ConfigDatabase.asset";

        private const string PendingAssetsKey = "Template.Config.PendingAssetWrite";

        /// <summary>全量导入（菜单 Tools/配置表/全部导入；CSV 变更后也会自动跑一次）。</summary>
        public static bool RunImport()
        {
            if (!Directory.Exists(ToFullPath(CsvFolder)))
            {
                Log.Warn("Config", "没有找到配置表目录 {0}，跳过导入", CsvFolder);
                return false;
            }

            // ① 解析
            var tables = new List<CsvTable>();
            var errors = new List<string>();
            foreach (string fullPath in Directory.GetFiles(ToFullPath(CsvFolder), "*.csv", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileNameWithoutExtension(fullPath);
                if (!TryReadCsv(fullPath, out string text, out string readError))
                {
                    errors.Add(readError);
                    continue;
                }
                if (!ConfigCsvParser.TryParse(name, ToAssetPath(fullPath), text, out CsvTable table, out string error))
                    errors.Add(error);
                else
                    tables.Add(table);
            }

            if (tables.Count == 0 && errors.Count == 0)
            {
                Log.Warn("Config", "{0} 下没有 CSV 表，跳过导入", CsvFolder);
                return false;
            }

            // ② 校验
            errors.AddRange(ConfigValidator.Validate(tables));
            if (errors.Count > 0)
            {
                Log.Error("Config", "配置表导入中止：{0} 个问题 —— 未写入任何代码与资产", errors.Count);
                for (int i = 0; i < errors.Count; i++)
                    Log.Error("Config", "  {0}", errors[i]);
                return false;
            }

            // ③ 生成代码（内容没变就不写盘，避免无谓的脚本重编译）
            bool codeChanged = WriteGeneratedCode(tables);

            // ④ 写资产：代码刚变过就先等重编译（新类型还不存在）
            if (codeChanged)
            {
                SessionState.SetBool(PendingAssetsKey, true);
                Log.Info("Config", "生成的代码已更新，等脚本重编译后自动写入配置资产…");
                return true;
            }

            WriteAssets(tables);
            return true;
        }

        /// <summary>重编译完成后接着把资产写完（上一轮留下了待写标记才会执行）。</summary>
        [InitializeOnLoadMethod]
        private static void ResumePendingImport()
        {
            if (!SessionState.GetBool(PendingAssetsKey, false))
                return;
            EditorApplication.delayCall += () =>
            {
                SessionState.SetBool(PendingAssetsKey, false);
                RunImport();
            };
        }

        /// <summary>
        /// 读 CSV：校验是合法 UTF-8（非 UTF-8 直接拦住 —— 中文会变乱码），并剥掉 Excel「CSV UTF-8」
        /// 会写入的 BOM（不剥的话第一个字段名会带上看不见的 ﻿，表头就对不上了）。
        /// </summary>
        internal static bool TryReadCsv(string fullPath, out string text, out string error)
        {
            text = null;
            error = null;
            byte[] bytes = File.ReadAllBytes(fullPath);

            if (!CsvEncodingCheck.IsValidUtf8(bytes))
            {
                error = $"{ToAssetPath(fullPath)}：不是 UTF-8 编码（中文会变乱码）—— " +
                        "请在编辑器里「另存为」选 UTF-8（记事本 / VS Code / Excel 的「CSV UTF-8」都行）";
                return false;
            }

            int offset = CsvEncodingCheck.HasBom(bytes) ? 3 : 0;
            text = new UTF8Encoding(false).GetString(bytes, offset, bytes.Length - offset);
            return true;
        }

        // ── 代码生成 ──

        private static bool WriteGeneratedCode(IReadOnlyList<CsvTable> tables)
        {
            Dictionary<string, string> files = ConfigCodeGenerator.Generate(tables);
            bool changed = false;

            foreach (KeyValuePair<string, string> pair in files)
            {
                string fullPath = ToFullPath(pair.Key);
                if (File.Exists(fullPath) && Normalize(File.ReadAllText(fullPath)) == Normalize(pair.Value))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllText(fullPath, pair.Value);
                changed = true;
            }

            // 删掉已不存在表的生成物（删了 CSV 就该删代码，否则会留下孤儿类型）
            string generatedFull = ToFullPath(ConfigCodeGenerator.GeneratedFolder);
            if (Directory.Exists(generatedFull))
            {
                foreach (string existing in Directory.GetFiles(generatedFull, "*.cs", SearchOption.TopDirectoryOnly))
                {
                    if (files.ContainsKey(ToAssetPath(existing)))
                        continue;
                    File.Delete(existing);
                    Log.Info("Config", "已删除不再需要的生成文件：{0}", ToAssetPath(existing));
                    changed = true;
                }
            }

            if (changed)
                AssetDatabase.Refresh();
            Log.Info("Config", changed
                ? "已生成强类型访问代码：{0} 个文件（Configs.<表名>.Get(id)）"
                : "生成代码无变化（{0} 个文件）", files.Count);
            return changed;
        }

        // ── 资产写入 ──

        private static void WriteAssets(IReadOnlyList<CsvTable> tables)
        {
            string folderFull = ToFullPath(AssetFolder);
            if (!Directory.Exists(folderFull))
            {
                Directory.CreateDirectory(folderFull);
                AssetDatabase.Refresh(); // 让 Unity 先认得新目录，否则 CreateAsset 会失败
            }

            // 第一遍：建行对象 + 填基础列（此时还不知道外键目标，先跳过外键列）
            var rowsByTable = new Dictionary<string, List<object>>(StringComparer.Ordinal);
            for (int i = 0; i < tables.Count; i++)
            {
                CsvTable csv = tables[i];
                Type rowType = FindGeneratedType(ConfigTypeMap.RowTypeOf(csv.Name));
                if (rowType == null)
                {
                    Log.Error("Config", "找不到生成的行类型 {0} —— 生成代码可能还没编译完成，稍后重新导入",
                        ConfigTypeMap.RowTypeOf(csv.Name));
                    return;
                }

                var rows = new List<object>(csv.Rows.Count);
                for (int r = 0; r < csv.Rows.Count; r++)
                {
                    object row = Activator.CreateInstance(rowType);
                    FillScalarColumns(row, rowType, csv, csv.Rows[r]);
                    rows.Add(row);
                }
                rowsByTable[csv.Name] = rows;
            }

            // 第二遍：填外键（目标表的行此时都已建好；外键存在性已在校验阶段保证）
            for (int i = 0; i < tables.Count; i++)
            {
                CsvTable csv = tables[i];
                Type rowType = FindGeneratedType(ConfigTypeMap.RowTypeOf(csv.Name));
                List<object> rows = rowsByTable[csv.Name];
                for (int c = 0; c < csv.ColumnCount; c++)
                {
                    if (!ConfigTypeMap.IsRowReference(csv.Types[c]))
                        continue;
                    List<object> targets = rowsByTable[ConfigTypeMap.ReferencedTable(csv.Types[c])];
                    for (int r = 0; r < rows.Count; r++)
                    {
                        string value = csv.Rows[r][c];
                        if (string.IsNullOrEmpty(value))
                            continue;
                        int id = int.Parse(value);
                        object target = FindRowById(targets, id);
                        SetField(rows[r], rowType, csv.Fields[c], target);
                    }
                }
            }

            // 第三遍：写进各表资产，并维护配置库
            var tableAssets = new List<ScriptableObject>();
            int totalRows = 0;
            for (int i = 0; i < tables.Count; i++)
            {
                CsvTable csv = tables[i];
                Type tableType = FindGeneratedType(ConfigTypeMap.TableTypeOf(csv.Name));
                if (tableType == null)
                {
                    Log.Error("Config", "找不到生成的表类型 {0}", ConfigTypeMap.TableTypeOf(csv.Name));
                    return;
                }

                string assetPath = $"{AssetFolder}/{tableType.Name}.asset";
                var table = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                if (table == null)
                {
                    table = ScriptableObject.CreateInstance(tableType);
                    AssetDatabase.CreateAsset(table, assetPath);
                }

                List<object> rows = rowsByTable[csv.Name];
                InvokeEditorReplaceRows(table, tableType, rows, csv.SourcePath);
                EditorUtility.SetDirty(table);
                tableAssets.Add(table);
                totalRows += rows.Count;
            }

            DeleteOrphanAssets(tableAssets);
            WriteDatabase(tableAssets);
            AssetDatabase.SaveAssets();
            Log.Info("Config", "配置资产已写入：{0} 张表 / {1} 行 → {2}", tableAssets.Count, totalRows, AssetFolder);
        }

        private static void WriteDatabase(List<ScriptableObject> tables)
        {
            var database = AssetDatabase.LoadAssetAtPath<ConfigDatabase>(DatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<ConfigDatabase>();
                AssetDatabase.CreateAsset(database, DatabasePath);
            }
            database.EditorReplaceTables(tables);
            EditorUtility.SetDirty(database);
        }

        /// <summary>删掉源 CSV 已不存在的表资产（避免配置库里堆孤儿表）。</summary>
        private static void DeleteOrphanAssets(List<ScriptableObject> liveTables)
        {
            string folder = ToFullPath(AssetFolder);
            if (!Directory.Exists(folder))
                return;
            foreach (string fullPath in Directory.GetFiles(folder, "*.asset", SearchOption.TopDirectoryOnly))
            {
                string assetPath = ToAssetPath(fullPath);
                if (assetPath == DatabasePath)
                    continue;
                bool alive = false;
                for (int i = 0; i < liveTables.Count; i++)
                {
                    if (AssetDatabase.GetAssetPath(liveTables[i]) == assetPath)
                    {
                        alive = true;
                        break;
                    }
                }
                if (!alive)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                    Log.Info("Config", "已删除源 CSV 不存在的表资产：{0}", assetPath);
                }
            }
        }

        // ── 反射小工具（编辑器期一次性开销，不进包） ──

        private static void FillScalarColumns(object row, Type rowType, CsvTable csv, string[] values)
        {
            for (int c = 0; c < csv.ColumnCount; c++)
            {
                if (!ConfigTypeMap.IsBuiltin(csv.Types[c]))
                    continue; // 外键留给第二遍
                if (ConfigTypeMap.TryConvert(csv.Types[c], values[c], out object converted))
                    SetField(row, rowType, csv.Fields[c], converted);
            }
        }

        private static void SetField(object row, Type rowType, string fieldName, object value)
        {
            FieldInfo field = rowType.GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
            {
                Log.Error("Config", "行类型 {0} 里没有字段 '{1}'（生成的代码与 CSV 不同步？重新导入一次）",
                    rowType.Name, fieldName);
                return;
            }
            field.SetValue(row, value);
        }

        private static object FindRowById(List<object> rows, int id)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] is IConfigRow row && row.Id == id)
                    return rows[i];
            }
            return null;
        }

        private static void InvokeEditorReplaceRows(ScriptableObject table, Type tableType, List<object> rows, string sourceCsv)
        {
            MethodInfo method = tableType.GetMethod("EditorReplaceRows", BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
            {
                Log.Error("Config", "表类型 {0} 缺少 EditorReplaceRows（Template.Config 版本不匹配？）", tableType.Name);
                return;
            }
            method.Invoke(table, new object[] { rows, sourceCsv });
        }

        private static Type FindGeneratedType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(ConfigCodeGenerator.Namespace + "." + typeName, false);
                    if (type != null)
                        return type;
                }
                catch (Exception)
                {
                    // 动态程序集等不参与查找，忽略
                }
            }
            return null;
        }

        // ── 路径小工具 ──

        internal static string ToFullPath(string assetPath)
            => Path.Combine(Directory.GetCurrentDirectory(), assetPath);

        internal static string ToAssetPath(string fullPath)
            => fullPath.Replace('\\', '/').Replace(Directory.GetCurrentDirectory().Replace('\\', '/') + "/", string.Empty);

        /// <summary>比较生成物时统一换行符 —— 否则 CRLF/LF 之差会让每次导入都"看起来变了"而反复重编译。</summary>
        private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    }
}
