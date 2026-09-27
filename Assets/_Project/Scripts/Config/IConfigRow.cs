namespace Template.Config
{
    /// <summary>
    /// 配置行契约：每张表的主键（CSV 里的 <c>id</c> 列）。
    /// 行类型由配置表管线**自动生成**（见 Scripts/Configs/Generated），不要手写。
    /// </summary>
    public interface IConfigRow
    {
        /// <summary>主键（表内唯一；导入时校验重复）。</summary>
        int Id { get; }
    }
}
