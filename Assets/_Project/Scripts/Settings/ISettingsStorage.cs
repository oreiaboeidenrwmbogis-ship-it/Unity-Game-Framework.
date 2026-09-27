namespace Template.Settings
{
    /// <summary>
    /// 设置持久化后端（存取的是设置文件全文，格式由 <see cref="SettingsService"/> 决定）。
    /// 默认实现 <see cref="JsonFileSettingsStorage"/> 写 persistentDataPath；
    /// 需要云同步 / 多份配置（如"账号设置"+"本机设置"）时换一个实现即可，上层 API 不变。
    /// </summary>
    public interface ISettingsStorage
    {
        /// <summary>读取设置文件全文；文件不存在或读取失败返回 null（首次运行属正常）。</summary>
        string Load();

        /// <summary>写回设置文件全文。实现方必须自行兜底异常 —— 写设置失败不应打断游戏。</summary>
        void Save(string content);
    }
}
