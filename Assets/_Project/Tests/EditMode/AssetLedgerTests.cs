using NUnit.Framework;
using Template.Assets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Template.Tests
{
    /// <summary>
    /// 资产账本的记账语义（纯逻辑，不需要真实资源加载 ——— 这是把账本从 AssetService 拆出来的理由）。
    /// 真实加载/卸载由 Addressables 负责，那部分走 Play 验收。
    /// </summary>
    public class AssetLedgerTests
    {
        private const float Now = 12.5f;

        private AssetLedger _ledger;

        [SetUp]
        public void SetUp() => _ledger = new AssetLedger();

        private AssetRecord Add(string key, string label = null)
            => _ledger.AddOrAddRef(key, label, typeof(object), default(AsyncOperationHandle), Now);

        [Test]
        public void AddOrAddRef_SameKey_KeepsOneRecordAndCountsRefs()
        {
            AssetRecord first = Add("Enemy_Goblin");
            AssetRecord second = Add("Enemy_Goblin");

            Assert.AreSame(first, second, "同一地址只应有一条记录");
            Assert.AreEqual(2, second.RefCount);
            Assert.AreEqual(1, _ledger.Count);
        }

        [Test]
        public void AddRef_UnknownKey_ReturnsFalse()
        {
            Assert.IsFalse(_ledger.AddRef("nobody"));
        }

        [Test]
        public void Release_DecrementsUntilZero_ThenHandsBackRecord()
        {
            Add("Enemy_Goblin");
            Add("Enemy_Goblin");

            Assert.IsFalse(_ledger.Release("Enemy_Goblin", out AssetRecord first), "计数没归零就不该真释放");
            Assert.IsNull(first);
            Assert.AreEqual(1, _ledger.Count);

            Assert.IsTrue(_ledger.Release("Enemy_Goblin", out AssetRecord second), "计数归零 → 交出记录让调用方卸载");
            Assert.IsNotNull(second);
            Assert.AreEqual("Enemy_Goblin", second.Key);
            Assert.AreEqual(0, _ledger.Count);
        }

        [Test]
        public void Release_UnknownKey_ReturnsFalse()
        {
            Assert.IsFalse(_ledger.Release("nobody", out AssetRecord record));
            Assert.IsNull(record);
        }

        [Test]
        public void TakeLabelGroup_TakesOnlyThatLabel()
        {
            Add("a", "chapter_01");
            Add("b", "chapter_01");
            Add(AssetLedger.LabelKey("chapter_01"), "chapter_01"); // 组记录自身
            Add("c", "ui");

            var taken = _ledger.TakeLabelGroup("chapter_01");

            Assert.AreEqual(3, taken.Count, "组记录 + 组内资产都应被取出");
            Assert.AreEqual(1, _ledger.Count, "别的组不受影响");
            Assert.IsNotNull(_ledger.Find("c"));
            Assert.IsNull(_ledger.Find("a"));
        }

        [Test]
        public void TakeInstance_RemovesByInstanceId()
        {
            int id = 12345;
            Add(AssetLedger.InstanceKey(id));

            Assert.IsTrue(_ledger.TakeInstance(id, out AssetRecord record));
            Assert.AreEqual(AssetLedger.InstanceKey(id), record.Key);
            Assert.IsFalse(_ledger.TakeInstance(id, out _), "重复回收返回 false");
        }

        [Test]
        public void Clear_ReturnsEverythingAndEmptiesLedger()
        {
            Add("a");
            Add("b");

            var all = _ledger.Clear();

            Assert.AreEqual(2, all.Count);
            Assert.AreEqual(0, _ledger.Count);
        }

        [Test]
        public void Dump_ReportsCountAndAge()
        {
            Add("Enemy_Goblin");

            string dump = _ledger.Dump(Now + 3f);

            StringAssert.Contains("Enemy_Goblin", dump);
            StringAssert.Contains("×1", dump, "泄漏排查时最需要看到的就是引用计数");
            StringAssert.Contains("3.0s", dump);

            _ledger.Clear();
            Assert.AreEqual("（当前没有已加载资产）", _ledger.Dump(Now), "全部释放后应是干净状态");
        }
    }
}
