using UnityEngine;

namespace Template.Core.Extensions
{
    /// <summary>
    /// Component / GameObject 高频扩展。
    /// </summary>
    public static class ComponentExtensions
    {
        /// <summary>
        /// 获取组件，不存在则添加（仅运行时安全，组件不要求可序列化配置时适用）。
        /// 替代手写 GetComponent ?? AddComponent 的样板。
        /// </summary>
        public static T GetOrAddComponent<T>(this Component component) where T : Component
        {
            if (!component.TryGetComponent(out T result))
                result = component.gameObject.AddComponent<T>();
            return result;
        }
    }
}
