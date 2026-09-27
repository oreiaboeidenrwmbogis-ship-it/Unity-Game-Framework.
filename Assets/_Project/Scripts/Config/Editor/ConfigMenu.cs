using Template.Core.Logging;
using UnityEditor;
using UnityEngine;

namespace Template.Config.EditorTools
{
    /// <summary>配置表菜单入口。</summary>
    public static class ConfigMenu
    {
        [MenuItem("Tools/配置表/全部导入", priority = 0)]
        public static void ImportAll()
        {
            if (ConfigImporter.RunImport())
                Log.Info("Config", "配置表导入完成");
        }

        [MenuItem("Tools/配置表/打开 CSV 目录", priority = 20)]
        public static void PingCsvFolder()
        {
            Object folder = AssetDatabase.LoadAssetAtPath<Object>(ConfigImporter.CsvFolder);
            if (folder == null)
            {
                Log.Warn("Config", "配置表目录不存在：{0}", ConfigImporter.CsvFolder);
                return;
            }
            EditorGUIUtility.PingObject(folder);
            Selection.activeObject = folder;
        }

        [MenuItem("Tools/配置表/选中配置库", priority = 21)]
        public static void SelectDatabase()
        {
            Object database = AssetDatabase.LoadAssetAtPath<Object>(ConfigImporter.DatabasePath);
            if (database == null)
            {
                Log.Warn("Config", "配置库还不存在（先跑一次「全部导入」）：{0}", ConfigImporter.DatabasePath);
                return;
            }
            EditorGUIUtility.PingObject(database);
            Selection.activeObject = database;
        }
    }

    /// <summary>
    /// CSV 变更后自动导入：改完表另存为 CSV，回到 Unity 就自动重生成代码 + 重写资产。
    /// 用 <c>delayCall</c> 推迟到本轮导入流程结束再跑 —— 在导入回调里改资产库/写脚本文件是自找麻烦。
    /// （写入前会比较内容，没变化不会碰文件，因此不会引发"改文件 → 重编译 → 再导入"的循环。）
    /// </summary>
    public sealed class ConfigCsvPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!TouchesConfigCsv(imported) && !TouchesConfigCsv(deleted)
                && !TouchesConfigCsv(moved) && !TouchesConfigCsv(movedFrom))
                return;

            EditorApplication.delayCall += () => ConfigImporter.RunImport();
        }

        private static bool TouchesConfigCsv(string[] paths)
        {
            if (paths == null)
                return false;
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i].Replace('\\', '/');
                if (path.StartsWith(ConfigImporter.CsvFolder + "/") && path.EndsWith(".csv"))
                    return true;
            }
            return false;
        }
    }
}
