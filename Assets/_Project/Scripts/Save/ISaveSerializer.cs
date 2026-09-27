namespace Template.Save
{
    /// <summary>
    /// 存档序列化抽象。默认实现是 Unity 内置的 <see cref="JsonSaveSerializer"/>（JSON，人类可读、零依赖）；
    /// 需要二进制体积/速度（MessagePack）、或需要字典/多态（Newtonsoft）时换个实现即可，
    /// <see cref="SaveService"/> 与各分片都不动。
    ///
    /// 约定：<see cref="Deserialize{T}"/> 遇到损坏内容**抛异常**（由调用方决定降级策略，
    /// 别在这里悄悄返回 default —— 那会把"坏档"伪装成"空档"）。
    /// </summary>
    public interface ISaveSerializer
    {
        string Serialize<T>(T value);

        T Deserialize<T>(string text);
    }
}
