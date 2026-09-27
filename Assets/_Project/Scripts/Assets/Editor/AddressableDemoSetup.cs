using Template.Core.Logging;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Template.Assets.EditorTools
{
    /// <summary>
    /// 一键把示例资产标记为 Addressable —— 让 3.3 的资源加载演示开箱可验收。
    ///
    /// 为什么不自动做：Addressables 的分组/地址/标签本来是在 **Window → Asset Management →
    /// Addressables → Groups** 窗口里手工维护的，替使用者擅自改设置是很讨厌的行为。
    /// 所以这里只提供一个**显式菜单**，点一下走完这几步（用默认分组，不新建分组结构）：
    /// <list type="number">
    /// <item>把示例配置表资产（下方 <see cref="DemoAssetPaths"/> 里列出的）加进 Addressables 默认分组；</item>
    /// <item>地址设为资产名（如 <c>ItemTable</c>）；</item>
    /// <item>统一打上 Label <c>demo_config</c>（用于演示"按 Label 整组加载/释放"）。</item>
    /// </list>
    /// 正式项目请按业务分组（关卡/ui/audio…），并把 Label 约定写进 README。
    /// </summary>
    public static class AddressableDemoSetup
    {
        /// <summary>演示用的 Label（整组加载/释放的演示靠它）。</summary>
        public const string DemoLabel = "demo_config";

        /// <summary>演示要标记的示例资产（增删表时同步这里；不存在的路径会被跳过并给出日志）。</summary>
        private static readonly string[] DemoAssetPaths =
        {
            "Assets/_Project/Data/Configs/Generated/ItemTable.asset",
        };

        [MenuItem("Tools/资源/初始化 Addressables 演示分组", priority = 0)]
        public static void Setup()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
            {
                Log.Error("Assets", "拿不到 Addressables 设置（创建失败？）——确认已安装 com.unity.addressables");
                return;
            }

            AddressableAssetGroup group = settings.DefaultGroup;
            if (group == null)
            {
                Log.Error("Assets", "Addressables 没有默认分组，请在 Addressables Groups 窗口里先建一个");
                return;
            }

            int marked = 0;
            for (int i = 0; i < DemoAssetPaths.Length; i++)
            {
                string path = DemoAssetPaths[i];
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid))
                {
                    Log.Warn("Assets", "跳过（资产不存在）：{0}", path);
                    continue;
                }

                AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
                entry.address = System.IO.Path.GetFileNameWithoutExtension(path); // 地址 = 资产名
                entry.SetLabel(DemoLabel, true);
                marked++;
            }

            AssetDatabase.SaveAssets();
            Log.Info("Assets", "已标记 {0} 个示例资产为 Addressable：Label='{1}'（分组 {2}）" +
                " —— 现在 Play 可以看到资源加载演示", marked, DemoLabel, group.Name);
        }

        [MenuItem("Tools/资源/打开 Addressables Groups 窗口", priority = 20)]
        public static void OpenGroupsWindow() => EditorApplication.ExecuteMenuItem("Window/Asset Management/Addressables/Groups");
    }
}
