using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Template.Save;
using UnityEngine;
using UnityEngine.TestTools;

namespace Template.Tests
{
    /// <summary>测试用存档分片：可控地模拟"正常/拒绝恢复/恢复时抛异常"三种情况。</summary>
    internal sealed class FakeSection : ISaveSection
    {
        [Serializable]
        public sealed class State
        {
            public int Value;
            public string Label;
        }

        public string Key { get; }
        public int Value;
        public string Label;
        public bool RejectRestore;  // Restore 返回 false
        public bool ThrowOnRestore; // Restore 抛异常

        public int RestoreCount;

        public FakeSection(string key) => Key = key;

        public string Capture(ISaveSerializer serializer)
            => serializer.Serialize(new State { Value = Value, Label = Label });

        public bool Restore(string payload, ISaveSerializer serializer)
        {
            RestoreCount++;
            if (ThrowOnRestore)
                throw new InvalidOperationException("测试：分片恢复故意抛错");
            if (RejectRestore)
                return false;

            State state = serializer.Deserialize<State>(payload);
            Value = state.Value;
            Label = state.Label;
            return true;
        }
    }

    /// <summary>测试用迁移：往某个分片的 Label 追加后缀（模拟"字段改名/结构搬运"这类真实迁移）。</summary>
    internal sealed class LabelAppendingMigration : ISaveMigration
    {
        private readonly string _sectionKey;
        private readonly string _suffix;

        public int FromVersion { get; }

        public LabelAppendingMigration(int fromVersion, string sectionKey, string suffix)
        {
            FromVersion = fromVersion;
            _sectionKey = sectionKey;
            _suffix = suffix;
        }

        public void Apply(SaveData data, ISaveSerializer serializer)
        {
            SaveSectionEntry entry = data.FindSection(_sectionKey);
            if (entry == null)
                return;
            var state = serializer.Deserialize<FakeSection.State>(entry.Payload);
            state.Label += _suffix;
            data.SetSection(entry.Key, serializer.Serialize(state));
        }
    }

    /// <summary>测试用迁移：往摘要追加标记（用来验证迁移链的执行顺序）。</summary>
    internal sealed class MarkerMigration : ISaveMigration
    {
        private readonly string _mark;

        public int FromVersion { get; }

        public MarkerMigration(int fromVersion, string mark)
        {
            FromVersion = fromVersion;
            _mark = mark;
        }

        public void Apply(SaveData data, ISaveSerializer serializer)
            => data.Meta.Summary = (data.Meta.Summary ?? string.Empty) + _mark;
    }

    public class JsonSaveSerializerTests
    {
        [Test]
        public void SerializeDeserialize_RoundTrips()
        {
            var serializer = new JsonSaveSerializer();
            var state = new FakeSection.State { Value = 42, Label = "木剑" };

            var restored = serializer.Deserialize<FakeSection.State>(serializer.Serialize(state));

            Assert.AreEqual(42, restored.Value);
            Assert.AreEqual("木剑", restored.Label, "中文应原样往返（JSON 缩进输出便于人眼排查）");
        }
    }

    public class FileSaveStorageTests
    {
        private string _dir;
        private FileSaveStorage _storage;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "template-save-tests-" + Guid.NewGuid().ToString("N"));
            _storage = new FileSaveStorage(_dir);
        }

        [TearDown]
        public void TearDown() => CleanUpTempDir();

        /// <summary>清理临时目录；清不掉也只警告（不能让"临时目录残留"把用例判成失败）。</summary>
        private void CleanUpTempDir()
        {
            try
            {
                if (Directory.Exists(_dir))
                    Directory.Delete(_dir, true);
            }
            catch (Exception ex)
            {
                Template.Core.Logging.Log.Warn("Tests", "临时目录清理失败（不影响用例结论）：{0}", ex.Message);
            }
        }

        [Test]
        public void SaveThenLoad_RoundTrips()
        {
            _storage.Save(0, "内容 A");

            Assert.IsTrue(_storage.Exists(0));
            Assert.AreEqual("内容 A", _storage.Load(0));
            Assert.IsFalse(File.Exists(Path.Combine(_dir, "slot_0.json.tmp")), "写入后不应残留临时文件");
        }

        [Test]
        public void SecondSave_KeepsPreviousContentAsBackup()
        {
            _storage.Save(0, "第一次");
            _storage.Save(0, "第二次");

            Assert.AreEqual("第二次", _storage.Load(0));
            Assert.AreEqual("第一次", _storage.LoadBackup(0), "覆盖写入前应把旧档留成备份");
        }

        [Test]
        public void ListSlots_ReturnsExistingSlotsSorted()
        {
            _storage.Save(2, "b");
            _storage.Save(0, "a");
            _storage.Save(5, "c");

            Assert.AreEqual(new[] { 0, 2, 5 }, _storage.ListSlots());
        }

        [Test]
        public void Delete_RemovesMainAndBackup()
        {
            _storage.Save(1, "a");
            _storage.Save(1, "b");

            _storage.Delete(1);

            Assert.IsFalse(_storage.Exists(1));
            Assert.IsNull(_storage.LoadBackup(1));
            Assert.IsEmpty(_storage.ListSlots());
        }

        [Test]
        public void Load_MissingSlot_ReturnsNull()
        {
            Assert.IsNull(_storage.Load(9));
            Assert.IsNull(_storage.LoadBackup(9));
        }
    }

    public class SaveServiceTests
    {
        private string _dir;
        private FileSaveStorage _storage;
        private JsonSaveSerializer _serializer;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "template-save-tests-" + Guid.NewGuid().ToString("N"));
            _storage = new FileSaveStorage(_dir);
            _serializer = new JsonSaveSerializer();
        }

        [TearDown]
        public void TearDown() => CleanUpTempDir();

        private void CleanUpTempDir()
        {
            try
            {
                if (Directory.Exists(_dir))
                    Directory.Delete(_dir, true);
            }
            catch (Exception ex)
            {
                Template.Core.Logging.Log.Warn("Tests", "临时目录清理失败（不影响用例结论）：{0}", ex.Message);
            }
        }

        private SaveService NewService() => new SaveService(_storage, _serializer);

        [Test]
        public void SaveThenLoad_RestoresSectionState()
        {
            var service = NewService();
            var section = new FakeSection("fake.section") { Value = 7, Label = "甲" };
            service.RegisterSection(section);

            Assert.IsTrue(service.Save(0, "第 1 章"));

            section.Value = 999;   // 改脏内存
            section.Label = "脏";

            Assert.IsTrue(service.Load(0, out string error), error);
            Assert.AreEqual(7, section.Value);
            Assert.AreEqual("甲", section.Label);
        }

        [Test]
        public void SlotInfo_CarriesVersionSummaryAndTime()
        {
            var service = NewService();
            service.RegisterSection(new FakeSection("fake.section"));
            service.Save(3, "第 3 章 · 12 分钟");

            Assert.IsTrue(service.TryGetSlotInfo(3, out SaveSlotInfo info));

            Assert.AreEqual(3, info.Slot);
            Assert.AreEqual(SaveService.CurrentVersion, info.Version);
            Assert.AreEqual("第 3 章 · 12 分钟", info.Summary);
            Assert.Greater(info.SavedAtLocal, DateTime.MinValue);
        }

        [Test]
        public void Load_MissingSection_KeepsOtherSectionsWorking()
        {
            var service = NewService();
            var first = new FakeSection("a") { Value = 1 };
            service.RegisterSection(first);
            service.Save(0, null);

            // 换一个"新注册的分片"再读同一份档：老档里没有它 → 只记警告、不影响其它分片
            var service2 = NewService();
            var second = new FakeSection("a") { Value = 1 };
            var added = new FakeSection("b") { Value = 42 };
            service2.RegisterSection(second);
            service2.RegisterSection(added);

            Assert.IsTrue(service2.Load(0, out string error), error);
            Assert.AreEqual(1, second.Value, "老档里有的分片应正常恢复");
            Assert.AreEqual(42, added.Value, "老档里没有的分片保持默认值");
        }

        [Test]
        public void Load_SectionThatThrows_DoesNotBreakOtherSections()
        {
            var service = NewService();
            service.RegisterSection(new FakeSection("bad") { Value = 1 });
            service.RegisterSection(new FakeSection("good") { Value = 2 });
            service.Save(0, null);

            var target = new FakeSection("bad") { ThrowOnRestore = true };
            var good = new FakeSection("good");
            var service2 = NewService();
            service2.RegisterSection(target);
            service2.RegisterSection(good);

            LogAssert.Expect(LogType.Error, new Regex("恢复异常"));
            Assert.IsTrue(service2.Load(0, out string error), error);

            Assert.AreEqual(2, good.Value, "一个分片抛异常不应拖垮整个读档");
        }

        [Test]
        public void Load_SectionRejectingRestore_LoadStillSucceeds()
        {
            var service = NewService();
            service.RegisterSection(new FakeSection("s") { Value = 5 });
            service.Save(0, null);

            var section = new FakeSection("s") { RejectRestore = true };
            var service2 = NewService();
            service2.RegisterSection(section);

            Assert.IsTrue(service2.Load(0, out string error), error);
            Assert.AreEqual(0, section.Value, "分片拒绝恢复 → 保持自身默认值");
        }

        [Test]
        public void RegisterSection_DuplicateKey_IsRejected()
        {
            var service = NewService();
            service.RegisterSection(new FakeSection("dup"));

            LogAssert.Expect(LogType.Error, new Regex("分片键重复"));
            service.RegisterSection(new FakeSection("dup"));
        }

        [Test]
        public void Load_CorruptedMainFile_FallsBackToBackup()
        {
            var service = NewService();
            service.RegisterSection(new FakeSection("s") { Value = 8 });
            service.Save(0, null);
            service.Save(0, null); // 第二次保存 → 产生备份

            File.WriteAllText(Path.Combine(_dir, "slot_0.json"), "{ 这不是合法 JSON"); // 模拟主档损坏

            var section = new FakeSection("s");
            var service2 = NewService();
            service2.RegisterSection(section);

            Assert.IsTrue(service2.Load(0, out string error), "应回退到备份档而不是直接失败");
            Assert.AreEqual(8, section.Value);
            Assert.IsNotNull(error, "回退备份属于降级，必须把原因带回给业务（否则界面无法提示玩家）");
            StringAssert.Contains("回退", error);
        }

        [Test]
        public void Load_BothMainAndBackupCorrupted_Fails()
        {
            var service = NewService();
            service.RegisterSection(new FakeSection("s"));
            service.Save(0, null);
            service.Save(0, null);

            string main = Path.Combine(_dir, "slot_0.json");
            File.WriteAllText(main, "{ 坏的");
            File.WriteAllText(main + ".bak", "{ 也是坏的");

            var service2 = NewService();
            service2.RegisterSection(new FakeSection("s"));

            LogAssert.Expect(LogType.Error, new Regex("读取槽位 0 失败"));
            Assert.IsFalse(service2.Load(0, out string error));
            Assert.IsNotEmpty(error, "失败必须给出原因（业务据此提示玩家，而不是白屏）");
        }

        [Test]
        public void Load_OldVersion_RunsMigrationChain()
        {
            // 造一份 v0 老档（当前版本是 v1）
            var oldData = new SaveData { Version = 0, Meta = new SaveMeta { Summary = "老档" } };
            oldData.SetSection("s", _serializer.Serialize(new FakeSection.State { Value = 7, Label = "old" }));
            _storage.Save(0, _serializer.Serialize(oldData));

            var section = new FakeSection("s");
            var service = NewService();
            service.RegisterSection(section);
            service.RegisterMigration(new LabelAppendingMigration(0, "s", "-migrated"));

            Assert.IsTrue(service.Load(0, out string error), error);
            Assert.AreEqual(7, section.Value);
            Assert.AreEqual("old-migrated", section.Label, "迁移应能改写分片载荷（字段改名/结构搬运）");
        }

        [Test]
        public void Load_VersionNewerThanApp_Fails()
        {
            var future = new SaveData { Version = SaveService.CurrentVersion + 1 };
            _storage.Save(0, _serializer.Serialize(future));

            var service = NewService();

            LogAssert.Expect(LogType.Error, new Regex("高于程序支持"));
            Assert.IsFalse(service.Load(0, out string error));
            StringAssert.Contains("高于程序支持", error);
        }

        [Test]
        public void Delete_RemovesSlot()
        {
            var service = NewService();
            service.RegisterSection(new FakeSection("s"));
            service.Save(1, null);

            Assert.IsTrue(service.Delete(1));
            Assert.IsFalse(service.SlotExists(1));
            Assert.IsFalse(service.Delete(1), "重复删除返回 false");
        }
    }

    public class SaveMigratorTests
    {
        private static SaveData NewData(int version, string summary = "")
            => new SaveData { Version = version, Meta = new SaveMeta { Summary = summary } };

        [Test]
        public void TryMigrate_RunsStepsInOrder()
        {
            var migrator = new SaveMigrator(3);
            migrator.Register(new MarkerMigration(1, "A"));
            migrator.Register(new MarkerMigration(2, "B"));

            SaveData data = NewData(1);
            Assert.IsTrue(migrator.TryMigrate(data, new JsonSaveSerializer(), out string error), error);

            Assert.AreEqual(3, data.Version);
            Assert.AreEqual("AB", data.Meta.Summary, "迁移必须按 v1→v2→v3 顺序逐级执行");
        }

        [Test]
        public void TryMigrate_AlreadyCurrent_IsNoOp()
        {
            var migrator = new SaveMigrator(1);
            SaveData data = NewData(1, "不动");

            Assert.IsTrue(migrator.TryMigrate(data, new JsonSaveSerializer(), out _));
            Assert.AreEqual("不动", data.Meta.Summary);
        }

        [Test]
        public void TryMigrate_MissingStep_Fails()
        {
            var migrator = new SaveMigrator(3);
            migrator.Register(new MarkerMigration(2, "B")); // 缺 v1→v2

            SaveData data = NewData(1);

            LogAssert.Expect(LogType.Error, new Regex("缺少 v1 → v2 的迁移"));
            Assert.IsFalse(migrator.TryMigrate(data, new JsonSaveSerializer(), out string error));
            StringAssert.Contains("缺少", error);
        }

        [Test]
        public void Register_DuplicateFromVersion_IsRejected()
        {
            var migrator = new SaveMigrator(2);
            Assert.IsTrue(migrator.Register(new MarkerMigration(1, "A")));

            LogAssert.Expect(LogType.Error, new Regex("迁移重复注册"));
            Assert.IsFalse(migrator.Register(new MarkerMigration(1, "B")));
            Assert.AreEqual(1, migrator.Count);
        }
    }
}
