using UnityEngine;

namespace Jam
{
    /// <summary>自定义事件（struct = 零 GC）。事件类型定义在哪都行，发布方与监听方互不认识。</summary>
    public struct ScoreGainedEvent
    {
        public int Amount;

        public ScoreGainedEvent(int amount) => Amount = amount;
    }

    /// <summary>
    /// Jam 起步脚本 —— 底座常用 API 的"一行示例"合集。
    ///
    /// 用法：新建场景 → 建个空物体 → 挂上本组件 → Play。Console 会打印就绪日志，
    /// 并演示 延迟回调 / 事件收发 / 音效 / 对象池。需要哪个功能就把对应几行抄到你自己的脚本里；
    /// 不需要的整段删掉 —— 本文件就是给你改的。
    ///
    /// 两个文件的分工：
    ///   · 本文件 = 可运行的范例（挂到物体上才有行为）
    ///   · G.cs   = 短入口，把所有 `using Template.*` 都收在里面 —— 所以本文件**只需要
    ///              `using UnityEngine;` 一行**，其余全靠 `G.xxx`（详见 G.cs 的注释）。
    ///
    /// 一条经验：**服务在 BeforeSceneLoad 就绪**（早于场景里任何 Awake），所以 Awake/Start 里
    /// 直接用 `G.Time` 之类都是安全的；服务缺席会立刻报错，别用 try/catch 掩盖它。
    /// </summary>
    public sealed class GameJamStarter : MonoBehaviour
    {
        [Header("Jam 里直接拖引用比走 Addressables 快")]
        public AudioClip SfxClip;
        public GameObject BulletPrefab;

        private bool _subscribed;

        private void Awake()
        {
            G.Info("Jam", "底座就绪（Play 中按 F1 打开调试面板看全部服务状态）");

            // ── 事件：订阅了什么，就要在 OnDestroy 退订什么 ──
            // EventBus 是静态的：不退订会在重进 Play 时留下幽灵回调
            G.Subscribe<ScoreGainedEvent>(OnScoreGained);
            _subscribed = true;

            // ── 定时器：不用协程；默认 Scaled 时钟（暂停 / 时间缩放时自动停）──
            G.Time.Delay(1f, () => G.Info("Jam", "1 秒后触发（游戏暂停时不会走）"));
            G.Time.Repeat(5f, () => G.Verbose("Jam", "每 5 秒触发一次（不需要就删掉这行）"));

            // ── 音频：一行出声 ──
            if (SfxClip != null)
            {
                G.Audio.PlaySound(SfxClip);                        // 2D（UI / 无方位）
                G.Audio.PlaySoundAt(SfxClip, transform.position);  // 3D 空间音（听得到方位）
            }

            // ── 广播事件：谁监听谁响应，互相不认识 ──
            G.Publish(new ScoreGainedEvent(10));

            // ── 给 F1 面板加一条作弊命令（可随时删）──
            G.Cheat("win", "win —— 直接过关（示例命令）", _ =>
            {
                G.Info("Jam", "作弊：过关！");
                return "已过关";
            });
        }

        private void Start()
        {
            // ── 对象池：建池 → 出池 → 到点自动回收（子弹 / 刷怪 / 特效都走它）──
            if (BulletPrefab != null)
            {
                G.Pools.CreatePool("bullet", BulletPrefab, capacity: 64, prewarm: 8);
                GameObject bullet = G.Pools.Spawn("bullet", transform.position, Quaternion.identity);
                G.Pools.DespawnAfter(bullet, 2f); // 2 秒（真实时间）后自动归池
            }
        }

        private void OnScoreGained(ScoreGainedEvent e)
            => G.Info("Jam", "得分 +{0}", e.Amount);

        private void OnDestroy()
        {
            if (_subscribed)
                G.Unsubscribe<ScoreGainedEvent>(OnScoreGained);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 其它常用 API 速查（复制到需要的地方；用不到的不用管）
        //
        // ① UI 面板：面板 = 挂 UiPanel 子类的预制体，开合全交 UIService
        //    （别自己 Instantiate，否则遮罩 / Esc 返回 / 开关面板音效都失效）
        //      UiPanel panel = G.Ui.Open(myPanelPrefab);   // 返回的是活实例
        //      G.Ui.Close(panel);
        //      if (!G.Ui.TryBack()) { /* 栈底：回主流程 / 退出 */ }
        //    （Esc / 手柄 B → TryBack 已由输入层自动接好，你不用写；UiPanel 类型要 using Template.UI）
        //
        // ② 存档：一个分片 = 你自己的状态结构；存档系统不认识你的数据
        //      G.Save.RegisterSection(new MySaveSection(state));   // 实现 ISaveSection（Capture/Restore）
        //      G.Save.Save(0, "第 1 关 · 3 分钟");
        //      if (!G.Save.Load(0, out string error)) G.Warn("Jam", "读档失败：{0}", error);
        //
        // ③ 配置表：改 _Project/Data/Configs/*.csv（存 UTF-8）保存即自动导入，然后
        //      ItemConfig sword = G.Config.GetTable<ItemTable>().Get(1001);   // 或直接 Configs.Item.Get(1001)
        //    ⚠ 运行期要有人把配置库交给服务：新建空物体挂 ConfigInstaller，拖入
        //      _Project/Data/Configs/Generated/ConfigDatabase.asset（演示关闭后没人再做这件事了）
        //
        // ④ 相机跟随 / 屏幕震动
        //      G.Cam.Follow(player.transform, CameraFollowOptions.Default3D);
        //      G.Cam.Shake(0.4f);                                  // 0~1 的创伤值
        //
        // ⑤ 特效：一行播放，播完自动回收
        //      G.Vfx.Play(hitEffectPrefab, pos);
        //
        // ⑥ 输入：游戏动作由你构建，交给 InputService 托管（进暂停自动屏蔽、恢复自动启用）
        //      var map = new InputActionMap("Player");            // using UnityEngine.InputSystem;
        //      var jump = map.AddAction("Jump", InputActionType.Button);
        //      jump.AddBinding("<Keyboard>/space");
        //      jump.performed += _ => DoJump();
        //      G.Input.AttachMap(map);
        //
        // ⑦ F1 面板加一节（内容随便写）
        //      G.PanelSection("我的系统", () => "血量 " + hp + " / 金币 " + gold);
        //
        // 更多用法：_Project/README.md 每个模块末尾都有「XX 快速上手」，直接抄（把
        // ServiceLocator.Get<XxxService>() 换成 G.Xxx 就行）。
        // ─────────────────────────────────────────────────────────────────────────
    }
}
