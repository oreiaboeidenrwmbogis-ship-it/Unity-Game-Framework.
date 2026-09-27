using System;
using System.Collections.Generic;
using UnityEngine;

namespace Template.Core.Extensions
{
    /// <summary>
    /// 列表类高频扩展：空安全判断、去重添加、无序 O(1) 移除、随机取样。
    /// 为空安全而选 IReadOnlyList 作入参（List / 数组均可直接用）。
    /// </summary>
    public static class ListExtensions
    {
        /// <summary>null 或空表的判断（数组 / List 通用）。</summary>
        public static bool IsNullOrEmpty<T>(this IReadOnlyList<T> list)
            => list == null || list.Count == 0;

        /// <summary>仅当元素不存在时添加；返回是否新增。适合“注册一次”的场景（监听器列表、去重集合）。</summary>
        public static bool AddUnique<T>(this IList<T> list, T item)
        {
            if (list.Contains(item))
                return false;
            list.Add(item);
            return true;
        }

        /// <summary>
        /// 无序移除：把末尾元素搬移到 index 后删除尾部，O(1) 且不搬移中间元素。
        /// 仅当容器不关心顺序时可用 —— 典型场景：对象池的空闲表、特效注册表。
        /// </summary>
        public static void RemoveAtSwap<T>(this IList<T> list, int index)
        {
            int last = list.Count - 1;
            if (index < 0 || index > last)
                throw new ArgumentOutOfRangeException(nameof(index), "RemoveAtSwap: 索引越界");

            list[index] = list[last];
            list.RemoveAt(last);
        }

        /// <summary>随机取一个元素；空表返回 default 并告警（不抛异常，适合非关键路径）。</summary>
        public static T RandomOrDefault<T>(this IReadOnlyList<T> list)
        {
            if (list == null || list.Count == 0)
            {
                Debug.LogWarning("ListExtensions.RandomOrDefault: 列表为空，返回 default。");
                return default;
            }
            return list[UnityEngine.Random.Range(0, list.Count)];
        }
    }
}
