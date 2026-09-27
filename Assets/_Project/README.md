# Template — 项目结构总纲

> 完整功能规划与开发顺序见 `docs/功能目录.md`（规划阶段定稿，本仓库的“合同”）。

## 目录规范

| 目录 | 用途 |
|---|---|
| `_Project/Scenes` | 唯一启动场景 + 各游戏场景（场景 ≠ 游戏状态） |
| `_Project/Scripts` | 全部 C# 代码（每个模块 = 一个 asmdef + 一个命名空间） |
| `_Project/Data` | 配置表：CSV 源在 `Data/Configs/`，生成的资产在 `Data/Configs/Generated/` |
| `_Project/Prefabs` | 共享预制体（按系统分子目录） |
| `_Project/Art` | 美术资产（贴图 / 模型 / 动画…） |
| `_Project/Audio` | 音频资产（按 BGM / SFX / Voice 分子目录） |
| `_Project/Tests` | 测试（EditMode / PlayMode）；阶段 3 起重逻辑模块必须带测试 |
| `_ThirdParty` | 第三方插件（DOTween 等），**只读不改源码**；插件官方要求放别处的照它办（UniTask 按官方文档在 `Assets/Plugins/UniTask`，同样只读） |

## 程序集与命名空间

原则：**每个模块一个 asmdef，命名空间与 asmdef 一一对应**。asmdef 强制模块单向依赖，
防止代码互相引用成毛线团 —— 这是“长期可维护”的技术底座。

| 程序集 | 目录 | 命名空间 | 状态 |
|---|---|---|---|
| `Template.Core` | `Scripts/Core` | `Template.Core.*` | ✅ 阶段 1 |
| `Template.UI` | `Scripts/UI` | `Template.UI` | ✅ 阶段 2.1（uGUI 面板框架） |
| `Template.Input` | `Scripts/Input` | `Template.Input` | ✅ 阶段 2.2（New Input System） |
| `Template.Audio` | `Scripts/Audio` | `Template.Audio` | ✅ 阶段 2.3（音频系统） |
| `Template.Settings` | `Scripts/Settings` | `Template.Settings` | ✅ 阶段 2.4（设置系统） |
| `Template.Config` | `Scripts/Config` | `Template.Config` | ✅ 阶段 3.1（配置表运行期） |
| `Template.Config.Editor` | `Scripts/Config/Editor` | `Template.Config.EditorTools` | ✅ 阶段 3.1（导入管线，**仅编辑器**） |
| `Template.Configs` | `Scripts/Configs/Generated` | `Template.Config.Generated` | ✅ 阶段 3.1（**自动生成**，勿手改） |
| `Template.Save` | `Scripts/Save` | `Template.Save` | ✅ 阶段 3.2（存档系统） |
| `Template.Assets` | `Scripts/Assets` | `Template.Assets` | ✅ 阶段 3.3（Addressables 资源服务） |
| `Template.Assets.Editor` | `Scripts/Assets/Editor` | `Template.Assets.EditorTools` | ✅ 阶段 3.3（Addressable 标记工具，**仅编辑器**） |
| `Template.Vfx` | `Scripts/Vfx` | `Template.Vfx` | ✅ 阶段 4.10（特效池化 + 自动回收） |
| `Template.CameraSystem` | `Scripts/CameraSystem` | `Template.CameraSystem` | ✅ 阶段 4.9（Cinemachine 薄封装 + Trauma 震动） |
| `Template.Diagnostics` | `Scripts/Diagnostics` | `Template.Diagnostics` | ✅ 阶段 4.11（运行期调试面板，**仅编辑器 / 开发构建**，可整体摘除） |
| `Template.Demo` | `Scripts/Gameplay/Demo` | `Template.Demo` | ✅ 阶段 1+2.1+2.2+2.3+2.4（**仅编辑器**，可整体删除） |
| `Template.Tests.EditMode` | `Tests/EditMode` | `Template.Tests` | ✅ 阶段 3.0（纯逻辑测试） |
| `Template.Tests.PlayMode` | `Tests/PlayMode` | `Template.Tests` | ✅ 阶段 3.0（播放模式冒烟测试） |
| （各系统模块） | `Scripts/Systems/*` | `Template.*` | 按阶段建立 |
| （玩法模块） | `Scripts/Gameplay/*` | `Template.*` | 按阶段建立 |

阶段 1 已落地的 Core 内部命名空间：

| 命名空间 | 内容 |
|---|---|
| `Template.Core.Services` | `IGameService` / `ITickable`（生命周期契约）、`ServiceLocator` |
| `Template.Core.Eventing` | `EventBus` / `EventBus<TEvent>`（强类型事件总线） |
| `Template.Core.Timing` | `TimeService`（Timer + 冻结）、`TimerHandle`、`TimerClock` |
| `Template.Core.Pooling` | `ObjectPool<T>`（纯类池）、`GameObjectPool`、`PoolService`、`IPoolable` |
| `Template.Core.Fsm` | `IState` / `StateBase` / `StateMachine`（表驱动转换） |
| `Template.Core.App` | `Bootstrap`（启动器）、`GameStateService`（GamePhase 状态机）、`SceneFlowService`、`SceneContext` |

> Scripts 下 `Systems / UI / Gameplay` 是模块的**分类目录**，未来每个模块在其下
> 建独立文件夹 + 独立 asmdef，并在依赖中引用 `Template.Core`。

## 工程铁律（随阶段补充，违者入 Code Review）

1. **服务不继承 MonoBehaviour** —— 实现 `IGameService`（Init/Dispose），需要每帧推进的加
   `ITickable`（Tick），由启动器 Bootstrap 注册、Init、逐帧驱动、逆序 Dispose；
   场景中禁止满天飞的 Update()，场景脚本访问服务走 `ServiceLocator.Get<T>()`（在 Start 中，不在 Awake）。
2. **模块间只通过事件与接口通信**（事件中心阶段 1 落地），禁止 A 系统直接 new 或调用 B 系统具体类。
3. **数值与文本进配置表，代码零魔法数字**（配置管线阶段 3 落地）。
4. **全面禁用 `Resources.Load`**，异步统一走 UniTask。
5. **代码内禁止直接 `Debug.Log`** —— 一律走日志门面（见下）。
6. **第三方插件只读**：永不修改 `_ThirdParty` 内源码（升级即炸）。
7. 阶段 3 起，重逻辑模块（背包 / 存档 / 任务）**必须同步带测试**。
8. **拆卸路径（Dispose / CloseAll / 退出 Play）必须"有序且必然终止"**：先停播、解绑引用再销毁对象；
   遍历集合的 `while` 每轮都必须让集合缩小。违反的表现是**退出 Play 静默卡死编辑器、无任何日志**
   （曾因面板被引擎先行销毁、`Close()` 返回 false 导致 `CloseAll` 原地空转）。

## 日志快速上手（阶段 0 已可用）

```csharp
using Template.Core.Logging;

Log.Info("Save", "存档完成: 槽位 {0}", slotIndex);   // 支持格式化与标签
Log.Warn("Save", "写入失败，已降级");
Log.Error("Save", exception);                          // 异常重载，保留完整堆栈
Log.Verbose("Pool", "预热完成", this);                 // 高频调试输出；第三个参数是 context（见下）
```

> `context` 传一个 UnityEngine.Object（通常就是 `this`）时，双击 Console 里的这条日志会**在 Hierarchy 中
> 高亮那个物体**而不是跳到代码行 —— 排查"这条日志是哪个对象打出来的"很有用。传 null（默认）则跳代码行。

- 编辑器 Console 中 `[标签]` 高亮显示，可按文本检索；
- **双击日志直接跳到你的调用点**：`Log` 与 `GameLogHandler` 的实现整段包在 `#line hidden` 里 ——
  运行时不给这些帧记录源码信息，而 Console 双击会跳过"没有源码信息"的帧，于是落到你的代码上
  （等价于"把门面编成无 pdb 的 DLL"，但源码仍在工程里）。⚠ 注意 `[HideInCallstack]` **做不到**这件事：
  官方说明它只影响 Console 详情区的显示，不影响双击跳转。**给门面加新的转发方法时，务必写进那两个
  `#line hidden` 区间内**，否则双击会停在新方法上；
- **日志正文自带调用点**：每行前面还有 `(DemoRunner.cs:412)`，日志被复制出 Console 后
  （贴给别人、写进日志文件）照样能定位。嫌开销可以 `Log.IncludeCallSite = false`；
- 运行时过滤：`Log.MinLevel = LogLevel.Info;` 可屏蔽 Verbose 细节；
- 真机自动把 Error 写入 `persistentDataPath/Logs/log-*.log`，可手动
  `Log.EnableFileOutput()` 开启全量落盘（注意 IO 开销）；
- 正式包（非 Development Build）中 Verbose / Info 自动编译为空实现；
  彻底剥离需打包时注入宏 `TEMPLATE_STRIP_LOGS`（构建管线阶段实现）。

## 阶段产物

**阶段 0：骨架** —— git 规范、目录树、`Template.Core` 程序集、日志系统、扩展方法库。

**阶段 1：核心循环** —— 事件中心 / 服务定位器 / 时间管理器 / 对象池 / FSM / 游戏状态机 / 场景流转，
全部由 `Bootstrap` 在 Play 时自动自举（`BeforeSceneLoad`，无需 _Boot 场景即可在任何场景 Play）。
`Scripts/Gameplay/Demo` 提供一条覆盖全部模块的时间线演示（仅编辑器，可整体删除）。
> 演示时间线**默认开启**（`DemoRunner.DemoEnabled = true`），下面各阶段的验收步骤都直接依赖它 ——
> **开始写自己的游戏后请把它改成 `false`**（或整个目录删掉），否则它会抢游戏状态机、开面板、放音效。

**阶段 2.1：UI 框架（uGUI）** —— `Template.UI`：`UIService`（服务，Bootstrap 注册）统一管理
**Screen（全屏页，互斥压栈）/ Popup（弹窗，自动遮罩）/ Overlay（悬浮提示）** 三层面板栈；
业务面板继承 `UiPanel` 声明层级与生命周期钩子；开/关发布 `UIPanelOpenedEvent / UIPanelClosedEvent`；
返回统一走 `TryBack()`（弹窗优先 → 页面 → 栈底交业务决策），输入层只调一次不各自实现。
EventSystem 托管与 UI Cancel（Esc / 手柄 B）→ TryBack 联动由阶段 2.2 输入系统接通；
面板资源加载阶段 3 接 Addressables 后扩展，API 不变。

**验收（阶段 1）**：进入 Play，Console 中应依次看到：
`核心框架就绪` → `阶段 Boot→Splash→MainMenu→Loading→Gameplay→Paused→Gameplay` 的状态流转日志、
Game 视图 3 个自动回收的方块（池演示）、PingEvent 事件收发与广播中退订、
暂停期间 `[unscaled]` 触发而 `[scaled]` 不触发 —— 最后输出 `═══ 演示完成 ═══`。

**验收（阶段 2.1 UI）**：MainMenu 阶段约 2.5s 后自动演示（Game 视图观察）：
① 主菜单全屏页 → ② 设置页压栈时主菜单自动隐藏 → ③ 返回后主菜单 `OnShown` 恢复 →
④ 确认弹窗打开时半透明遮罩亮起（下层被挡住）→ ⑤ Toast 悬浮在遮罩之上（Overlay 不参与返回）→
⑥ 返回键先关弹窗再关页面、到栈底返回 false（业务决策日志）→ ⑦ CloseAll 收尾进入 Loading。
Console 全程无红错即通过。

**阶段 2.2：输入系统（New Input System）** —— `Template.Input`：`InputService` 代码构建内置
**UI 动作映射**（Navigate/Submit/Cancel/Point/Click/ScrollWheel…标准动作名，与 .inputactions 资产同构），
并自动创建托管 `EventSystem`；游戏动作由游戏层 `AttachMap()` 附加（映射由游戏层构建持有），
**进入 Paused 自动禁用、恢复自动启用**（暂停屏蔽，UI 映射不受影响）；`UI Cancel → UIService.TryBack()`
联动已接通；设备方案（键鼠/手柄/触摸）检测发布 `InputSchemeChangedEvent`。
动作名与标准 UI 资产一致 —— 如需自定义键位布局，可在编辑器里用
同名动作的 .inputactions 资产整体替换内置映射。

**验收（阶段 2.2 输入）**：进入 Play 后**点一次 Game 画面使其获得焦点**，观察：
① 摘要日志含 `InputService`，随后 `输入系统就绪` 与 `EventSystem 已创建`；
② 首次按键后出现 `输入方案检测 → 键鼠`（期间接入手柄再按键会切到"手柄"）；
③ UI 演示段（主菜单出现后）按 **Esc** 应有关闭/返回日志；弹窗打开时按 Esc 直接关弹窗（Cancel→TryBack）；
④ Gameplay 段打印 `Gameplay 映射 <Gameplay_Demo> 已附加并启用`；
⑤ 转入暂停打印 `暂停：已自动禁用 1 个 Gameplay 映射` —— 暂停 1 秒内按空格**应无任何日志**（屏蔽生效）；
⑥ 恢复打印 `自动重新启用` 并进入 **输入验收窗口（3.5 秒）**：按 **Esc** 关闭中央弹窗 →
   出现 `✓ Esc 联动验证通过`；期间按**空格**出现 `[输入] 开火！设备 <Keyboard>`（手柄则显示手柄名）。
   窗口结束弹窗仍开着 → 打印未收到取消键的告警（说明 Esc 链路异常，保留 Console 反馈）。
Console 全程无红错即通过。

> 环境前提：New Input System 要求 Project Settings → Player → Other Settings →
> **Active Input Handling = Both**（仓库代码不依赖旧输入，clone 后需手动设一次，编辑器会重启）。

**阶段 2.3：音频系统** —— `Template.Audio`：`AudioService`（服务，Bootstrap 注册）分
**Master / BGM / SFX / Voice** 四通道（代码构建，零资产依赖）：

- **独立音量**：`SetVolume(AudioBus, 0~1)` 线性值即改即生效（通道增益 = Master × 该通道）。
  持久化交给阶段 2.4 的设置系统（键 `audio.{bus}.volume`），进 Play 自动读回；
  设置系统缺席时自动降级为"仅本次运行生效、不持久化"。
  > 未用 AudioMixer 资产：2022.3 无法用代码创建 Mixer 分组（AudioMixer 只有操作既有资产的
  > SetFloat/GetFloat 等），而模板铁律是零资产依赖。需要混响/压缩等效果器分层的项目，
  > 在编辑器里建 Mixer 资产挂到 `AudioService.MixerOwner` 下的通道源即可，本类 API 不变。
- **淡入淡出**：BGM 双源**交叉淡变**（换曲无静音缝）；淡变以 **unscaled 时间**推进 ——
  暂停（timeScale=0）时音频操作仍生效。
- **SFX 池化**：AudioSource 自持池，2D 平铺并发、3D 按世界坐标衰减（`PlaySoundAt`），播完自动回收。
- **Voice 单轨**：新语音打断旧语音（对话推进的默认语义）。
- **界面音联动**：订阅 `UIPanelOpenedEvent / UIPanelClosedEvent` 播放默认界面音 ——
  音频模块不认识任何具体面板，演示模块间只经事件通信。默认音为运行期合成，
  真实项目在 Boot 阶段替换：`AudioFeedback.DefaultScreenOpen = myClip;`（置 null 即关闭该类音）。

**验收（阶段 2.3 音频）**：进入 Play 并**点一次 Game 画面**，**开扬声器**，MainMenu 阶段约 2.2s 后
音频演示开始（全程 Console 有步骤编号日志对应）：
① `BGM 起播` → 听到约 1.5 秒淡入（音量从无到有）；
② `2D 音效` → 一声枪响，左右耳等响；
③ `3D 空间音` → 左前方、右前方各一声，**能听出方位差异**；
④ `音量调节` → BGM 明显变轻（30%），随后恢复；
⑤ `交叉换曲` → 换曲过程**无静音缝**；
⑥ `语音单轨` → 连播两句只听到后一句（第一句被打断）；
⑦ `BGM 淡出` → 音乐 0.8 秒内渐弱至无声。
随后 UI 演示段**每次开关面板都有界面音**（事件联动验证）；Gameplay 段按**空格**开枪为 3D 枪声；
输入验收窗口左右各响一声 ping。结尾 `演示完成（阶段 1 核心循环 + 2.1 UI 框架 + 2.2 输入系统 + 2.3 音频系统 + 2.4 设置系统）`。
Console 全程无红错即通过。

> 注意：音量会持久化到设置文件 —— 上次调过静音的话，下次 Play 仍是静音（属预期）；
> 要复位可删掉 `persistentDataPath/settings.json`，或在阶段 2.4 的设置界面里改回来。

**阶段 2.4：设置系统** —— `Template.Settings`：`SettingsService`（服务，Bootstrap 注册）是全局设置的
**唯一出口与存档载体**（设置项由使用方模块声明，持久化都经它）：

- **强类型键**：`SettingsKey<T>`（id + 默认值），设置项由**使用方模块**声明 —— 音频声明
  `AudioSettings.VolumeKeys`（索引 = AudioBus）。
  新增一个设置项 = 定义一个键 + 写一处应用逻辑，**框架与存储格式都不动**；
  删除一个设置项同理（设置项级可删性）。
- **存储**：`ISettingsStorage` + 默认 `JsonFileSettingsStorage`
  → `persistentDataPath/settings.json`，缩进 JSON、人类可读可手改，带 `Version` 字段
  （版本迁移链接口已留，与阶段 3 存档系统同一套路）。只支持 float/int/bool/string 四种基本类型。
- **加载**：**首次访问即加载**（懒加载）—— 于是 Init 顺序无关紧要，任何服务在自己的 Init 里
  `Get` 都能拿到已加载的值。
- **写盘**：`Set` 只标脏，Tick 延迟合并（默认 0.6s，吸收滑条拖动的高频写入），
  `Dispose` 兜底落盘 —— 改完立刻按 Stop 也不会丢。
- **生效**：`SettingsChangedEvent<T>`（事件中心，强类型）—— 消费方"Init 拉取初值 + 订阅后续变更"
  两件都做，缺一不可。
- **可整删性**：音频用 `ServiceLocator.TryGet` 解析设置服务，**删掉整个 Template.Settings 模块**后
  它自动降级为"设置只在本次运行内生效"，其余功能不受影响。
- **内置 UI 动作显式声明了期望控件类型**（Navigate/Point/ScrollWheel = Vector2，按钮类 = Button）——
  动作期望类型与实际绑定的控件不符会抛 `InvalidOperationException`（把 uGUI 输入模块读崩），
  声明出来既自解释，也便于将来用同名 .inputactions 资产整体替换内置映射。

**验收（阶段 2.4 设置）**：Play 后 Console 开头即有
`启动时设置（上次运行保存的）：音量 Master 100% / …`（首次运行即默认值）。
UI 演示结束后进入**设置演示窗口（可交互 25 秒）**，Game 视图出现设置页：

① 拖动 4 条音量滑条 → 右侧百分比实时变化，**声音同步变响/变轻**（Master 影响全部通道），
  状态栏显示 `已写入设置：…`；
② 按 **Esc** 关闭设置页 → 回到主菜单（Screen 压栈/恢复语义），25 秒后自动收尾进入 Loading；
③ **持久化验收**：改完音量后按 Stop，再 Play → 开头那行 `启动时设置` 显示的就是你改过的值
  （设置文件在 `persistentDataPath/settings.json`，可打开查看）。Console 全程无红错即通过。

**阶段 3.0：测试骨架** —— `Template.Tests.EditMode` / `Template.Tests.PlayMode` 两个测试程序集
（`defineConstraints: ["UNITY_INCLUDE_TESTS"]` → 只在启用测试时编译，不会进正式包）。
首批用例覆盖：**对象池**（复用/容量/工厂返回 null）、**事件总线**（广播、广播中退订、异常隔离）、
**状态机**（转换表）、**设置服务**（默认值、延迟写盘、跨实例读回、损坏文件降级、事件携带强类型键）、
**线性↔分贝换算**。

跑法：**Window → General → Test Runner** → EditMode / PlayMode → **Run All**，Console 全绿即通过。
EditMode 秒级完成；PlayMode 会真的进播放模式（`DemoRunner` 检测到测试运行器的临时场景
`InitTestScene*` 会跳过演示时间线，不干扰测试）。

约定（铁律 7 落地）：**阶段 3 起，重逻辑模块（配置表 / 存档 / 背包 / 任务）必须同步带测试** ——
纯逻辑进 EditMode（快、不必进 Play），涉及场景 / 生命周期 / 协程的进 PlayMode。
测试里的 `EventSubscriptions` 助手负责统一退订：事件总线是静态的，用例之间不退订会互相污染。

> 写测试时的两个坑（都已在本批用例里示范）：
> ① **未声明的 Error 日志 = 测试失败** —— Unity Test Framework 把 Error/Exception/Assert 级别的日志
>    当作失败信号。故意触发错误路径的用例（如"损坏文件降级""监听者抛异常"）必须在日志出现**之前**
>    调用 `LogAssert.Expect(LogType.Error, new Regex("..."))` 声明它；
> ② 静态状态要自己收拾 —— 事件总线、`ServiceLocator` 都是静态的，用例之间会互相污染（见 `EventSubscriptions`）。

**阶段 3.1：配置表管线** —— CSV → 强类型代码 + ScriptableObject 资产
（`Template.Config` 运行期 / `Template.Config.Editor` 导入工具 / `Template.Configs` 生成物，
生成物的命名空间是 `Template.Config.Generated` —— 叫 `Template.Configs` 会和类名 `Configs` 撞车，
编译器会把 `Configs.Item` 解析成"命名空间 Template.Configs 的成员 Item"而报 CS0234）：

- **源**：`Data/Configs/<表名>.csv`（单数表名，如 `Item.csv`）—— 第 1 行字段名、第 2 行类型、
  第 3 行起数据；支持引号包裹（可含逗号/换行，`""` 表示引号）、`#` 注释行、空行。
  类型：`int / float / bool / string`，以及**外键**（写目标表的行类型名如 `ItemConfig`，值写目标行 id）。
  > **编码铁律：CSV 一律存 UTF-8。** 中文 Windows 的编辑器常默认 ANSI/GBK → 中文在游戏里变乱码；
  > 更阴的是 **GBK 字节有时恰好是合法 UTF-8**（如"木剑"的 GBK 字节就是），管线无法从字节层面可靠识别，
  > 所以导入工具只做能可靠做的事：拒绝非法 UTF-8 字节 + 自动剥掉 Excel「CSV UTF-8」写入的 BOM。
  > 改名/改数据后，顺手在 Console 或 Play 里确认中文正常。
- **导入**（菜单 `Tools/配置表/全部导入`；改完 CSV 保存也会自动跑）：解析 → 校验
  （id 列必须是 int 且唯一、类型可识别、值可解析、外键指向存在的行）→ 生成强类型代码 →
  写资产 `Data/Configs/Generated/<表>Table.asset` 并维护 `ConfigDatabase.asset`。
  **校验不过就整体终止**，既不写代码也不写资产 —— 绝不把半套数据放进运行期（运行期保留上一次的好数据）。
  两段式的原因：新表要先有"生成的类型"才能建资产，而新类型要等重编译；所以代码有变化时先等重编译，
  重编译完成后自动接着写资产；**只改数值（代码没变）时立即写盘，毫秒级反馈**。
- **运行期**：`Configs.Item.Get(1001)`（缺项返回 null）/ `Require(1001)`（缺项记 Error 日志）/
  `All` / `Count` / `TryGet` / `Contains`。配置表是**只读资产**，运行期不做增删改（那是存档系统的事）。
- **装配**：Boot 场景挂 `ConfigInstaller`，把配置库交给 `ConfigService`（模板禁用 `Resources.Load`，
  运行期拿资产只能靠显式引用）。阶段 3.3 接入 Addressables 后改由服务按 address 加载，本组件可撤掉，
  业务侧 API 不变。

**新增一张表只要三步**：① 在 `Data/Configs/` 加 `Skill.csv`（字段名行 + 类型行 + 数据）
② 跑一次导入（或直接保存 CSV 即自动导入）③ 用 `Configs.Skill.Get(id)` 读 —— **不用写任何 C#**。

**验收（阶段 3.1 配置表）**：Play 后 MainMenu 一开始就有这几行日志：
`配置库已装配：1 张表 [ItemTable]` → `配置读取：Configs.Item.Get(1002) → 铁剑（价格 33，攻击 25）`
→ `缺项语义：Configs.Item.Get(9999) = null`。
（外键解析那一行需要第二张带外键的表 —— 本仓库的示例数据只剩 `Item.csv`，那段演示已跳过；
放回一张带外键列的 CSV 即可恢复。）
再验两件事：① 把 `Item.csv` 里铁剑的价格改成 999 保存 → Console 出现 `配置资产已写入：1 张表 / 4 行`，
下次 Play 同一行显示 999（**改表不改码**）；② 故意写坏一行（价格填 `abc`）→
导入中止并逐条报错，**运行期数据仍是上一次导入成功的那份**。Console 全程无红错即通过。

**阶段 3.2：存档系统** —— `Template.Save`：多槽位 + 版本迁移链 + 损坏降级，`SaveService`（服务，Bootstrap 注册）：

- **分片注册**（核心设计）：存档系统**不认识任何业务数据** —— 背包/任务/成就各实现一个
  `ISaveSection { Key; Capture(serializer); Restore(payload, serializer); }` 在 Boot 阶段注册。
  一个分片坏掉/读不了只影响它自己（记日志 + 走默认值），不拖垮整个读档；加新系统不用动存档系统。
- **存储**：`ISaveStorage` → 默认 `FileSaveStorage`（`persistentDataPath/saves/slot_N.json`）
  **原子写盘**：先写 `.tmp` → 原子替换主档 → 旧档留成 `.bak`。写一半断电只损失这一次保存，不毁上一份好档。
- **序列化**：`ISaveSerializer` → 默认 `JsonSaveSerializer`（JsonUtility，**缩进输出、人类可读**、零依赖）。
  局限：不支持 `Dictionary`/多态（用 `List` + `[Serializable]` 类代替）；要换 Newtonsoft / MessagePack 只替换实现。
- **版本迁移链**：存档头带 `Version`，读档时逐级跑 `ISaveMigration`（v1→v2→v3…）。
  每条迁移只处理相邻两版的差异、只做结构搬运（纯函数 → 可单测）。**缺步或存档版本高于程序 → 读档失败**，
  绝不用错位数据进游戏。加新版本：`SaveService.CurrentVersion + 1` + 补一条迁移。
- **降级顺序**：主档 → `.bak` 备份 → 明确失败并给出原因（业务据此提示玩家，不白屏）。
- **槽位概览**：`TryGetSlotInfo(slot, out SaveSlotInfo)` 给出 版本/摘要/保存时间（存档列表界面用）。

**验收（阶段 3.2 存档）**：演示按**真实顺序**（先读档、再保存）走，MainMenu 一开始就能看到：

- **首次 Play**（槽位 0 无档）：`存档系统就绪：格式 v1，0 个分片，0 条迁移，目录 …`（启动时业务还没注册分片）
  → `已注册存档分片：demo.inventory` → `槽位 0 还没有存档（首次运行属正常）`
  → `存档写入后（内存）：金币 100，物品 [1001、1003]` → `故意改脏内存：金币 999，物品 []`
  → `读档成功（内存已还原）：金币 100，物品 [1001、1003]`（读档覆盖内存）
  → `存档槽位：1 个 [0]` → `槽位概览：槽位 0：v1 · 演示档 · 第 1 章 · <时间>`；
- **再 Play 一次**：开头变成 `启动读档成功：金币 100，物品 [1001、1003]` —— **跨运行持久化**；
- **验降级**：去 `persistentDataPath/saves/` 把 `slot_0.json` 内容改坏几个字符，再 Play →
  开头第一条就读到 `启动读档成功：…（主档损坏已回退备份档：…）`，数据仍是好的（**这就是降级**）。
  > 注意：必须在 Play **之前**改坏 —— 演示会先读档，若先保存就把坏档覆盖了。
- 存档文件长这样：`slot_0.json`（缩进 JSON，能直接看到各分片的载荷）+ `slot_0.json.bak`（上一次保存前的内容）。
- 不需要演示档就删掉整个 `saves` 目录。Console 全程无红错即通过。

**存档快速上手**（谁的数据谁自己存；加新系统不用动存档系统）：

```csharp
using Template.Save;
using Template.Core.Logging;
using Template.Core.Services;

// 1) 实现分片：自己的状态结构 + 自己序列化
private sealed class InventorySection : ISaveSection
{
    [Serializable] private sealed class State { public int Gold; public List<int> Items = new List<int>(); }

    private readonly Inventory _inventory;                        // 业务对象
    public InventorySection(Inventory inventory) => _inventory = inventory;

    public string Key => "inventory.items";
    public string Capture(ISaveSerializer s) => s.Serialize(new State { Gold = _inventory.Gold, Items = _inventory.Items });
    public bool Restore(string payload, ISaveSerializer s)
    {
        try { var st = s.Deserialize<State>(payload); _inventory.LoadFrom(st.Gold, st.Items); return true; }
        catch (Exception) { return false; }                        // 读不了 → 保持默认值（存档系统会记日志）
    }
}

// 2) Boot 阶段注册（服务装配完成后）
var save = ServiceLocator.Get<SaveService>();
save.RegisterSection(new InventorySection(_inventory));

// 3) 存 / 读 / 列表
save.Save(slot: 0, summary: "第 3 章 · 12 分钟");
if (!save.Load(0, out string error))
    Log.Error("Save", "读档失败：{0}", error);        // 业务据此提示玩家，别白屏

foreach (int slot in save.ListSlots())
    if (save.TryGetSlotInfo(slot, out SaveSlotInfo info))
        Log.Info("Save", "{0}", info);               // 槽位 0：v1 · 第 3 章 · 12 分钟 · 时间

// 4) 加新版本：CurrentVersion 改为 2，并补一条迁移（只处理 v1 → v2 的差异）
private sealed class AddQuestCountMigration : ISaveMigration
{
    public int FromVersion => 1;
    public void Apply(SaveData data, ISaveSerializer s) { /* 补字段/搬载荷，纯结构搬运 */ }
}
save.RegisterMigration(new AddQuestCountMigration());
```

**阶段 3.3：资源系统（Addressables）** —— `Template.Assets`：`AssetService`（服务，Bootstrap 注册）是资源唯一入口：

- **接口**：`await LoadAsync<T>(address)` + `Release(address)`（单资产）；`await LoadLabelAsync<T>(label)` +
  `ReleaseLabel(label)`（**整组加载/整组卸载** —— 切关卡释放就靠它）；`await InstantiateAsync(address, parent)` +
  `ReleaseInstance(go)`（预制体）；`Dump()`（当前挂着哪些资产 —— 泄漏排查第一现场）/ `ReleaseAll()`（兜底释放）。
- **引用计数**：同地址重复加载 = 同一份资产、计数 +1（**每次加载都要配一次 Release**），计数归零才真正卸载；
  同一地址用不同类型加载会报错（不做隐式转换，避免静默类型错乱）。
- **异步统一走 UniTask**（铁律 4）：`UniTask.Addressables` 用 versionDefines 自动启用
  `UNITASK_ADDRESSABLE_SUPPORT`（检测到 com.unity.addressables 即定义），**不需要手加宏**。
- **禁用 `Resources.Load`**（铁律 4）：本服务是唯一入口；加载失败返回 null + 记日志，业务按"加载不到"降级。
- **Label 约定**：模板示例用 `demo_config` 标记两张配置表资产；正式项目建议按 `chapter_01` / `ui` / `audio`
  这类分组，切关卡时 `ReleaseLabel("chapter_01")` 整组卸载（这也是"玩得越久内存越大"的解药）。
- **测试边界**：真实加载依赖 Addressables 的构建产物（没法脱离构建单测）→ 走 Play 验收；
  可测的**记账逻辑**（引用计数、整组取出、实例回收、诊断输出）拆在 `AssetLedger` 里，有 EditMode 单测。

**验收（阶段 3.3 资源）**：一次性准备 —— 菜单 **Tools/资源/初始化 Addressables 演示分组**
（把两张配置表资产标记为 Addressable 并打上 `demo_config` 标签；**不自动做**，因为那会擅自改你的分组设置）。
然后 Play，MainMenu 开头应看到：
`资源服务就绪：Addressables 统一入口（引用计数 + Label 整组释放）` →
`异步加载 <ItemTable> 成功：4 行，用时 N ms（引用计数 1）` →
`账本：1 项：· ItemTable [ItemTable] ×1 已加载 0.0s` →
`按 Label 整组加载 <demo_config>：2 项（同一批资产只需一次 ReleaseLabel）` →
`账本：2 项 …` → `整组释放 + 单资产释放后：0 项挂着（账本干净 ✅）`。
没跑那个菜单的话只多一行提示、不会报红（加载失败按业务降级处理）。Console 全程无红错即通过。

**阶段 4.9：相机系统** —— `Template.CameraSystem`：**薄封装 Cinemachine**（路线图要求："不要自研相机系统"）：

- **跟随**：`CameraHandle Follow(target, CameraFollowOptions)` —— 参数走 `CameraFollowOptions.Default3D`（第三人称：
  `CinemachineTransposer` + 阻尼，视角不随目标翻滚）/ `Default2D`（横版俯视：正交 + `CinemachineFramingTransposer` +
  死区 + 屏幕位置）。`Stop(handle)` / `StopAll()` 取消。
- **机位切换**：优先级 —— 后创建的默认接管（也可在 options 里显式指定 `Priority`）；混合/过渡由 Cinemachine 负责。
- **屏幕震动（Trauma 模型）**：`Shake(0~1)` —— 创伤**累加 + 按秒衰减**，力度按**平方映射**
  （`0.3 → 0.09`、`1.0 → 满力`：小事件只是轻抖、大事件才猛）。数学部分抽成 `CameraTrauma` 纯逻辑 + EditMode 单测；
  画面抖动交给 Cinemachine 的 Impulse 系统（源与监听器同频道，代码自动配好）。
- **零资产、代码构建**：主相机上的 `CinemachineBrain`、VCam 都在运行期由服务创建（挂在常驻根 `[CameraRig]` 下），
  不需要在场景里手工摆虚拟相机；业务代码只见 `CameraService`，不认识 Cinemachine 类型 —— 将来换实现不影响业务。
- **类型无关**：不预设锁敌/开镜/轨道/过场这些玩法语义；它们要么是某个 `Follow` + `Priority` 组合，要么放游戏层。
- **前提**：Package Manager → Unity Registry → **Cinemachine** → Install（装完即可；主相机需带 `MainCamera` 标签）。

**验收（阶段 4.9 相机）**：Play 后 MainMenu 开头（Game 视图配合 Console）：
`相机服务就绪：Cinemachine 虚拟相机 + Trauma 震动（主相机在首次使用时就近接管）` →
（首次跟随时）`已接管主相机 <Main Camera>（代码构建 CinemachineBrain，无需手工配置）`
→ `① 相机已开始跟随方块（Game 视图应平滑地对准它）` → 1 秒后 `② 方块瞬移到右侧 —— 相机应**平滑**跟过去`
（Game 视图里能看到相机带阻尼地追过去，而不是瞬间跳过去）→ `③ 轻受击震动：创伤 0.3 → 力度 0.09`（画面轻轻一颤）
→ `④ 大招震动：创伤 1.0 → 力度 3.00`（明显更猛）→ `⑤ 演示结束：已停止跟随`。
Console 全程无红错即通过。

**相机快速上手**（Cinemachine 薄封装；业务只见 `CameraService`，不认识 Cinemachine 类型）：

```csharp
using Template.CameraSystem;
using Template.Core.Services;
using UnityEngine;

var cam = ServiceLocator.Get<CameraService>();

// 3D 第三人称跟随（世界偏移 + 阻尼，视角不随目标翻滚）
CameraHandle follow = cam.Follow(player.transform, CameraFollowOptions.Default3D);
cam.Stop(follow);                                    // 取消跟随（主相机停在原地）

// 2D 横版 / 俯视：正交 + 屏幕位置 + 死区
CameraFollowOptions options2D = CameraFollowOptions.Default2D;
options2D.DeadZone = new Vector2(2f, 1f);            // 目标在小范围内移动时相机不动
cam.Follow(player.transform, options2D);

// 震动：0~1 的创伤值（累加 + 按秒衰减，力度平方映射 —— 小事件轻抖、大事件才猛）
cam.Shake(0.25f);                                    // 受击：轻轻一颤
cam.Shake(0.8f);                                     // 爆炸：明显晃动

// 多机位 / 过场：显式优先级（数值大的接管，混合交给 Cinemachine）
CameraFollowOptions bossView = CameraFollowOptions.Default3D;
bossView.Priority = 100;
cam.Follow(boss.transform, bossView);

cam.StopAll();                                       // 切场景兜底
```

**阶段 4.10：特效系统** —— `Template.Vfx`：`VfxService`（服务，Bootstrap 注册）一行播放、播完自动回收、实例走对象池：

- **两个入口**：`Play(address, pos, rot)`（走 Addressables，正式项目用；首次自动加载并建池）与
  `Play(prefab, pos, rot)`（已持有的引用 / 程序生成的特效）；`await PlayAsync(...)` 拿 `VfxHandle` 可单独 `Stop`。
- **复用 Core 的 `PoolService`**（不另造池）：同一个特效地址一个池，超容量由池策略直接销毁。
- **自动回收**（`Tick` 轮询）：有粒子系统 → "全部不存活"才回收；没有 → 按兜底时长回收。
  关键保护：**起播当帧绝不回收**（粒子起播当帧 `IsAlive` 为 false，只看它就会把刚生成的特效吞掉
  —— 音效池踩过同一个坑）。这条判定抽成 `VfxRecycleRule` 纯逻辑，有 EditMode 单测。
- **预热**：`await PrewarmAsync(address, count)` 进关卡/战斗前备好实例，避免首次播放卡一下。
- **兜底**：`StopAll()`（切场景/退出）、`Dump()`（列出特效池，泄漏排查）。
- **类型无关**：不预设任何"打击感/技能特效"语义 —— 谁播、播什么、播完做什么全由业务决定。

**验收（阶段 4.10 特效）**：Play 后 MainMenu 一开始：
`特效服务就绪：对象池复用 <PoolService>，播完自动回收` → `① 播放特效 #1/#2/#3（Game 视图看三个爆点）`
→ `当前活跃特效 3 个：1 个特效池，活跃 3 个实例` → 1.5 秒后
`② 1.5 秒后（粒子已播完）：活跃特效 0 个 —— 已自动回收 ✅` →
`③ 再播一个：成功 ✅（上面回收进池的实例会被复用，不会再新建）`。
Game 视图应看到三个橙色爆点出现在原点左右、约 1 秒内自行消失。Console 全程无红错即通过。

**阶段 4.11：调试面板（Debug Console）** —— `Template.Diagnostics`：把前几个阶段散落各处的
`Dump()` / 计数接口收口成**一个运行期面板**（`DiagnosticsService` + `DebugPanel`，代码构建，零美术资产）：

- **一个热键**：**F1**（或 `` ` ``）开关面板。关闭用 F1 或右上角 ×。
- **面板自带用法说明**：本次运行**第一次打开会先显示一页「这个面板是干什么的」**（看什么 / 点什么 / 敲什么 /
  怎么给自己的系统接进来），点右上角 **`?`** 随时切回报告。调试工具的第一用户是"第一次打开它的人" ——
  看不懂的面板等于不存在，所以这页是刻意做的，不是文档的替代品。
- **自带独立画布**（这条重要，别改成挂到游戏 UI 下）：面板用 `ConstantPixelSize` 挂在屏幕空间，
  尺寸就是**物理像素**，**不受游戏画布的 ScaleWithScreenSize 缩放影响** —— 小窗口下游戏 UI 会等比缩小，
  而 uGUI 字体是按画布倍率栅格化的，挂进去字会糊成一团
  （例如 1200×700 的 Game 窗口下 scaleFactor ≈ 0.63，15px 的正文只剩约 9px 高 —— 中文到这个尺寸就不可读了）。
  副作用是它也**不依赖 `UIService` / UILayer**：不参与 Esc 返回栈、只在自己矩形内吃点击，
  项目换 UI 方案（甚至不用 uGUI 面板框架）时调试面板照样能用。
- **看得见**：头部实时摘要（平滑 FPS / 平均帧时间 / **窗口最差帧** / 内存 / 托管堆 / GC 次数 / 时间缩放 / 阶段 / 帧号），
  主体是可滚动分节报告 —— 对象池、定时器、事件总线（监听者数 + 逐类型明细）、服务容器、配置表、存档槽位、
  资源账本、特效、音频、设置、输入、相机，全部来自各模块自己的公开统计接口。
- **摸得着**：时间缩放（1.0× / 0.5× / 0.25× / 2.0×）、暂停/继续（走 `GameStateService`，不直接改 timeScale）、
  日志级别循环切换（`Log.MinLevel`）、命令控制台（输入框回车执行）。
- **可扩展（两种接法，都不用改面板本体）**：
  ① `diag.RegisterSection(new DiagnosticsSection("背包", () => bag.Describe(), order: 120))` —— 加一节显示；
  ② `diag.Console.Register("give", "give <id> [n] —— 发物品", args => { ... return "已发放"; })` —— 加一条命令。
  模板**不预写任何玩法命令**（"无敌/加钱/跳任务"是具体游戏的东西，由游戏层注册，示例见 Demo）。
- **剔除策略（路线图要求"Release 包完全剔除"）**：本模块所有文件都包在
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 里 —— **正式包编译为空程序集，零残留**（与 `Log.Verbose/Info` 同一个开关）。
  ⚠ 因此引用它的程序集必须同样带守卫，或本身是编辑器专用程序集（如 `Template.Demo`）。
- **可整体摘除**：没有任何模块反向依赖它（模块要接面板一律走
  `ServiceLocator.TryGet(out DiagnosticsService diag)`），删掉 `Scripts/Diagnostics` 目录即可，
  删掉后各模块的 `TryGet` 静默跳过、面板也不存在 —— 就像 Demo 目录一样干净。
- **本身是可选模块**：`Save / Assets / Vfx / Camera` 等分节在服务缺席时自动跳过，不留空壳。

**验收（阶段 4.11 调试面板）**：Play 后等设置演示结束（约 41 秒），Console 打印
`调试面板演示（阶段 4.11）：面板已打开，接下来 2 分钟交给你`，Game 视图右上角出现深色面板
（窗口刻意给足 2 分钟：F1 开关、按钮、命令都值得挨个试一遍；2 分钟到点自动关面板进 Loading）：
⓪ **第一次打开先看到的是一页用法说明**（面板自己教的）——点右上角 `?` 切到实时报告，之后再打开就直接是数据；
① 头部 FPS 随画面波动（`0.5×` 按钮一点就看它变慢）；② 报告区各分节有实时数据，
在设置演示里改过音量的话「设置」「音频」两节能对上；
③ **按 F1 能反复开关面板**（窗口内自己试，这是本阶段唯一需要动手的验收点）；
④ 输入框敲 `help` 出现命令列表、敲 `dump` 把完整报告打到 Console、敲 `demo.spawn 5` 出池 5 个方块
（看「对象池」分节活跃数从 0 涨到 5，3 秒后回落）；
⑤ 窗口结束打印 `调试面板演示结束：N 个分节在线；本窗口帧率 FPS xx`。
Console 全程无红错即通过。

> 说明：命令输入框用的是 uGUI `InputField`，它依赖 **Active Input Handling = Both**（本模板的环境前提，见上）。
> 若你的项目改成 "Input System Package only"，请把输入框换成 `TMP_InputField`（面板其余部分不受影响）。

**调试面板快速上手**（一行接一节 / 一行加一条命令）：

```csharp
using Template.Core.Services;
using Template.Diagnostics;

// 取服务（可整体摘除的模块一律用 TryGet，别硬依赖）
if (ServiceLocator.TryGet(out DiagnosticsService diag))
{
    // 1) 给自己的系统加一节（内容是什么完全由你决定，面板只负责排版与刷新）
    diag.RegisterSection(new DiagnosticsSection("背包", () => bag.Describe(), order: 120));

    // 2) 加一条作弊命令（参数按空格切分，原样交给 handler；返回值直接显示在面板输出区）
    diag.Console.Register("give", "give <id> [n] —— 发物品", args =>
    {
        bag.Add(args[0], args.Length > 1 ? int.Parse(args[1]) : 1);
        return "已发放 " + args[0];
    });

    // 3) 也可以只借它的显示能力：把业务信息写进面板（不必开控制台）
    diag.Console.Write("任务进度：3 / 10");

    diag.Open();            // 也可以什么都不做 —— 玩家按 F1 就开
    diag.Sampler.Fps;       // 帧率采样器是公开的：你的性能监控/自动降画质逻辑可以直接读它
}
```

**特效快速上手**（一行播放、自动回收；预览用代码搭粒子系统也行）：

```csharp
using Template.Vfx;
using Template.Core.Services;

var vfx = ServiceLocator.Get<VfxService>();

vfx.Play("Hit_Spark", transform.position);                    // 正式项目：按 Addressables 地址播（首次自动加载 + 建池）
vfx.Play(effectPrefab, pos, Quaternion.identity);             // 或直接给预制体（程序生成 / 已持有引用）

VfxHandle handle = await vfx.PlayAsync("Explosion", pos);     // 需要单独停止时拿句柄
vfx.Stop(handle.Instance);

await vfx.PrewarmAsync("Hit_Spark", 10);                      // 预热：进战斗前备好 10 个实例
vfx.StopAll();                                                // 切场景 / 退出兜底
```

**资源快速上手**（Addressables 统一入口；`Resources.Load` 已禁用）：

```csharp
using Template.Assets;
using Template.Core.Logging;
using Template.Core.Services;
using UnityEngine;

// 在 async UniTask 方法里使用（铁律 4：异步统一走 UniTask）
var assets = ServiceLocator.Get<AssetService>();

// 可选：启动阶段预热，避免首次加载卡顿（Addressables 会自动初始化，但初始化本身有耗时）
await Addressables.InitializeAsync();

// 单资产：引用计数 +1
GameObject prefab = await assets.LoadAsync<GameObject>("Enemy_Goblin");
if (prefab == null)
{
    Log.Error("Assets", "敌人预制体加载失败，走降级");   // 失败返回 null，不抛异常穿透
    return;
}
assets.Release("Enemy_Goblin");                        // 计数归零才真正卸载

// 整组（关卡 / ui / audio…）：切关卡时整组卸载，防止"玩得越久内存越大"
IList<Sprite> icons = await assets.LoadLabelAsync<Sprite>("ui_mainmenu");
assets.ReleaseLabel("ui_mainmenu");

// 预制体实例（用完记得回收，或交给对象池托管）
GameObject enemy = await assets.InstantiateAsync("Enemy_Goblin", transform);
assets.ReleaseInstance(enemy);

// 切完关卡确认账本干净（泄漏排查的第一道闸）
Log.Info("Assets", "{0}", assets.Dump());
```

**配置表快速上手**（生成物勿手改，改数据只改 CSV）：

```csharp
using Template.Config.Generated;

// Item.csv：  id,name,price,attack,stackable
//            int,string,int,int,bool
//            1001,木剑,30,12,false

ItemConfig sword = Configs.Item.Require(1001);      // 缺项 → 记 Error 日志并返回 null
int price = sword.Price;                            // 强类型：没有 id 字符串、没有魔法下标
ItemConfig maybe = Configs.Item.Get(9999);          // 缺项 → 安静返回 null（业务自行降级）

foreach (ItemConfig item in Configs.Item.All)       // 遍历全表
    _shop.Add(item);

// 外键（Enemy.csv 的 dropItem 列类型写 ItemConfig，值写目标行 id）
EnemyConfig boss = Configs.Enemy.Require(1003);
ItemConfig drop = boss.DropItem;                    // 导入时已校验过引用存在
```

**Gameplay 映射快速上手**（输入动作由游戏层定义，模板只提供托管）:

```csharp
using Template.Core.Services;
using Template.Input;
using UnityEngine.InputSystem;

// 进玩法时附加（启停 / 暂停屏蔽由 InputService 统一管理）
var map = new InputActionMap("Player");
var fire = map.AddAction("Fire", InputActionType.Button);
fire.AddBinding("<Keyboard>/space");
fire.AddBinding("<Gamepad>/buttonSouth");
fire.performed += ctx => FireWeapon();
ServiceLocator.Get<InputService>().AttachMap(map);

// 出玩法时移除（自动禁用）
ServiceLocator.Get<InputService>().DetachMap(map);
```

**UI 面板快速上手**（业务面板 = prefab + UiPanel 子类；开合全交 UIService）：

```csharp
public class MainMenuScreen : UiPanel { }                       // 默认 Screen 层
public class ConfirmPopup : UiPanel { public override UILayer Layer => UILayer.Popup; }

// 打开/关闭（面板生命周期由 UIService 托管，勿自行 Instantiate/Destroy）
_ui.Open(confirmPrefab);
_ui.Close(panelInstance);
if (_ui.TryBack()) { /* 已由框架处理弹窗/页面关闭 */ }
else { /* 栈底：回主流程或退出游戏 */ }
```

**音频快速上手**（服务经 ServiceLocator 获取；素材由业务提供，模板只管播放）：

```csharp
using Template.Audio;
using Template.Core.Services;

var audio = ServiceLocator.Get<AudioService>();

audio.PlayBgm(bgmClip, fadeSeconds: 1.5f);          // 切歌（交叉淡变）
audio.PlaySound(clickClip);                         // 2D 音效（UI / 无方位）
audio.PlaySoundAt(hitClip, transform.position);     // 3D 空间音（枪声 / 脚步）
audio.PlayVoice(lineClip);                          // 语音（打断上一句）
audio.SetVolume(AudioBus.Sfx, 0.6f);                // 设置界面滑条（自动持久化）
audio.StopAll();                                    // 停播并解绑 clip（销毁自己传入的 clip 之前调用）
```

**设置快速上手**（设置项谁用谁定义，存储与广播归 `SettingsService`）：

```csharp
using Template.Core.Eventing;
using Template.Core.Services;
using Template.Settings;

// 1) 声明设置项（放在自己模块里；键 id 约定 "模块.设置项"，只支持 float/int/bool/string）
public static class PlayerSettings
{
    public static readonly SettingsKey<float> MouseSensitivity = new SettingsKey<float>("player.mouseSensitivity", 1f);
    public static readonly SettingsKey<bool>  InvertY          = new SettingsKey<bool>("player.invertY", false);
}

// 2) Init：拉初值 + 订阅变更 —— 两件都要做（只订阅会漏初值，只拉取则收不到后续改动）
var settings = ServiceLocator.Get<SettingsService>();
_sensitivity = settings.Get(PlayerSettings.MouseSensitivity);
EventBus<SettingsChangedEvent<float>>.Subscribe(OnSettingChanged);

private void OnSettingChanged(SettingsChangedEvent<float> e)
{
    if (e.Key.Equals(PlayerSettings.MouseSensitivity))   // 强类型键比较，没有字符串
        ApplySensitivity(e.Value);
}

// 3) 设置界面写入（即改即生效 + 延迟合并落盘 + 广播给所有订阅者）
settings.Set(PlayerSettings.MouseSensitivity, 1.6f);
```

**服务获取示例**（场景脚本 Start 中）：

```csharp
using Template.Core.Logging;
using Template.Core.Services;
using Template.Core.Timing;

public class MySystem : MonoBehaviour
{
    TimeService _time;
    void Start()
    {
        _time = ServiceLocator.Get<TimeService>();
        _time.Delay(2f, () => Log.Info("MySystem", "两秒后触发"));
        _time.Repeat(1f, OnTick); // 每秒回调，直到 Cancel
    }
}
```

**状态机快速上手**（玩家/敌人 AI 通用，见 `Template.Core.Fsm`）：

```csharp
var fsm = new StateMachine();
fsm.SetInitialState(new IdleState());
fsm.AddTransition(new Transition(runState, () => _moveInput != 0));
fsm.Start();
// 每帧: fsm.Tick();
```
