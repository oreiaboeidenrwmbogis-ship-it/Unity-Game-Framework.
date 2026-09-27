using System.Text.RegularExpressions;
using NUnit.Framework;
using Template.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace Template.Tests
{
    /// <summary>内存存储：让"持久化行为"可测而不碰真实文件（也演示 ISettingsStorage 的可替换性）。</summary>
    internal sealed class InMemorySettingsStorage : ISettingsStorage
    {
        public string Content;
        public int SaveCount;

        public string Load() => Content;

        public void Save(string content)
        {
            Content = content;
            SaveCount++;
        }
    }

    /// <summary>设置服务的契约：默认值、延迟写盘、跨实例读回、事件广播、损坏降级。</summary>
    public class SettingsServiceTests
    {
        private static readonly SettingsKey<float> Volume = new SettingsKey<float>("test.volume", 0.5f);
        private static readonly SettingsKey<bool> Flag = new SettingsKey<bool>("test.flag", false);

        private EventSubscriptions _subs;

        [SetUp]
        public void SetUp() => _subs = new EventSubscriptions();

        [TearDown]
        public void TearDown() => _subs.DisposeAll();

        [Test]
        public void Get_WithNoSavedValue_ReturnsKeyDefault()
        {
            var service = new SettingsService(new InMemorySettingsStorage());

            Assert.AreEqual(0.5f, service.Get(Volume));
            Assert.IsFalse(service.Get(Flag));
        }

        [Test]
        public void Set_MarksDirty_ButDoesNotWriteImmediately()
        {
            var storage = new InMemorySettingsStorage();
            var service = new SettingsService(storage);

            Assert.IsTrue(service.Set(Volume, 0.8f));
            Assert.AreEqual(0, storage.SaveCount, "Set 只标脏，写盘由 Tick 延迟合并（滑条拖动不该狂刷磁盘）");

            service.Flush();
            Assert.AreEqual(1, storage.SaveCount);
        }

        [Test]
        public void Set_WithUnchangedValue_ReturnsFalse_AndDoesNotBroadcast()
        {
            var service = new SettingsService(new InMemorySettingsStorage());
            int events = 0;
            _subs.Subscribe<SettingsChangedEvent<float>>(e => events++);

            Assert.IsTrue(service.Set(Volume, 0.9f));
            Assert.IsFalse(service.Set(Volume, 0.9f), "值没变不应重复写盘/广播");

            Assert.AreEqual(1, events, "只有真正变化的那一次才广播");
        }

        [Test]
        public void Set_PublishesTypedEvent_CarryingKeyAndValue()
        {
            var service = new SettingsService(new InMemorySettingsStorage());
            float received = -1f;
            SettingsKey<float> receivedKey = default;
            _subs.Subscribe<SettingsChangedEvent<float>>(e =>
            {
                received = e.Value;
                receivedKey = e.Key;
            });

            service.Set(Volume, 0.3f);

            Assert.AreEqual(0.3f, received, 0.0001f);
            Assert.IsTrue(receivedKey.Equals(Volume), "事件应携带键对象，监听者用强类型键比较（没有字符串）");
        }

        [Test]
        public void Flush_ThenNewService_ReadsValueBack()
        {
            var storage = new InMemorySettingsStorage();

            var writer = new SettingsService(storage);
            writer.Set(Volume, 0.25f);
            writer.Flush();

            var reader = new SettingsService(storage);
            Assert.AreEqual(0.25f, reader.Get(Volume), 0.0001f, "换一个实例应能从同一份存储读回（跨运行持久化）");
        }

        [Test]
        public void CorruptedFile_FallsBackToDefaultsInsteadOfThrowing()
        {
            var storage = new InMemorySettingsStorage { Content = "{ 这不是合法 JSON" };
            var service = new SettingsService(storage);

            // 降级时会记一条 Error 日志（预期行为）—— 不声明的话测试框架会判失败
            LogAssert.Expect(LogType.Error, new Regex("设置文件解析失败"));

            Assert.DoesNotThrow(() => service.Get(Volume));
            Assert.AreEqual(0.5f, service.Get(Volume), "解析失败应降级为默认值");
        }

        [Test]
        public void ValidJsonWithoutEntries_FallsBackToDefaults()
        {
            var storage = new InMemorySettingsStorage { Content = "{}" };
            var service = new SettingsService(storage);

            Assert.AreEqual(0.5f, service.Get(Volume), "结构合法但内容为空 → 用默认值");
        }

        [Test]
        public void EntryWithUnparsableValue_FallsBackToDefaultForThatKeyOnly()
        {
            var storage = new InMemorySettingsStorage
            {
                Content = "{\"Version\":1,\"Entries\":[" +
                    "{\"Key\":\"test.volume\",\"Value\":\"不是数字\",\"Type\":\"float\"}," +
                    "{\"Key\":\"test.flag\",\"Value\":\"true\",\"Type\":\"bool\"}]}",
            };
            var service = new SettingsService(storage);

            Assert.AreEqual(0.5f, service.Get(Volume), "坏值只影响该键，回退默认值");
            Assert.IsTrue(service.Get(Flag), "同一文件里的其它键应正常读回");
        }

        [Test]
        public void SettingsKey_EqualityIsById_NotByDefaultValue()
        {
            var a = new SettingsKey<float>("same.id", 1f);
            var b = new SettingsKey<float>("same.id", 2f);
            var c = new SettingsKey<float>("other.id", 1f);

            Assert.IsTrue(a.Equals(b), "同一 id 即同一项设置（默认值不同不影响读写）");
            Assert.IsFalse(a.Equals(c));
        }
    }
}
