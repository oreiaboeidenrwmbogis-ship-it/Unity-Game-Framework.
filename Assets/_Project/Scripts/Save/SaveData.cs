using System;
using System.Collections.Generic;

namespace Template.Save
{
    /// <summary>
    /// 存档根对象：**版本号** + 元数据 + 各系统自己的数据分片。
    ///
    /// 为什么根对象这么"薄"：存档内容随游戏变化，而存档系统不该认识任何具体业务数据。
    /// 各系统（背包/任务/成就）实现 <see cref="ISaveSection"/> 各存各的，根对象只是容器 ——
    /// 单个分片坏了不影响其它分片，加新系统也不用改存档系统。
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>存档格式版本（迁移链的输入；结构变更时 +1 并在 SaveService 补一条迁移）。</summary>
        public int Version;

        /// <summary>存档列表要显示的元信息。</summary>
        public SaveMeta Meta = new SaveMeta();

        /// <summary>数据分片（键唯一）。</summary>
        public List<SaveSectionEntry> Sections = new List<SaveSectionEntry>();

        public SaveSectionEntry FindSection(string key)
        {
            for (int i = 0; i < Sections.Count; i++)
            {
                if (string.Equals(Sections[i].Key, key, StringComparison.Ordinal))
                    return Sections[i];
            }
            return null;
        }

        /// <summary>写入/覆盖一个分片载荷。</summary>
        public void SetSection(string key, string payload)
        {
            SaveSectionEntry entry = FindSection(key);
            if (entry != null)
            {
                entry.Payload = payload;
                return;
            }
            Sections.Add(new SaveSectionEntry { Key = key, Payload = payload });
        }
    }

    /// <summary>存档元信息（存档列表界面用）。</summary>
    [Serializable]
    public sealed class SaveMeta
    {
        /// <summary>保存时刻（UTC ticks —— JsonUtility 不认 DateTime，所以存 long）。</summary>
        public long SavedAtUtcTicks;

        /// <summary>列表里显示的摘要（业务填，如"第 3 章 · 12 分钟"）。</summary>
        public string Summary;

        /// <summary>写入时的应用版本（排查跨版本问题用）。</summary>
        public string AppVersion;
    }

    /// <summary>一个数据分片：键 + 该分片自己的 JSON 载荷。</summary>
    [Serializable]
    public sealed class SaveSectionEntry
    {
        public string Key;
        public string Payload;
    }

    /// <summary>槽位概览（只读视图，供存档列表界面用）。</summary>
    public readonly struct SaveSlotInfo
    {
        public readonly int Slot;
        public readonly int Version;
        public readonly string Summary;
        public readonly string AppVersion;

        /// <summary>保存时刻（本地时间；ticks 为 0 时返回 <see cref="DateTime.MinValue"/>）。</summary>
        public readonly DateTime SavedAtLocal;

        public SaveSlotInfo(int slot, SaveData data)
        {
            Slot = slot;
            Version = data.Version;
            Summary = data.Meta.Summary;
            AppVersion = data.Meta.AppVersion;
            SavedAtLocal = data.Meta.SavedAtUtcTicks <= 0
                ? DateTime.MinValue
                : new DateTime(data.Meta.SavedAtUtcTicks, DateTimeKind.Utc).ToLocalTime();
        }

        public override string ToString()
            => $"槽位 {Slot}：v{Version} · {Summary ?? "(无摘要)"} · {SavedAtLocal:yyyy-MM-dd HH:mm}";
    }
}
