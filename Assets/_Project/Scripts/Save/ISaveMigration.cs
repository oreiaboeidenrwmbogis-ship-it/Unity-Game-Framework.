namespace Template.Save
{
    /// <summary>
    /// 存档迁移的**一步**：把 <see cref="FromVersion"/> 的存档改造成 FromVersion + 1 的形状。
    ///
    /// 长期维护的生命线：老玩家手里是几年前的档，改一次结构就废一次档是不可接受的。
    /// 规矩：
    /// <list type="bullet">
    /// <item>每步只负责相邻两个版本之间的差异（v2→v3 的迁移不必认识 v1）；</item>
    /// <item>迁移只做**结构搬运**（补默认值、搬字段、重写分片载荷），不做业务判断 —— 纯函数才好测；</item>
    /// <item>加新版本时：<c>SaveService.CurrentVersion + 1</c>，并补一条 FromVersion = 旧版本的迁移。</item>
    /// </list>
    ///
    /// 迁移可以读取/重写分片载荷（例如某分片字段改名了）：
    /// <code>
    /// var entry = data.FindSection("inventory.items");
    /// var old = serializer.Deserialize&lt;InventoryStateV1&gt;(entry.Payload);
    /// data.SetSection(entry.Key, serializer.Serialize(new InventoryStateV2 { ... }));
    /// </code>
    /// </summary>
    public interface ISaveMigration
    {
        /// <summary>本步迁移的起始版本（跑完后存档版本变为 FromVersion + 1）。</summary>
        int FromVersion { get; }

        /// <summary>就地改造存档数据。</summary>
        void Apply(SaveData data, ISaveSerializer serializer);
    }
}
