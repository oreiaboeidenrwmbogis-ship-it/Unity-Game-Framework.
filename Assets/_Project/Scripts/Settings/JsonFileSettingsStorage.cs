using System;
using System.IO;
using Template.Core.Logging;
using UnityEngine;

namespace Template.Settings
{
    /// <summary>
    /// 默认设置存储：`Application.persistentDataPath/settings.json`，人类可读（JSON 缩进输出），
    /// 出问题能直接打开看、手工改 —— 长期维护的项目里这点比"省几 KB"重要得多。
    ///
    /// 为什么不用 PlayerPrefs：Windows 上写注册表（清缓存清不掉、迁移困难）、非结构化、
    /// 且无法版本化。设置文件和阶段 3 的存档文件走同一套思路（版本号 + 迁移链）。
    /// </summary>
    public sealed class JsonFileSettingsStorage : ISettingsStorage
    {
        private readonly string _path;

        /// <summary>设置文件完整路径（日志/验收用）。</summary>
        public string Path => _path;

        public JsonFileSettingsStorage(string fileName = "settings.json")
        {
            _path = System.IO.Path.Combine(Application.persistentDataPath, fileName);
        }

        public string Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return null;
                return File.ReadAllText(_path);
            }
            catch (Exception ex)
            {
                // 读失败不致命：全用默认值，玩家只是"设置回到默认"，不该白屏
                Log.Error("Settings", "设置文件读取失败（改用默认值）: {0}", ex.Message);
                return null;
            }
        }

        public void Save(string content)
        {
            try
            {
                File.WriteAllText(_path, content);
            }
            catch (Exception ex)
            {
                Log.Error("Settings", "设置文件写入失败: {0}", ex.Message);
            }
        }
    }
}
