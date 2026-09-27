# 常用 API 示例

> **这份文档就是"抄"用的**：按任务查，复制代码块，改名字即可。
> 每个例子都是**编译验证过的**（模板版本 2022.3 / C# 9）。
>
> 配套：[快速开始.md](快速开始.md)（怎么跑起来、目录与架构、底座代码读哪些、怎么开新游戏）· [_Project/README.md](../Assets/_Project/README.md)（模块细节与各阶段验收）

## 30 秒约定

```csharp
using UnityEngine;                    // 你自己的脚本通常只需要这一行

namespace Jam                         // 与 Assets/Scripts/Game/G.cs 同命名空间 → G 直接可见
{
    public sealed class MyThing : MonoBehaviour
    {
        private void Start()
        {
            G.Info("MyThing", "就绪");        // 日志
            G.Audio.PlaySound(clip);          // 服务：G.Ui / G.Audio / G.Time / G.Pools / G.Save / G.Config ...
        }
    }
}
```

- `G` 是**服务短入口**（`Assets/Scripts/Game/G.cs`），把所有 `using Template.*` 收在里面；也可以不用它，直接 `ServiceLocator.Get<AudioService>()`。
- **服务在 `BeforeSceneLoad` 就绪**（早于场景里任何 Awake），所以 `Awake/Start` 里直接用都安全；找不到会立刻报错并抛出，别 try/catch 掩盖。
- 具体**类型**（`UiPanel`、`ItemConfig`、`SaveSlotInfo`…）仍需要各自命名空间的 `using`。
- 目录：`G.Time`=时间 · `G.Pools`=对象池 · `G.Ui`=UI · `G.Audio`=音频 · `G.Settings`=设置 · `G.Config`=配置表 · `G.Save`=存档 · `G.Assets`=资源 · `G.Input`=输入 · `G.Cam`=相机 · `G.Vfx`=特效 · `G.State`=游戏阶段 · `G.Scene`=场景流转

**目录**：[日志](#1-日志) · [服务](#2-服务与启动) · [时间/定时器](#3-时间与定时器) · [事件](#4-事件总线) · [对象池](#5-对象池) · [UI](#6-ui-面板) · [输入](#7-输入) · [音频](#8-音频) · [设置](#9-设置) · [配置表](#10-配置表) · [存档](#11-存档) · [资源](#12-资源addressables) · [相机](#13-相机) · [特效](#14-特效) · [状态机](#15-状态机fsm) · [游戏阶段/场景](#16-游戏阶段与场景流转) · [调试面板](#17-调试面板) · [扩展方法](#18-扩展方法) · [坑](#19-常见坑速查)

---

## 1. 日志

```csharp
using Template.Core.Logging;

Log.Info("Battle", "击杀 {0}，掉落 [{1}]", enemyName, itemId);   // 格式化 + 标签
Log.Warn("Save", "写入失败，已降级");
Log.Error("Config", "缺少 id=1001 的配置行");
Log.Error("Net", exception);                                     // 异常重载：保留完整堆栈
Log.Verbose("Pool", "池状态 {0}", poolId, this);                  // 高频调试；context 传 this → 双击在 Hierarchy 高亮

Log.MinLevel = LogLevel.Info;        // 屏蔽 Verbose（对引擎/第三方 Debug 输出同样生效）
Log.IncludeCallSite = false;         // 关掉正文里的 (文件.cs:行号) 前缀（高频日志省开销）
```

- **双击 Console 里的日志会跳到你的调用点**；正文自带 `(文件.cs:行号)`，日志被复制出 Console 也能定位。
- 铁律：业务代码不写 `Debug.Log`（走门面才能被 `MinLevel` 统一控制、才有跳转）。

## 2. 服务与启动

**写一个自己的服务**（全局唯一、跨场景、无位置概念的东西：分数、进度、背包数据）：

```csharp
using Template.Core.App;
using Template.Core.Services;

namespace Jam
{
    public sealed class ScoreService : IGameService      // 需要每帧 + ITickable
    {
        public int Score { get; private set; }
        public void Add(int n) => Score += n;

        public void Init() { }                            // 启动一次；此时可以 Get 任何服务
        public void Dispose() { }                         // 拆卸：退订、清引用，必须有序且必然终止
    }

    internal static class ScoreServiceBootstrap          // 登记—装配两段式
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register() => Bootstrap.AddModuleService(() => new ScoreService());
    }
}
```

**取服务**（场景脚本里，`Start` 或之后）：

```csharp
ScoreService score = ServiceLocator.Get<ScoreService>();          // 必须有：找不到会报错并抛异常
if (ServiceLocator.TryGet(out VfxService vfx)) { /* 可选依赖：缺席不炸 */ }
ScoreService s = G.Get<ScoreService>();                            // 等价写法（G 是短入口）
```

## 3. 时间与定时器

```csharp
int n = 0;
G.Time.Delay(2f, () => G.Info("A", "两秒后触发"));                        // Scaled 时钟：暂停/缩放时停走
G.Time.Delay(5f, () => G.Info("B", "真实时间 5 秒"), TimerClock.Unscaled);  // 加载超时、UI 动效用它

TimerHandle loop = G.Time.Repeat(1f, () => n++);                          // 周期；返回句柄
loop.Cancel();                                                            // 取消（幂等）

G.Time.Freeze();            // 暂停：timeScale=0（记住原值）
G.Time.Unfreeze();          // 恢复
G.Time.SetTimeScale(0.5f);  // 子弹时间（拒绝 0：暂停请用 Freeze / 状态机）
bool frozen = G.Time.IsFrozen;
float scale = G.Time.TimeScale;
```

- 定时器**不用协程**，由 Bootstrap 每帧手动驱动 —— 天然支持暂停与缩放。
- 游戏暂停走 `G.State.RequestTransition(GamePhase.Paused)`（它会调 `Freeze`），别直接改 `Time.timeScale`。

## 4. 事件总线

**定义事件**（struct，零 GC）：

```csharp
public struct EnemyKilledEvent { public string EnemyId; public int Gold; }
```

**发布 / 订阅 / 退订**：

```csharp
using Template.Core.Eventing;

EventBus<EnemyKilledEvent>.Publish(new EnemyKilledEvent { EnemyId = "slime", Gold = 5 });   // 发布

EventBus<EnemyKilledEvent>.Subscribe(OnEnemyKilled);      // 订阅（同一 handler 重复订阅会被拒绝）
EventBus<EnemyKilledEvent>.Unsubscribe(OnEnemyKilled);    // 退订 —— 订阅了就必须退订！

private void OnEnemyKilled(EnemyKilledEvent e) => G.Info("Quest", "击杀 {0}", e.EnemyId);
```

**用令牌自动退订**（推荐 MonoBehaviour 里这么写）：

```csharp
using Template.Core.Eventing;

private EventBus<EnemyKilledEvent>.Subscription _sub;    // 注意：类型是嵌套的 EventBus<T>.Subscription

private void OnEnable()  => _sub = EventBus<EnemyKilledEvent>.Subscribe(OnEnemyKilled);
private void OnDisable() => _sub.Dispose();               // 或 G.Subscribe / G.Unsubscribe

// 作用域写法：using (G.Subscribe<MyEvent>(OnMyEvent)) { ... }
```

- **红线：对象销毁必须退订**（EventBus 是静态的，不退订会留下幽灵回调）。
- 广播中退订是安全的；本次广播新增的监听者不接收当前这条事件。
- 单个监听者抛异常只记日志，不会瘫痪总线。

## 5. 对象池

**GameObject 池**（子弹 / 刷怪 / 可复用 UI 节点）：

```csharp
G.Pools.CreatePool("bullet", bulletPrefab, capacity: 64, prewarm: 8);   // 建池（幂等，可重复调用）

GameObject go = G.Pools.Spawn("bullet", muzzle.position, muzzle.rotation);
G.Pools.Despawn(go);                       // 手动回收
G.Pools.DespawnAfter(go, 2f);              // 2 秒（真实时间）后自动回收
G.Pools.DespawnAfter(go, 2f, TimerClock.Scaled);   // 暂停时不回收

G.Pools.ClearPool("bullet");               // 释放空闲实例并从注册表移除（切场景用）
G.Info("Pool", G.Pools.Dump());            // 排查：各池 总量/空闲/活跃
```

**池对象自己的生命周期回调**（挂在预制体根节点）：

```csharp
using Template.Core.Pooling;

public sealed class Bullet : MonoBehaviour, IPoolable
{
    public void OnSpawn()   { /* 出池：重置速度、播放音效 */ }
    public void OnDespawn() { /* 归池：清理状态，保证下次出池是干净的 */ }
}
```

**纯 C# 类池**（高频临时对象，避免 GC）：

```csharp
using Template.Core.Pooling;

var listPool = new ObjectPool<List<int>>(() => new List<int>(16), onReturn: l => l.Clear());
List<int> tmp = listPool.Rent();       // 取出（无空闲则工厂创建）
listPool.Return(tmp);                  // 归还（超容量直接丢弃）
```

## 6. UI 面板

**1）写一个面板类**（面板 = 预制体，根节点挂这个组件）：

```csharp
using Template.UI;

public sealed class InventoryPanel : UiPanel                       // 默认 Screen 层（全屏页，互斥压栈）
{
    protected override void OnOpened() { /* 初始化内容、订阅事件 */ }
    protected override void OnClosed() { /* 释放订阅、回写结果 */ }
}

public sealed class ConfirmPopup : UiPanel
{
    public override UILayer Layer => UILayer.Popup;                // 弹窗：自动遮罩
}

public sealed class ToastView : UiPanel
{
    public override UILayer Layer => UILayer.Overlay;              // 悬浮：不参与返回
}
```

**2）开 / 关 / 返回**：

```csharp
UiPanel prefab = go.GetComponent<UiPanel>();        // 或 LoadAsync<GameObject>(addr).GetComponent<UiPanel>()
UiPanel instance = G.Ui.Open(prefab);               // ★ Open 会克隆，返回值才是场上实例

G.Ui.Close(instance);                               // ★ 关的时候传【实例】，不是 prefab
if (G.Ui.TryBack()) { /* 已被框架处理（关弹窗/关页面） */ }
else { /* 栈底：回主流程或退出游戏 */ }

G.Ui.CloseAll();                                    // 收尾（退出玩法/切场景）
```

**3）查询与联动**：

```csharp
bool open = G.Ui.IsPanelOpen("InventoryPanel");     // 按 PanelName 查询
UiPanel top = G.Ui.TopScreen;                        // 栈顶页面（可能为 null）
int screens = G.Ui.ScreenCount;  int popups = G.Ui.PopupCount;   bool mask = G.Ui.IsAnyPopupOpen;
```

**4）别人开面板时做点什么**（音频模块就是这么接界面音的）：

```csharp
using Template.Core.Eventing;
using Template.UI;

EventBus<UIPanelOpenedEvent>.Subscribe(e => G.Info("UI", "打开 {0}（层 {1}）", e.PanelName, e.Layer));
EventBus<UIPanelClosedEvent>.Subscribe(e => { /* ... */ });
```

- **Esc / 手柄 B 已经接到 `TryBack()`**，你不用自己写返回逻辑。
- 别自己 `Instantiate`/`Destroy` 面板，否则遮罩、返回栈、开关面板音效全部失效。

## 7. 输入

**游戏动作由你构建，交给输入服务托管**（进暂停自动禁用、恢复自动启用）：

```csharp
using Template.Input;
using UnityEngine.InputSystem;

var map = new InputActionMap("Player");

InputAction move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
move.AddCompositeBinding("2DVector")
    .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
    .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");

InputAction jump = map.AddAction("Jump", InputActionType.Button);
jump.AddBinding("<Keyboard>/space");
jump.AddBinding("<Gamepad>/buttonSouth");
jump.performed += _ => Jump();

G.Input.AttachMap(map);        // 进玩法时附加（附加即启用）
G.Input.DetachMap(map);        // 出玩法时移除并禁用（映射由你持有，可复用）
```

**取消键 / 设备方案 / 逐帧读取**：

```csharp
G.Input.CancelPressed += OnCancel;                  // Esc / 手柄 B（纯 C# 事件，UI 层也用它）
G.Info("Input", "当前设备：{0}", G.Input.CurrentScheme);   // KeyboardMouse / Gamepad / Touch

// 变化时通知（UI 切按键提示图标）
EventBus<InputSchemeChangedEvent>.Subscribe(e => G.Info("Input", "设备切成 {0}", e.Scheme));

// 逐帧轮询（调试/开发期最省事；正式玩法建议用上面的 Action）
if (Keyboard.current.f5Key.wasPressedThisFrame) QuickSave();
if (Mouse.current.rightButton.wasPressedThisFrame) Aim();
```

- 内置 UI 映射（Navigate/Submit/Cancel/Point/Click/ScrollWheel…）常驻启用，EventSystem 由输入层自动创建，**场景里不要再摆一个**。

## 8. 音频

```csharp
using Template.Audio;

G.Audio.PlayBgm(bgmClip, fadeSeconds: 1.5f);              // 交叉淡变换曲（无静音缝）
G.Audio.StopBgm(0.8f);
bool playing = G.Audio.IsBgmPlaying;

G.Audio.PlaySound(clickClip);                             // 2D（UI / 无方位）
AudioSource src = G.Audio.PlaySound(shotClip, volume: 0.8f, pitch: 1.1f);

G.Audio.PlaySoundAt(hitClip, transform.position);         // 3D 空间音
G.Audio.PlaySoundAt(engineClip, transform.position, volume: 1f, pitch: 1f, follow: transform);  // 跟随移动

G.Audio.PlayVoice(lineClip);                              // 语音（新句打断旧句）
G.Audio.StopSound(src);                                   // 停单个 2D 音源

G.Audio.SetVolume(AudioBus.Sfx, 0.6f);                    // 音量滑条（自动写设置并持久化）
float v = G.Audio.GetVolume(AudioBus.Master);
G.Audio.StopAll();                                        // 切场景/退出兜底
```

- 四通道：`Master / Bgm / Sfx / Voice`（通道增益 = Master × 通道）；淡变按**真实时间**推进，暂停中也生效。

## 9. 设置

**定义设置项**（谁用谁定义；只支持 float/int/bool/string）：

```csharp
using Template.Settings;

public static class PlayerSettings
{
    public static readonly SettingsKey<float> MouseSensitivity = new SettingsKey<float>("player.mouseSensitivity", 1f);
    public static readonly SettingsKey<bool>  InvertY          = new SettingsKey<bool>("player.invertY", false);
}
```

**读 / 写 / 订阅**：

```csharp
using Template.Core.Eventing;
using Template.Settings;

float s = G.Settings.Get(PlayerSettings.MouseSensitivity);      // 首次访问自动加载设置文件
G.Settings.Set(PlayerSettings.MouseSensitivity, 1.6f);          // 即改即生效 + 延迟合并落盘 + 广播
G.Settings.Reset(PlayerSettings.InvertY);                       // 恢复默认
G.Settings.Flush();                                             // 立即写盘（一般不用手动调）

EventBus<SettingsChangedEvent<float>>.Subscribe(e =>
{
    if (e.Key.Equals(PlayerSettings.MouseSensitivity)) ApplySensitivity(e.Value);   // 强类型键比较
});
```

- 消费方要**"Init 拉初值 + 订阅后续变更"两件都做**（只订阅会漏初值）。
- 文件：`persistentDataPath/settings.json`（缩进 JSON，可直接看）。

## 10. 配置表

**读表**（改 CSV 不改代码；CSV 放 `Assets/_Project/Data/Configs/`，必须 UTF-8）：

```csharp
using Template.Config.Generated;

ItemConfig sword = Configs.Item.Get(1001);          // 缺项 → 安静返回 null
ItemConfig must  = Configs.Item.Require(1001);      // 缺项 → 记 Error 日志（"必须有"的场合用它）
if (Configs.Item.TryGet(1002, out ItemConfig i2)) { /* ... */ }

int count = Configs.Item.Count;
bool has = Configs.Item.Contains(1003);
foreach (ItemConfig item in Configs.Item.All) G.Info("Shop", "{0} 售价 {1}", item.Name, item.Price);

int price = sword.Price;        // 强类型属性：没有 id 字符串、没有魔法下标
string name = sword.Name;
```

**新增一张表**（不用写任何 C#）：新建 `Data/Configs/Skill.csv`（第 1 行字段名、第 2 行类型、第 3 行起数据）→ 保存（自动导入）→ 用 `Configs.Skill.Get(id)`。带外键的列：类型写目标表的**行类型名**（如 `ItemConfig`），值写目标行 id，导入时会校验引用存在。

**运行期必须有人装配配置库**（否则所有 `Get` 返回 null）：

```csharp
// 做法 A（推荐）：场景里建空物体 → 挂 ConfigInstaller → 把 ConfigDatabase.asset 拖到 Database 字段
// 做法 B：在自己的脚本里
public ConfigDatabase Database;                        // Inspector 拖 Data/Configs/Generated/ConfigDatabase.asset
private void Start() => G.Config.SetDatabase(Database);
```

## 11. 存档

**1）写一个"分片"**（谁的数据谁自己存；存档系统不认识你的数据）：

```csharp
using System;
using System.Collections.Generic;
using Template.Save;

[Serializable]
public sealed class PlayerState                        // 你的数据：[Serializable] + 公开字段
{
    public int Level = 1;
    public int Gold = 100;
    public List<string> Flags = new List<string>();
}

public sealed class PlayerSection : ISaveSection
{
    private readonly PlayerState _state;
    public PlayerSection(PlayerState state) => _state = state;

    public string Key => "player.state";                                    // 唯一键，"模块.用途"
    public string Capture(ISaveSerializer s) => s.Serialize(_state);
    public bool Restore(string payload, ISaveSerializer s)
    {
        try
        {
            PlayerState loaded = s.Deserialize<PlayerState>(payload);
            _state.Level = loaded.Level;
            _state.Gold  = loaded.Gold;
            _state.Flags = loaded.Flags ?? new List<string>();               // 老档缺字段会是 null
            return true;
        }
        catch (Exception) { return false; }                                  // 读不了 → 保持默认值
    }
}
```

**2）注册 + 存 / 读 / 管槽位**：

```csharp
using Template.Save;

G.Save.RegisterSection(new PlayerSection(_state));      // ★ 必须在任何 Load 之前（Boot/场景早期）

G.Save.Save(0, "第 1 关 · 金币 100");                     // 摘要会显示在存档列表里
if (!G.Save.Load(0, out string error))
    G.Warn("Save", "读档失败：{0}（按新档继续）", error);   // 没有档 / 主档与备份都坏
else if (error != null)
    G.Warn("Save", "读档降级：{0}", error);                // ★ 读到了档，但主档坏了、用了 .bak

foreach (int slot in G.Save.ListSlots())                 // 存档列表界面
    if (G.Save.TryGetSlotInfo(slot, out SaveSlotInfo info))
        G.Info("Save", "{0}", info);                     // 槽位 0：v1 · 第 1 关 · 2026-09-27 10:30

bool exists = G.Save.SlotExists(2);
G.Save.Delete(2);                                        // 删主档 + 备份
G.Info("Save", "{0}", G.Save.StorageDescription);         // 存档目录（persistentDataPath/saves）

// 只读不恢复（想看 Meta / 自查写盘是否真的成功）
if (G.Save.TryReadData(0, out SaveData data, out string err))
    G.Info("Save", "v{0}，{1} 个分片，摘要「{2}」", data.Version, data.Sections.Count, data.Meta.Summary);
```

**3）改存档结构时加迁移**（只加字段不用管）：

```csharp
using Template.Save;

private sealed class V1ToV2 : ISaveMigration
{
    public int FromVersion => 1;                          // 把 v1 改造成 v2
    public void Apply(SaveData data, ISaveSerializer s)
    {
        SaveSectionEntry entry = data.FindSection("player.state");
        if (entry == null) return;
        var old = s.Deserialize<PlayerStateV1>(entry.Payload);
        data.SetSection(entry.Key, s.Serialize(new PlayerState { Level = old.Level, Gold = old.Coin }));
    }
}
// 再把基座里的 SaveService.CurrentVersion 改成 2，然后： G.Save.RegisterMigration(new V1ToV2());
```

- ⚠ `Save()` 目前**恒返回 true**（写盘失败只记 Error）——要确认落盘就 `TryGetSlotInfo(slot, out _)` 自查。
- 文件：`persistentDataPath/saves/slot_N.json` + `.bak`（每次覆盖前留旧档，这就是"损坏降级"的来源）。

## 12. 资源（Addressables）

```csharp
using Template.Assets;
using UnityEngine;

// 单资产（引用计数 +1；每次加载都要配一次 Release）
Sprite icon = await G.Assets.LoadAsync<Sprite>("ui_icon_sword");
if (icon == null) { G.Error("Assets", "图标加载失败，走降级"); return; }
G.Assets.Release("ui_icon_sword");

// 预制体：★ 必须按 GameObject 加载，再取组件（带类型接口只认资产主类型，prefab 就是 GameObject）
GameObject go = await G.Assets.LoadAsync<GameObject>("Enemy_Goblin");
if (go != null)
{
    Enemy enemy = go.GetComponent<Enemy>();
    Instantiate(enemy, pos, Quaternion.identity);
}

// 预制体实例（用完回收，或交给对象池）
GameObject inst = await G.Assets.InstantiateAsync("Enemy_Goblin", parent: transform);
G.Assets.ReleaseInstance(inst);

// 整组（按 Label 打包的关卡/ui/audio 资源）：切关卡整组装、整组卸
IList<Sprite> icons = await G.Assets.LoadLabelAsync<Sprite>("ui_mainmenu");
G.Assets.ReleaseLabel("ui_mainmenu");

G.Info("Assets", G.Assets.Dump());     // 当前账本（泄漏排查第一现场）
int n = G.Assets.LoadedCount;
G.Assets.ReleaseAll();                 // 兜底释放
```

- 铁律：禁用 `Resources.Load`，资源统一走这里；地址在 **Window → Asset Management → Addressables → Groups** 里维护。
- 单资产地址要**先标成 Addressable**（Inspector 勾 Addressable + 填地址），否则加载返回 null。

> 需要 `async` 方法：`private async void Start()` 或 `private async UniTaskVoid Start()`（`using Cysharp.Threading.Tasks;`）。

## 13. 相机

```csharp
using Template.CameraSystem;

CameraHandle follow = G.Cam.Follow(player.transform, CameraFollowOptions.Default3D);   // 3D 第三人称（带阻尼）
G.Cam.Stop(follow);                                                                     // 停止跟随

CameraFollowOptions opt2D = CameraFollowOptions.Default2D;      // 2D：正交 + 死区 + 屏幕位置
opt2D.DeadZone = new Vector2(2f, 1f);
G.Cam.Follow(player.transform, opt2D);

CameraFollowOptions bossView = CameraFollowOptions.Default3D;   // 多机位：优先级大的接管
bossView.Priority = 100;
G.Cam.Follow(boss.transform, bossView);

G.Cam.Shake(0.25f);            // 屏幕震动：0~1 的创伤值（累加 + 按秒衰减 + 平方映射）
G.Cam.Shake(0.8f);             // 爆炸：明显晃动
G.Cam.ClearTrauma();
float trauma = G.Cam.Trauma;   int cams = G.Cam.CameraCount;
G.Cam.StopAll();               // 切场景兜底
```

- 主相机需带 `MainCamera` 标签；Cinemachine 相关对象由服务在运行期自动创建，不用手工摆虚拟相机。

## 14. 特效

```csharp
using Template.Vfx;

G.Vfx.Play(hitPrefab, transform.position);                       // 已持有引用：一行播放，播完自动回收
G.Vfx.Play("Explosion", pos, Quaternion.identity);               // 按 Addressables 地址播（首次自动加载建池）

VfxHandle handle = await G.Vfx.PlayAsync("Explosion", pos);      // 需要单独停时拿句柄
if (handle.IsValid) G.Vfx.Stop(handle.Instance);

await G.Vfx.PrewarmAsync("Hit_Spark", 10);                       // 预热：进战斗前备好 10 个instance
G.Vfx.StopAll();                                                 // 切场景/退出兜底
int active = G.Vfx.ActiveCount;   G.Info("Vfx", G.Vfx.Dump());
```

## 15. 状态机（FSM）

玩家状态 / 敌人 AI / 复杂面板状态通用（表驱动，每帧至多一次转换）：

```csharp
using Template.Core.Fsm;

public sealed class IdleState : StateBase
{
    public override void OnEnter() { /* 入场 */ }
    public override void OnExit()  { /* 出场清理 */ }
    public override void Tick()    { /* 每帧（仅当前状态被驱动） */ }
}

private readonly StateMachine _fsm = new StateMachine();
private readonly IState _idle = new IdleState();
private readonly IState _run  = new RunState();

private void Start()
{
    _fsm.SetInitialState(_idle);
    _fsm.AddTransition(new Transition(_run, () => moveInput.sqrMagnitude > 0.01f));        // 任意状态 → Run
    _fsm.AddTransition(new Transition(_idle, () => moveInput.sqrMagnitude < 0.01f, from: _run)); // 仅从 Run → Idle
    _fsm.Start();
}

private void Update() => _fsm.Tick();

// 外部驱动切换（如受击打断）：
_fsm.ChangeState(_run);
bool inRun = _fsm.IsInState<RunState>();
```

- 分层：把"内含子状态机的状态"作为一个普通 `IState` 组合进去（父状态 `OnEnter` 启动子机、`Tick` 转发）。
- `Shutdown()` 会清空转换表；`Changed` 是普通 C# 事件（订阅方不得抛异常）。

## 16. 游戏阶段与场景流转

```csharp
using Template.Core.App;
using Template.Core.Eventing;

G.State.RequestTransition(GamePhase.MainMenu);      // Boot→Splash→MainMenu→Loading→Gameplay↔Paused→GameOver
bool inGame = G.State.IsInGame;                     // Gameplay 或 Paused
GamePhase now = G.State.Current;

EventBus<GamePhaseChangedEvent>.Subscribe(e =>       // 各系统按阶段响应（音频压低、输入屏蔽…）
    G.Info("Phase", "{0} → {1}", e.Previous, e.Current));

// 切场景：先请求 Loading，加载完再进目标阶段
var ctx = new SceneContext().Set("slot", 0).Set("spawn", "door_a");   // 场景参数
G.Scene.LoadScene("Level_02", ctx);
float progress = G.Scene.LoadProgress;              // 0~1，驱动进度条
bool loading = G.Scene.IsLoading;

EventBus<SceneLoadedEvent>.Subscribe(e =>           // 场景加载完成：用 Context 初始化场景
{
    int slot = e.Context.Get<int>("slot");
    string spawn = e.Context.Get<string>("spawn");
    G.State.RequestTransition(GamePhase.Gameplay);
});
```

- 场景必须在 **Build Settings** 里（否则 `LoadScene` 报错拒绝）；`Paused` 会冻结缩放时钟、自动屏蔽 Gameplay 映射。

## 17. 调试面板

**加一节显示** / **加一条作弊命令**（Play 中按 **F1** 打开）：

```csharp
// 加一节（内容是什么完全由你决定）
G.PanelSection("我的系统", () => $"分数 {score}，敌人 {enemyCount} 个，状态 {state}");

// 加一条命令（F1 面板输入框里敲：give 100）
G.Cheat("give", "give <数量> —— 加金币", args =>
{
    int n = args.Length > 0 ? int.Parse(args[0]) : 1;
    AddGold(n);
    return "已加 " + n;                             // 返回值显示在面板输出区
});

// 只借用它的显示能力（把业务信息写进面板）
if (ServiceLocator.TryGet(out DiagnosticsService diag))
{
    diag.Console.Write("任务进度：3 / 10");
    diag.Open();                                    // 也可以什么都不做 —— 玩家按 F1 就开
    float fps = diag.Sampler.Fps;                   // 帧率采样器公开：可做自动降画质
    diag.SetTimeScale(0.25f);                       // 慢动作（走 TimeService）
    diag.TogglePause();                             // 暂停（走 GameStateService）
}
```

- `G.PanelSection` / `G.Cheat` 把 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 守卫藏在内部：**调用点不用写 `#if`**，正式包也不会因为"类型不存在"而构建失败。
- ⚠ 但**直接写 `DiagnosticsService` 类型**（下面那个 `TryGet(out DiagnosticsService diag)` 块）就没这层保护：它只在编辑器/开发构建里存在，必须自己包 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`，否则正式包构建报"类型不存在"。能用 `G.PanelSection` / `G.Cheat` 就用它们。

## 18. 扩展方法

```csharp
using Template.Core.Extensions;

transform.ResetLocal();                                  // 位置/旋转/缩放归零
transform.SetPositionX(3f);                              // 单轴改写（SetPositionY/Z、SetLocalPositionX/Y/Z 同理）
transform.DestroyChildren();                             // 销毁全部子物体（运行时安全）

Vector3 p = transform.position.FlattenY();               // 投影到 XZ 平面（地面移动/看向目标）
Vector3 q = p.WithY(1.5f);                               // 单轴替换（WithX/WithZ 同理）

if (myList.IsNullOrEmpty()) return;
myList.AddUnique(item);                                  // 不存在才添加
myList.RemoveAtSwap(index);                              // O(1) 无序移除
var any = myList.RandomOrDefault();                      // 随机取（空表返回 default + 告警）

var rb = transform.GetOrAddComponent<Rigidbody>();       // GetComponent ?? AddComponent
//   注意：它定义在 Component 上（不是 GameObject），所以写 transform/this 而不是 gameObject
```

## 19. 常见坑速查

| 现象 | 原因 / 怎么办 |
|---|---|
| `Configs.X.Get(id)` 返回 null | 表没导入（看 Console 有无 `配置资产已写入`）或**没人装配配置库** → 场景挂 `ConfigInstaller`（见 §10） |
| `LoadAsync<组件类型>(addr)` 抛 `InvalidKeyException` | Addressables 只认资产主类型 → 用 `LoadAsync<GameObject>` + `GetComponent<T>()`（见 §12） |
| 面板 `Close` 关不掉 | 传的必须是 **`Open` 的返回值**（活实例），不是 prefab（见 §6） |
| 存档"成功"但文件没写 | `Save()` 恒返回 true → 用 `TryGetSlotInfo(slot, out _)` 自查（见 §11） |
| 重进 Play 有幽灵回调 | `EventBus` 是静态的：订阅了就必须在 `OnDisable/OnDestroy/Dispose` 退订（见 §4） |
| 服务里拿不到 `Camera.main` / 场景物体 | 服务的 `Init` 跑在 **BeforeSceneLoad**（场景还没加载）→ 首次使用时再找（懒解析） |
| 退出 Play 编辑器静默卡死 | 拆卸路径没终止：`while` 每轮必须让集合缩小，先停播/解绑再销毁（铁律 8） |
| 中文变乱码 | CSV 与 `.cs` 都必须存 **UTF-8**（中文 Windows 的 Excel 默认 ANSI/GBK） |
| 场景里两个 EventSystem，你的不生效 | 输入层在 `BeforeSceneLoad` 已自建一个 → 别在场景里再摆 |
| 编辑器里加组件报 "because it is an editor script" | **仅编辑器**程序集里的 MonoBehaviour，文件名不能与类名相同 |
| 测试莫名失败 | 故意触发的 Error 日志必须先 `LogAssert.Expect(...)`；静态状态用例间要清理（见 `Tests/EditMode/EventSubscriptions.cs`） |

> 铁律与完整的坑清单见仓库根 `CLAUDE.md`；每个模块的"为什么这么设计"见各类的头部注释。
