using System;
using System.Collections.Generic;
using Template.Save;

namespace Template.Demo
{
    /// <summary>
    /// 示例存档分片（演示用一个假背包）：演示"**谁的数据谁自己存**"——
    /// 它自己定义 <c>[Serializable]</c> 状态结构、自己序列化，存档系统完全不认识它，
    /// 也不知道"金币/物品"是什么。真实项目里背包/任务/成就各写一个这样的分片即可。
    /// </summary>
    internal sealed class DemoInventorySection : ISaveSection
    {
        /// <summary>分片自己的状态结构（JsonUtility 要求 [Serializable] + 公开字段）。</summary>
        [Serializable]
        private sealed class State
        {
            public int Gold;
            public List<int> ItemIds = new List<int>();
        }

        public string Key => "demo.inventory";

        public int Gold { get; set; }

        public List<int> ItemIds { get; } = new List<int>();

        /// <summary>演示起点：金币 100，身上两件物品。</summary>
        public void Reset()
        {
            Gold = 100;
            ItemIds.Clear();
            ItemIds.Add(1001);
            ItemIds.Add(1003);
        }

        public string Describe() => $"金币 {Gold}，物品 [{string.Join("、", ItemIds)}]";

        public string Capture(ISaveSerializer serializer)
            => serializer.Serialize(new State { Gold = Gold, ItemIds = ItemIds });

        public bool Restore(string payload, ISaveSerializer serializer)
        {
            try
            {
                var state = serializer.Deserialize<State>(payload);
                Gold = state.Gold;
                ItemIds.Clear();
                ItemIds.AddRange(state.ItemIds);
                return true;
            }
            catch (Exception)
            {
                return false; // 读不了就保持当前（默认）状态并让存档系统记日志
            }
        }
    }
}
