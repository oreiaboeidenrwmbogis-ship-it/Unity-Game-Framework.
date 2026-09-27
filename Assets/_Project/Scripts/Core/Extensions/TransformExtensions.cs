using UnityEngine;

namespace Template.Core.Extensions
{
    /// <summary>
    /// Transform 高频扩展。只收录“真正高频、一行样板”的操作，避免无意义的方法堆积。
    /// </summary>
    public static class TransformExtensions
    {
        /// <summary>重置本地 位置 / 旋转 / 缩放 为单位值。</summary>
        public static void ResetLocal(this Transform t)
        {
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
        }

        public static void SetPositionX(this Transform t, float x)
        {
            Vector3 p = t.position;
            p.x = x;
            t.position = p;
        }

        public static void SetPositionY(this Transform t, float y)
        {
            Vector3 p = t.position;
            p.y = y;
            t.position = p;
        }

        public static void SetPositionZ(this Transform t, float z)
        {
            Vector3 p = t.position;
            p.z = z;
            t.position = p;
        }

        public static void SetLocalPositionX(this Transform t, float x)
        {
            Vector3 p = t.localPosition;
            p.x = x;
            t.localPosition = p;
        }

        public static void SetLocalPositionY(this Transform t, float y)
        {
            Vector3 p = t.localPosition;
            p.y = y;
            t.localPosition = p;
        }

        public static void SetLocalPositionZ(this Transform t, float z)
        {
            Vector3 p = t.localPosition;
            p.z = z;
            t.localPosition = p;
        }

        /// <summary>
        /// 销毁全部子物体（运行时安全）。先脱离父级再销毁，避免延迟销毁期间新逻辑挂到已标记删除的节点上。
        /// </summary>
        public static void DestroyChildren(this Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                Transform child = t.GetChild(i);
                child.SetParent(null);
                Object.Destroy(child.gameObject);
            }
        }

        /// <summary>
        /// 立即销毁全部子物体。仅限编辑器与停止态（OnValidate 等场景）使用，
        /// 运行时请用 <see cref="DestroyChildren"/>（DestroyImmediate 在运行中绕过延迟销毁可能破坏生命周期）。
        /// </summary>
        public static void DestroyChildrenImmediate(this Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(t.GetChild(i).gameObject);
        }
    }
}
