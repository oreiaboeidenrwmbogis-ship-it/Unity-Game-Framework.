using UnityEngine;

namespace Template.Core.Extensions
{
    /// <summary>
    /// 向量高频扩展：单轴改写与投影。避免手写 new Vector3(x, v.y, v.z) 样板。
    /// </summary>
    public static class VectorExtensions
    {
        public static Vector3 WithX(this Vector3 v, float x) => new Vector3(x, v.y, v.z);

        public static Vector3 WithY(this Vector3 v, float y) => new Vector3(v.x, y, v.z);

        public static Vector3 WithZ(this Vector3 v, float z) => new Vector3(v.x, v.y, z);

        /// <summary>投影到 XZ 平面（把带 Y 的方向压平，常用于地面移动 / 看向目标）。</summary>
        public static Vector3 FlattenY(this Vector3 v) => new Vector3(v.x, 0f, v.z);

        public static Vector2 WithX(this Vector2 v, float x) => new Vector2(x, v.y);

        public static Vector2 WithY(this Vector2 v, float y) => new Vector2(v.x, y);
    }
}
