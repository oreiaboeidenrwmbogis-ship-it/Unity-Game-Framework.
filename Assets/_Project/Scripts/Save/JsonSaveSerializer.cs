using UnityEngine;

namespace Template.Save
{
    /// <summary>
    /// 默认序列化实现：<see cref="JsonUtility"/> + 缩进输出 —— 存档是**人类可读**的，
    /// 出问题能直接打开看、能进版本库对比、玩家反馈时能让他把档发过来。
    ///
    /// 局限（JsonUtility 的固有约束，选它就是为了零依赖）：
    /// 不支持 <c>Dictionary</c> / 接口多态 / <c>null</c> 引用类型字段。
    /// 写存档数据时用 <c>List</c> + <c>[Serializable]</c> 类代替字典；真要字典/多态，
    /// 换一个 <see cref="ISaveSerializer"/> 实现（Newtonsoft / MessagePack）即可，上层不动。
    /// </summary>
    public sealed class JsonSaveSerializer : ISaveSerializer
    {
        public string Serialize<T>(T value) => JsonUtility.ToJson(value, prettyPrint: true);

        public T Deserialize<T>(string text) => JsonUtility.FromJson<T>(text);
    }
}
