using System.Collections.Generic;
using Template.Core.Logging;

namespace Template.Core.App
{
    /// <summary>
    /// 场景切换参数袋：LoadScene 前放入数据（读档信息、出生点、目标关卡等），
    /// 场景初始化代码在 SceneLoadedEvent.Context 里读取 —— 替代“切场景后全局变量到处飞”。
    /// 键为字符串，值为任意类型（少量参数场景；海量/强类型建议定义专用上下文类并自行传递）。
    /// </summary>
    public sealed class SceneContext
    {
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>();

        /// <summary>写入参数（链式：ctx.Set("slot", 2).Set("spawn", "door_a")）。</summary>
        public SceneContext Set<T>(string key, T value)
        {
            _values[key] = value;
            return this;
        }

        /// <summary>读取参数（含类型检查）。键不存在或类型不匹配时告警并返回 default —— 不抛异常。</summary>
        public T Get<T>(string key)
        {
            if (_values.TryGetValue(key, out object value))
            {
                if (value is T typed)
                    return typed;

                Log.Error("Scene", "Context 键 '{0}' 类型不匹配（期望 {1}，实际 {2}）",
                    key, typeof(T).Name, value?.GetType().Name);
                return default;
            }

            Log.Warn("Scene", "Context 中不存在键 '{0}'，返回 default", key);
            return default;
        }

        public bool TryGet<T>(string key, out T value)
        {
            if (_values.TryGetValue(key, out object raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public bool ContainsKey(string key) => _values.ContainsKey(key);
    }
}
