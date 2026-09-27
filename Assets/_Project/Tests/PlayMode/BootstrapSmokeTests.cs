using System.Collections;
using NUnit.Framework;
using Template.Core.App;
using Template.Core.Services;
using Template.Core.Timing;
using Template.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace Template.Tests
{
    /// <summary>
    /// 播放模式冒烟测试：验证"进 Play 就自举完成"这条主线（Bootstrap 在 BeforeSceneLoad 装配服务、
    /// 模块服务完成登记、容器加锁）。不依赖任何场景内容 —— 测试运行器会在临时场景（InitTestScene*）里跑，
    /// DemoRunner 检测到该场景会跳过演示时间线，所以这里不会受到演示干扰。
    /// </summary>
    public class BootstrapSmokeTests
    {
        [UnityTest]
        public IEnumerator Bootstrap_AssemblesCoreAndModuleServices()
        {
            yield return null; // 等一帧：BeforeSceneLoad 的自举在进入播放模式时已完成

            Assert.IsNotNull(ServiceLocator.Get<TimeService>(), "核心服务应已注册");
            Assert.IsNotNull(ServiceLocator.Get<GameStateService>(), "核心服务应已注册");
            Assert.IsNotNull(ServiceLocator.Get<SettingsService>(), "模块服务（登记—装配两段式）应已注册");
            Assert.IsTrue(ServiceLocator.IsLocked, "启动完成后容器应加锁（运行期禁止再注册）");
        }

        [UnityTest]
        public IEnumerator TimeService_Delay_FiresWithinTimeout()
        {
            var time = ServiceLocator.Get<TimeService>();
            bool fired = false;
            time.Delay(0.1f, () => fired = true);

            float waited = 0f;
            while (!fired && waited < 3f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsTrue(fired, "0.1 秒定时器应在 3 秒内触发（Bootstrap 每帧驱动 ITickable）");
        }
    }
}
