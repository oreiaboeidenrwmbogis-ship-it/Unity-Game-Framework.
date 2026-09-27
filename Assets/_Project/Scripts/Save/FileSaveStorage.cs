using System;
using System.Collections.Generic;
using System.IO;
using Template.Core.Logging;
using UnityEngine;

namespace Template.Save
{
    /// <summary>
    /// 默认存储：`persistentDataPath/saves/slot_N.json`（每槽一个文件 + 同名 `.bak` 备份）。
    ///
    /// **原子写盘**：先把内容写进 `slot_N.json.tmp`，再一次性替换主档 —— 替换前把旧主档留成 `.bak`。
    /// 这样"写一半断电/崩溃"只会损失这一次保存，不会毁掉上一份好档（这是商业级存档的最低要求）。
    ///
    /// 目录可注入：测试传临时目录，避免污染玩家真档。
    /// </summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        private const string FileSuffix = ".json";
        private const string BackupSuffix = ".bak";
        private const string TempSuffix = ".tmp";

        private readonly string _root;

        /// <summary>存档目录。</summary>
        public string Root => _root;

        public FileSaveStorage() : this(Path.Combine(Application.persistentDataPath, "saves")) { }

        public FileSaveStorage(string rootDirectory)
        {
            _root = rootDirectory;
        }

        public bool Exists(int slot) => slot >= 0 && File.Exists(PathOf(slot));

        public string Load(int slot) => ReadText(PathOf(slot));

        public string LoadBackup(int slot) => ReadText(PathOf(slot) + BackupSuffix);

        public void Save(int slot, string content)
        {
            if (slot < 0)
            {
                Log.Error("Save", "非法槽位 {0}，拒绝写入", slot);
                return;
            }

            try
            {
                Directory.CreateDirectory(_root);
                string target = PathOf(slot);
                string temp = target + TempSuffix;

                File.WriteAllText(temp, content);

                if (!File.Exists(target))
                {
                    File.Move(temp, target);
                    return;
                }

                try
                {
                    File.Replace(temp, target, target + BackupSuffix); // 原子替换，旧档自动变成 .bak
                }
                catch (Exception replaceError)
                {
                    // 个别平台/Mono 实现对 File.Replace 支持不全。退化路径：先备份、再删主档、最后改名。
                    // 不如原子替换安全，但**备份已在**（真出事仍能从 .bak 恢复），远好过写不进去。
                    Log.Verbose("Save", "File.Replace 不可用（{0}），改用备份+移动", replaceError.Message);
                    File.Copy(target, target + BackupSuffix, overwrite: true);
                    File.Delete(target);
                    File.Move(temp, target);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Save", "槽位 {0} 写入失败：{1}", slot, ex.Message);
            }
        }

        public void Delete(int slot)
        {
            if (slot < 0)
                return;
            TryDelete(PathOf(slot));
            TryDelete(PathOf(slot) + BackupSuffix);
            TryDelete(PathOf(slot) + TempSuffix);
        }

        public int[] ListSlots()
        {
            var slots = new List<int>();
            if (!Directory.Exists(_root))
                return slots.ToArray();

            foreach (string fullPath in Directory.GetFiles(_root, "slot_*" + FileSuffix, SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileNameWithoutExtension(fullPath); // slot_3
                if (name.Length > 5 && int.TryParse(name.Substring(5), out int slot) && slot >= 0)
                    slots.Add(slot);
            }
            slots.Sort();
            return slots.ToArray();
        }

        private string PathOf(int slot) => Path.Combine(_root, "slot_" + slot + FileSuffix);

        private static string ReadText(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception ex)
            {
                Log.Error("Save", "读取存档文件失败 <{0}>：{1}", path, ex.Message);
                return null;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Warn("Save", "删除存档文件失败 <{0}>：{1}", path, ex.Message);
            }
        }
    }
}
