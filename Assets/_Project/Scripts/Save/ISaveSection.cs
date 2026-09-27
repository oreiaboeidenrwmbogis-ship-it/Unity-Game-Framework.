namespace Template.Save
{
    /// <summary>
    /// 存档分片契约：**谁的数据谁自己存/读** —— 背包、任务、成就各实现一个，
    /// 互不认识存档系统内部结构（沿用模块解耦思路）。分片在 Boot 阶段注册给 <see cref="SaveService"/>。
    ///
    /// 两级版本的分工：
    /// <list type="bullet">
    /// <item>根版本（<see cref="SaveData.Version"/>）表达**整体结构**的变化，由迁移链处理；</item>
    /// <item>分片自己的载荷向后兼容由**分片自己负责** —— 想在载荷里再放一个小版本号也行（推荐）。</item>
    /// </list>
    ///
    /// 实现示例：
    /// <code>
    /// private sealed class InventorySection : ISaveSection
    /// {
    ///     public string Key => "inventory.items";
    ///     public string Capture(ISaveSerializer s) => s.Serialize(_state);          // _state 是 [Serializable] 类
    ///     public bool Restore(string payload, ISaveSerializer s)
    ///     {
    ///         try { _state = s.Deserialize&lt;InventoryState&gt;(payload); return true; }
    ///         catch { return false; }                                               // 读不了 → 走默认值
    ///     }
    /// }
    /// </code>
    /// </summary>
    public interface ISaveSection
    {
        /// <summary>唯一键（约定 "模块.用途"，如 "inventory.items"）；重复注册会被拒绝。</summary>
        string Key { get; }

        /// <summary>把当前状态写成一段可序列化文本。</summary>
        string Capture(ISaveSerializer serializer);

        /// <summary>
        /// 从载荷恢复状态。返回 false = "这段数据读不了"（版本不符/内容异常）——
        /// 存档系统会记日志但**不中断整个读档**：其它分片照常恢复，缺的那部分走自身默认值。
        /// </summary>
        bool Restore(string payload, ISaveSerializer serializer);
    }
}
