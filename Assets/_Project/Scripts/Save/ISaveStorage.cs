namespace Template.Save
{
    /// <summary>
    /// 槽位存储抽象：只管"把一段文本按槽位存/取/删"，不认识存档结构（那是序列化与
    /// <see cref="SaveService"/> 的事）。默认实现 <see cref="FileSaveStorage"/> 写本地文件；
    /// 将来接云存档（Steam Cloud / 自建服务）时换一个实现即可，上层 API 不变。
    ///
    /// 约定：读取失败不要抛异常，返回 null（"没有档"与"档坏了"由上层按同一路径处理：
    /// 先试主档、再试备份、最后走降级）。
    /// </summary>
    public interface ISaveStorage
    {
        /// <summary>该槽位是否已有存档（主档存在即可）。</summary>
        bool Exists(int slot);

        /// <summary>读主档全文；不存在或读取失败返回 null。</summary>
        string Load(int slot);

        /// <summary>读备份档（上一次成功保存前的版本）；没有备份返回 null。</summary>
        string LoadBackup(int slot);

        /// <summary>写档（实现方负责"先写临时文件再替换"的原子写，断电不毁档）。</summary>
        void Save(int slot, string content);

        /// <summary>删除该槽位（含备份）。不存在时是空操作。</summary>
        void Delete(int slot);

        /// <summary>已有槽位编号（升序）。</summary>
        int[] ListSlots();
    }
}
