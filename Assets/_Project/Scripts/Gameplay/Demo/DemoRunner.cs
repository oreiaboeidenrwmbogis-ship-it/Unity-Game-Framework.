using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Template.Assets;
using Template.Audio;
using Template.CameraSystem;
using Template.Config;
using Template.Config.Generated;
using Template.Core.App;
using Template.Core.Eventing;
using Template.Core.Logging;
using Template.Core.Pooling;
using Template.Core.Services;
using Template.Core.Timing;
using Template.Diagnostics;
using Template.Input;
using Template.Save;
using Template.UI;
using Template.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Template.Demo
{
    /// <summary>
    /// 演示用强类型事件（演示事件总线；正式项目按“发布方所属模块”定义事件）。
    /// </summary>
    public struct DemoPingEvent
    {
        public int Id;

        public DemoPingEvent(int id) => Id = id;
    }

    /// <summary>
    /// 框架演示 —— 仅编辑器编译（asmdef includePlatforms: Editor），永不进正式包。
    /// 进入 Play 后观察 Console 时间线，验证：事件中心 / 服务定位 / 时间管理器 / 对象池 / 状态机 / 场景流转 /
    /// UI 框架 / 输入 / 音频 / 设置 / 配置表 / 存档 / 资源 / 相机 / 特效 / 调试面板
    /// （阶段 1 + 2.1 + 2.2 + 2.3 + 2.4 + 3.1 + 3.2 + 3.3 + 4.9 + 4.10 + 4.11）。本 Demo 目录可整体删除，不影响框架。
    ///
    /// 时间线：Boot → Splash → MainMenu(配置表 → 存档 → 资源 → 特效 → 相机 → 池/事件 → 音频 → UI 面板栈 → 设置界面 → 调试面板) → Loading →
    ///         Gameplay → Paused(冻结+输入屏蔽演示) → Gameplay(自由开火 + Esc 验收窗口) → 结束
    /// </summary>
    public static class DemoRunner
    {
        private static TimeService _time;
        private static GameStateService _gameState;
        private static PoolService _pools;
        private static UIService _ui;
        private static InputService _input;
        private static AudioService _audio;
        private static ConfigService _config;
        private static SaveService _save;
        private static AssetService _assets;
        private static VfxService _vfx;
        private static GameObject _vfxPrefab; // 演示用特效预制体（运行期代码搭的）
        private static CameraService _camera;
        private static GameObject _cameraTarget;   // 相机演示的跟随目标（运行期建的方块）
        private static CameraHandle _cameraFollow; // 跟随相机句柄
        private static DiagnosticsService _diagnostics; // 调试面板（阶段 4.11）
        private static readonly DemoInventorySection _inventory = new DemoInventorySection(); // 演示用存档分片
        private static InputActionMap _fireMap; // Gameplay 演示映射（启停/暂停屏蔽由 InputService 托管）
        private static UiPanel _escTarget;      // Esc 验收靶子的【模板】（Open 会克隆，它本身不进栈）
        private static UiPanel _escInstance;    // Open 出来的活实例 —— 判断是否被关掉、收尾关闭都必须用它
        private static UiPanel _settingsMenuBackdrop; // 设置页的返回目标（演示 Screen 压栈）
        private static DemoSettingsPanel _settingsPanel; // 设置界面模板（阶段 2.4）

        // 音频演示素材（运行期程序合成，见 DemoAudioClips）
        private static AudioClip _bgmMain;
        private static AudioClip _bgmAlt;
        private static AudioClip _sfxShot;
        private static AudioClip _sfxPing;
        private static AudioClip _voiceLine;
        private static AudioClip _voiceLine2;

        private static GameObject _cubeTemplate;
        private static bool _enteredGameplayOnce; // 区分 Gameplay 首次进入（演示暂停）与恢复进入（收尾）
        private static bool _started;

        /// <summary>
        /// 演示时间线总开关（**模板默认 `true`**：clone 下来直接 Play 就能看到全部模块演一遍，
        /// 也是各阶段验收脚本 —— 验收步骤见 README 对应段落）。
        ///
        /// **开始做自己的游戏后请改成 `false`**（或删掉整个 `Gameplay/Demo` 目录）：打开时它会抢游戏
        /// 状态机（自动 Boot→Splash→MainMenu→…）、开面板、放音效，并占住 25 秒设置窗口 + 2 分钟调试面板窗口。
        /// 演示是编辑器专用程序集，无论开关如何都不会进正式包。
        ///
        /// 注意用 static readonly 而不是 const：const 会让编译器把 Run() 里
        /// <c>if (!DemoEnabled) return;</c> 之后的整段判为"无法访问的代码"，每次编译刷一条 CS0162 警告。
        /// </summary>
        private static readonly bool DemoEnabled = true;

        /// <summary>无域重载（Enter Play Mode Options）下每次 Play 前重置启动标记，保证演示可重复运行。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay() => _started = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            if (!DemoEnabled)
                return; // 演示时间线已关闭（见 DemoEnabled）：写自己的游戏时保持关闭

            if (_started)
                return; // 无域重载模式下防止重复启动

            // PlayMode 测试运行器会加载临时场景（Assets/InitTestScene<ticks>.unity，判断依据同
            // Unity Test Framework 的 RuntimeTestLauncherBase）—— 此时不启动演示，免得开面板/切状态干扰测试。
            if (SceneManager.GetActiveScene().name.StartsWith("InitTestScene", System.StringComparison.Ordinal))
            {
                Log.Info("Demo", "检测到测试运行器场景，跳过演示时间线（PlayMode 测试专用）");
                return;
            }

            _started = true;

            _time = ServiceLocator.Get<TimeService>();
            _gameState = ServiceLocator.Get<GameStateService>();
            _pools = ServiceLocator.Get<PoolService>();
            _ui = ServiceLocator.Get<UIService>();
            _input = ServiceLocator.Get<InputService>();
            _audio = ServiceLocator.Get<AudioService>();
            _config = ServiceLocator.Get<ConfigService>();
            _save = ServiceLocator.Get<SaveService>();
            _assets = ServiceLocator.Get<AssetService>();
            _vfx = ServiceLocator.Get<VfxService>();
            _camera = ServiceLocator.Get<CameraService>();
            _diagnostics = ServiceLocator.Get<DiagnosticsService>();
            RegisterDiagnosticsCommands(); // 阶段 4.11：给调试面板挂上演示命令（作弊命令由游戏层自己注册）

            EventBus<GamePhaseChangedEvent>.Subscribe(OnPhaseChanged);
            EventBus<InputSchemeChangedEvent>.Subscribe(OnInputSchemeChanged);
            EventBus.VerboseLogging = true; // 演示期间打开事件调试日志

            Log.Info("Demo", "═══ 框架演示开始（当前阶段: {0}）═══", _gameState.Current);
            LogSettingsSnapshot("启动时设置（上次运行保存的）"); // 阶段 2.4：跨 Play 持久化的验收凭据
            if (_gameState.Current == GamePhase.Boot)
                _gameState.RequestTransition(GamePhase.Splash);
            else
                Log.Warn("Demo", "当前不在 Boot 阶段，跳过自动演示（框架本身仍已就绪）");
        }

        private static void OnPhaseChanged(GamePhaseChangedEvent e)
        {
            switch (e.Current)
            {
                case GamePhase.Splash:
                    // 时间管理器：Scaled 延迟（1 秒后进主菜单）
                    _time.Delay(1f, () => _gameState.RequestTransition(GamePhase.MainMenu));
                    break;

                case GamePhase.MainMenu:
                    RunMainMenuDemo();
                    break;

                case GamePhase.Loading:
                    Log.Info("Demo", "── Loading 阶段（演示状态流转；真实项目在此调用 SceneFlowService.LoadScene 加载关卡）──");
                    _time.Delay(1.2f, () => _gameState.RequestTransition(GamePhase.Gameplay));
                    break;

                case GamePhase.Gameplay:
                    if (!_enteredGameplayOnce)
                    {
                        _enteredGameplayOnce = true;
                        RunGameplayDemo();
                    }
                    else
                    {
                        RunPostPauseDemo(); // 输入演示窗口：暂停解除后映射自动恢复
                    }
                    break;

                case GamePhase.Paused:
                    RunPauseDemo();
                    break;
            }
        }

        /// <summary>主菜单阶段：纯类池 / GameObject 池（预热+回收）/ 事件总线（含广播中退订）演示。</summary>
        private static void RunMainMenuDemo()
        {
            Log.Info("Demo", "── MainMenu：对象池 + 事件总线 演示 ──");
            RunConfigDemo(); // 配置表演示（阶段 3.1）：同步读表，不占时间线
            RunSaveDemo();   // 存档演示（阶段 3.2）：存→改脏→读回，同步完成
            RunAssetDemoAsync().Forget(); // 资源演示（阶段 3.3）：异步加载，完成后自己打日志
            RunVfxDemo();    // 特效演示（阶段 4.10）：播 3 个 → 1.5 秒后看自动回收
            RunCameraDemo(); // 相机演示（阶段 4.9）：Cinemachine 跟随 + Trauma 震动（约 4 秒）

            // 1) 纯 C# 类池（高频临时对象）
            var listPool = new ObjectPool<List<int>>(() => new List<int>(8), onReturn: list => list.Clear());
            List<int> rented = listPool.Rent();
            rented.Add(7);
            rented.Add(9);
            Log.Info("Demo", "纯类池 ObjectPool: Rent 一个 List，内容 [{0}]，归还后复用", string.Join(",", rented));
            listPool.Return(rented);
            List<int> rentedAgain = listPool.Rent();
            Log.Info("Demo", "再次 Rent 得到同一实例（Count={0}，onReturn 已清空）→ 零 GC 复用 ✓", rentedAgain.Count);
            listPool.Return(rentedAgain);

            // 2) GameObject 池（预热 3，容量 8）
            _cubeTemplate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cubeTemplate.name = "[Demo]CubeTemplate";
            _cubeTemplate.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            _cubeTemplate.AddComponent<DemoCubePoolable>();
            _cubeTemplate.SetActive(false); // 模板休眠；池克隆它也保持休眠直至 Spawn

            GameObjectPool cubePool = _pools.CreatePool("demo_cube", _cubeTemplate, capacity: 8, prewarm: 3);
            Log.Info("Demo", "创建池 'demo_cube': 预热后 Created={0} Idle={1} Active={2}",
                cubePool.Created, cubePool.IdleCount, cubePool.ActiveCount);

            var positions = new[]
            {
                new Vector3(-1.2f, 0.4f, 2f),
                new Vector3(0f, 0.4f, 2f),
                new Vector3(1.2f, 0.4f, 2f),
            };
            for (int i = 0; i < positions.Length; i++)
            {
                GameObject cube = _pools.Spawn("demo_cube", positions[i]);
                _pools.DespawnAfter(cube, 3f); // 3 秒后自动回收（观察 Console 的 OnSpawn/OnDespawn 回调）
            }
            Log.Info("Demo", "出池 3 个方块（Game 视图可见），3 秒后自动回收 → 池状态 Created={0} Idle={1} Active={2}",
                cubePool.Created, cubePool.IdleCount, cubePool.ActiveCount);

            // 3) 强类型事件总线：发布 3 次，handler 收到 #2 时在广播中退订自己（演示广播安全）
            EventBus<DemoPingEvent>.Subscribe(OnPing);
            _time.Delay(0.5f, () => EventBus<DemoPingEvent>.Publish(new DemoPingEvent(1)));
            _time.Delay(1.0f, () => EventBus<DemoPingEvent>.Publish(new DemoPingEvent(2)));
            _time.Delay(1.5f, () => EventBus<DemoPingEvent>.Publish(new DemoPingEvent(3)));
            _time.Delay(2.0f, () =>
                Log.Info("Demo", "PingEvent 发布完毕（OnPing 已在广播中退订，第 3 次应无监听者日志）"));

            // 4) 音频系统演示（与池/事件演示错峰，约 6.5s）
            _time.Delay(2.2f, RunAudioSequence);

            // 5) UI 框架演示（音频演示结束、BGM 已淡出后开始；面板开合会自动带界面音）
            _time.Delay(9f, RunUiDemoSequence);
        }

        /// <summary>
        /// 配置表演示（阶段 3.1）：装配配置库 → 用生成的强类型入口读表 → 演示外键解析与缺项语义。
        ///
        /// 装配方式：正式项目在 Boot 场景挂 ConfigInstaller（Inspector 拖引用）；
        /// 演示走编辑器专用路径直接找资产（Template.Demo 是仅编辑器程序集），
        /// 免得依赖场景里手工摆物体 —— 运行期读表的 API 完全一致。
        /// </summary>
        private static void RunConfigDemo()
        {
            var database = AssetDatabase.LoadAssetAtPath<ConfigDatabase>(
                "Assets/_Project/Data/Configs/Generated/ConfigDatabase.asset");
            if (database == null)
            {
                Log.Warn("Demo", "配置库资产不存在：先跑一次菜单「Tools/配置表/全部导入」（或改一下 Data/Configs 下的 CSV）");
                return;
            }
            _config.SetDatabase(database);

            ItemConfig sword = Configs.Item.Require(1002);
            Log.Info("Demo", "配置读取：Configs.Item.Get(1002) → {0}（价格 {1}，攻击 {2}）",
                sword.Name, sword.Price, sword.Attack);

            // 外键解析演示（Enemy.dropItem → Item 表的行）需要第二张带外键的表；本仓库的示例数据里
            // 只剩 Item.csv（Enemy.csv 已被删除），所以这段跳过。想看外键解析：把一张带外键列
            // （列类型写目标表的行类型名，如 ItemConfig）的 CSV 放回 Data/Configs 再跑一次导入。

            Log.Info("Demo", "缺项语义：Configs.Item.Get(9999) = {0}（返回 null 不抛异常；要求必须存在时用 Require）",
                Configs.Item.Get(9999) == null ? "null" : "非 null ❌");
        }

        /// <summary>
        /// 相机演示（阶段 4.9）：Cinemachine 跟随（阻尼平滑）+ Trauma 震动（平方映射的手感差异）。
        /// 跟随目标是运行期建的方块（零资产）；参数走 <see cref="CameraFollowOptions.Default3D"/>。
        /// 正式项目不需要这样手动建 —— 场景里摆好角色、按一下 Follow 就行。
        /// </summary>
        private static void RunCameraDemo()
        {
            Log.Info("Demo", "── 相机演示：Cinemachine 跟随（看 Game 视图）+ Trauma 震动 ──");

            _cameraTarget = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cameraTarget.name = "DemoCameraTarget";
            _cameraTarget.transform.position = new Vector3(0f, 0.5f, 0f);

            _cameraFollow = _camera.Follow(_cameraTarget.transform, CameraFollowOptions.Default3D);
            Log.Info("Demo", "① 相机已开始跟随方块（Game 视图应平滑地对准它）");

            _time.Delay(1f, () =>
            {
                _cameraTarget.transform.position = new Vector3(4f, 0.5f, 0f);
                Log.Info("Demo", "② 方块瞬移到右侧 —— 相机应**平滑**跟过去（不是瞬移：阻尼生效）");
            });
            _time.Delay(2.2f, () =>
            {
                _camera.Shake(0.3f);
                Log.Info("Demo", "③ 轻受击震动：创伤 0.3 → 力度 0.09（平方映射，几乎只是抖一下）");
            });
            _time.Delay(3.2f, () =>
            {
                _camera.Shake(1f);
                Log.Info("Demo", "④ 大招震动：创伤 1.0 → 力度 3.00（明显更猛 —— 这就是 Trauma² 的意义）");
            });
            _time.Delay(4.6f, () =>
            {
                _camera.Stop(_cameraFollow);
                _cameraFollow = default;
                if (_cameraTarget != null)
                    Object.Destroy(_cameraTarget);
                _cameraTarget = null;
                Log.Info("Demo", "⑤ 演示结束：已停止跟随（相机停在原地），跟随目标已销毁");
            });
        }

        /// <summary>
        /// 特效演示（阶段 4.10）：一行播放 → 走对象池 → 播完自动回收 → 再播时复用同一个实例。
        /// 素材是运行期代码搭的粒子系统（零资产）；正式项目用 `Play(address, pos)` 走 Addressables 加载。
        /// </summary>
        private static void RunVfxDemo()
        {
            Log.Info("Demo", "── 特效演示：一行播放 → 对象池复用 → 播完自动回收 ──");

            if (_vfxPrefab == null)
                _vfxPrefab = DemoVfxFactory.CreateBurst(); // 出厂即未激活：模板本体不入画面，池里生成的是它的克隆

            for (int i = 0; i < 3; i++)
            {
                VfxHandle handle = _vfx.Play(_vfxPrefab, new Vector3(i * 1.5f - 1.5f, 0f, 0f));
                Log.Info("Demo", "① 播放特效 #{0}（Game 视图看三个爆点）：{1}",
                    i + 1, handle.IsValid ? "已生成 ✅" : "失败 ❌");
            }
            Log.Info("Demo", "当前活跃特效 {0} 个：\n{1}", _vfx.ActiveCount, _vfx.Dump());

            _time.Delay(1.5f, () =>
            {
                Log.Info("Demo", "② 1.5 秒后（粒子已播完）：活跃特效 {0} 个{1}", _vfx.ActiveCount,
                    _vfx.ActiveCount == 0 ? " —— 已自动回收 ✅" : " —— 有残留 ❌ 检查回收规则");

                VfxHandle again = _vfx.Play(_vfxPrefab, Vector3.zero);
                Log.Info("Demo", "③ 再播一个：{0}（上面回收进池的实例会被复用，不会再新建）",
                    again.IsValid ? "成功 ✅" : "失败 ❌");
            });
        }

        /// <summary>
        /// 资源演示（阶段 3.3）：Addressables 异步加载 → 看账本 → 按 Label 整组加载 → 整组释放。
        ///
        /// 需要先把示例资产标记为 Addressable：菜单 **Tools/资源/初始化 Addressables 演示分组**（一次性）。
        /// 没标记也不报错 —— 会提示你去点那个菜单（加载失败按"业务降级"处理，这正是本服务的设计语义）。
        /// 结束时会检查账本是否归零：**切关卡后账本应该干净**，这是防资源泄漏的第一道闸。
        /// </summary>
        private static async UniTaskVoid RunAssetDemoAsync()
        {
            Log.Info("Demo", "── 资源演示（Addressables）：异步加载 → 账本 → 按 Label 整组加载/释放 ──");

            // 没初始化过 Addressables 就直接提示，不去触发一堆运行时错误（免得验收时满屏红字分不清是谁的问题）
            if (UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.GetSettings(false) == null)
            {
                Log.Warn("Demo", "Addressables 尚未初始化：先跑一次菜单「Tools/资源/初始化 Addressables 演示分组」");
                return;
            }

            float start = Time.realtimeSinceStartup;
            var table = await _assets.LoadAsync<ItemTable>("ItemTable");
            if (table == null)
            {
                Log.Warn("Demo", "地址 'ItemTable' 加载不到：先在菜单跑一次「Tools/资源/初始化 Addressables 演示分组」");
                return;
            }
            Log.Info("Demo", "异步加载 <ItemTable> 成功：{0} 行，用时 {1:F0} ms（引用计数 1）",
                table.Count, (Time.realtimeSinceStartup - start) * 1000f);
            Log.Info("Demo", "账本：\n{0}", _assets.Dump());

            IList<Object> group = await _assets.LoadLabelAsync<Object>("demo_config");
            Log.Info("Demo", "按 Label 整组加载 <demo_config>：{0} 项（同一批资产只需一次 ReleaseLabel）", group.Count);
            Log.Info("Demo", "账本：\n{0}", _assets.Dump());

            _assets.ReleaseLabel("demo_config");
            _assets.Release("ItemTable");
            Log.Info("Demo", "整组释放 + 单资产释放后：{0} 项挂着{1}", _assets.LoadedCount,
                _assets.LoadedCount == 0 ? "（账本干净 ✅）" : "（有残留 ❌ 检查 Release 是否配对）");
        }

        /// <summary>
        /// 存档演示（阶段 3.2）：按真实顺序演示 —— **先读档**（若已有档）→ 存一档 → 故意改脏内存 → 再读回来。
        ///
        /// 先读后存很关键：
        /// <list type="bullet">
        /// <item>这是真实游戏的顺序（启动读档、进度变化再保存）；</item>
        /// <item>也才走得到**损坏降级**路径 —— 如果一上来就保存，坏档在被读之前就被覆盖了（"改坏的数据自己恢复了"就是这么来的）。</item>
        /// </list>
        /// 会在 persistentDataPath/saves 留下演示档，不需要可直接删该目录。
        /// </summary>
        private static void RunSaveDemo()
        {
            _save.RegisterSection(_inventory);
            Log.Info("Demo", "已注册存档分片：{0}（分片自己负责序列化自己的状态）", _inventory.Key);

            // ① 启动读档：槽位 0 已有档时读回来（主档被改坏会在这里打出"已回退备份档"）
            if (_save.SlotExists(0))
            {
                bool startupLoad = _save.Load(0, out string startupError);
                Log.Info("Demo", "启动读档{0}：{1}{2}", startupLoad ? "成功" : "失败", _inventory.Describe(),
                    string.IsNullOrEmpty(startupError) ? string.Empty : $"（{startupError}）");
            }
            else
            {
                Log.Info("Demo", "槽位 0 还没有存档（首次运行属正常）");
            }

            // ② 存一档
            _inventory.Reset();
            if (!_save.Save(0, "演示档 · 第 1 章"))
            {
                Log.Warn("Demo", "存档写入失败（磁盘/权限问题？）");
                return;
            }
            Log.Info("Demo", "存档写入后（内存）：{0}", _inventory.Describe());

            // ③ 故意改脏内存 → 读档必须被存档里的值覆盖
            _inventory.Gold = 999;
            _inventory.ItemIds.Clear();
            Log.Info("Demo", "故意改脏内存：{0}", _inventory.Describe());

            bool loaded = _save.Load(0, out string error);
            Log.Info("Demo", "读档{0}（内存已还原）：{1}{2}",
                loaded ? "成功" : "失败", _inventory.Describe(),
                string.IsNullOrEmpty(error) ? string.Empty : $"（{error}）");

            int[] slots = _save.ListSlots();
            Log.Info("Demo", "存档槽位：{0} 个 [{1}]", slots.Length, string.Join("、", slots));
            if (slots.Length > 0 && _save.TryGetSlotInfo(slots[0], out SaveSlotInfo info))
                Log.Info("Demo", "槽位概览：{0}", info);
        }

        /// <summary>
        /// 音频演示（约 6.5s）：BGM 淡入 → 2D/3D 音效 → 音量实时调节 → 换曲交叉淡变 →
        /// 语音单轨打断 → BGM 淡出。全部素材运行期合成，戴耳机/开扬声器直接听。
        /// </summary>
        private static void RunAudioSequence()
        {
            Log.Info("Demo", "── 音频演示开始：请开扬声器 —— BGM 淡入 / 2D·3D 音效 / 音量调节 / 交叉换曲 / 语音打断 ──");

            _bgmMain = DemoAudioClips.CreateLoopBgm();
            _bgmAlt = DemoAudioClips.CreateLoopBgm(); // 同一合成器，靠音高区分不了则听音色一致（正常）
            _sfxShot = DemoAudioClips.CreateShot();
            _sfxPing = DemoAudioClips.CreatePing();
            _voiceLine = DemoAudioClips.CreateVoiceLine(0);
            _voiceLine2 = DemoAudioClips.CreateVoiceLine(1); // 音高不同，便于听出"后一句盖掉前一句"

            _audio.PlayBgm(_bgmMain, fadeSeconds: 1.5f);
            Log.Info("Demo", "① BGM 起播：应有约 1.5 秒的淡入（音量从 0 升到 100%）");

            _time.Delay(2f, () =>
            {
                Log.Info("Demo", "② 2D 音效：发射声（左耳/右耳等响）");
                _audio.PlaySound(_sfxShot, volume: 0.8f);
            });

            _time.Delay(2.6f, () =>
            {
                Log.Info("Demo", "③ 3D 空间音：左前方 (-2,0,2) 与右前方 (2,0,2) 各响一声 —— 应能听出方位差异");
                _audio.PlaySoundAt(_sfxPing, new Vector3(-2f, 0f, 2f), volume: 1f);
                _audio.PlaySoundAt(_sfxPing, new Vector3(2f, 0f, 2f), volume: 1f);
            });

            _time.Delay(3.4f, () =>
            {
                Log.Info("Demo", "④ 音量调节：BGM 降到 30%（请听音量变化，随后回到 100%）");
                _audio.SetVolume(AudioBus.Bgm, 0.3f);
            });
            _time.Delay(4.2f, () =>
            {
                _audio.SetVolume(AudioBus.Bgm, 1f);
                Log.Info("Demo", "⑤ 交叉换曲：新曲淡入的同时旧曲淡出（曲间无静音缝）");
                _audio.PlayBgm(_bgmAlt, fadeSeconds: 1.2f, volume: 0.6f);
            });

            _time.Delay(5.2f, () =>
            {
                Log.Info("Demo", "⑥ 语音单轨：连播两句，第二句立即打断第一句（听到的应是后一句）");
                _audio.PlayVoice(_voiceLine, volume: 0.9f);
                _time.Delay(0.25f, () => _audio.PlayVoice(_voiceLine2, volume: 0.9f), TimerClock.Unscaled);
            });

            _time.Delay(6.2f, () =>
            {
                Log.Info("Demo", "⑦ BGM 淡出（0.8 秒），音频演示结束");
                _audio.StopBgm(0.8f);
            });
            _time.Delay(6.5f, () => Log.Info("Demo", "── 音频演示完成（随后进入 UI 演示，面板开合会自动带界面音）──"));
        }

        /// <summary>
        /// UI 演示序列（约 7s）：全屏页互斥（压栈隐藏/恢复）→ 弹窗遮罩 → Overlay 叠加 → 统一返回。
        /// 面板均为运行期纯代码构建的 uGUI，直接在 Game 视图观察。
        /// </summary>
        private static void RunUiDemoSequence()
        {
            Log.Info("Demo", "── UI 演示开始：主菜单(栈底) → 设置(压栈) → 确认弹窗(遮罩) → Toast(Overlay) ──");
            Log.Info("Demo", "（查看 Game 视图：全屏页互斥、弹窗遮罩变暗、Toast 浮在最上层）");
            Log.Info("Demo", "（同时可听：音频系统订阅面板开合事件，每次开/关面板都有界面音 —— 模块间事件联动）");

            UiPanel panelMainMenu = DemoPanelFactory.Build<DemoMainMenuScreen>("主菜单",
                "栈底页面 · 返回由业务决策", new Color(0.10f, 0.15f, 0.24f), new Vector2(660f, 380f));
            UiPanel panelSettings = DemoPanelFactory.Build<DemoSettingsScreen>("设置",
                "全屏页压栈 · 主菜单自动隐藏", new Color(0.16f, 0.13f, 0.10f), new Vector2(560f, 320f));
            UiPanel panelConfirm = DemoPanelFactory.Build<DemoConfirmPopup>("确认弹窗",
                "Popup 层 · 遮罩挡住下层点击", new Color(0.12f, 0.20f, 0.28f), new Vector2(480f, 260f));
            UiPanel panelToast = DemoPanelFactory.Build<DemoToastView>("Toast 悬浮提示",
                null, new Color(0f, 0f, 0f, 0.8f), new Vector2(600f, 64f));

            _time.Delay(0.1f, () => _ui.Open(panelMainMenu));
            _time.Delay(1.1f, () => _ui.Open(panelSettings));
            _time.Delay(2.1f, () => Log.Info("Demo", "TryBack（设置页在顶）→ {0}",
                _ui.TryBack() ? "true：关闭设置，主菜单 OnShown 恢复" : "false：未处理 ❌"));
            _time.Delay(3.1f, () => _ui.Open(panelConfirm));
            _time.Delay(3.9f, () =>
            {
                _ui.Open(panelToast);
                Log.Info("Demo", "Toast 已开（Overlay 层）：应浮在遮罩/弹窗之上，且不参与返回逻辑");
            });
            _time.Delay(4.7f, () => Log.Info("Demo", "TryBack（弹窗在顶）→ {0}",
                _ui.TryBack() ? "true：关闭弹窗，遮罩释放" : "false：未处理 ❌"));
            _time.Delay(5.7f, () => Log.Info("Demo", "TryBack（只剩栈底主菜单）→ false ⇒ 业务决策：回主流程/退出（此处演示结束 UI 收尾）"));
            _time.Delay(6.5f, () =>
            {
                _ui.CloseAll();
                // 实例已全部关闭回收，顺带销毁 prefab 模板本体
                Object.Destroy(panelMainMenu.gameObject);
                Object.Destroy(panelSettings.gameObject);
                Object.Destroy(panelConfirm.gameObject);
                Object.Destroy(panelToast.gameObject);
                Log.Info("Demo", "UI 演示完成（CloseAll 后 Screen={0} Popup={1}）", _ui.ScreenCount, _ui.PopupCount);
            });
            _time.Delay(7.1f, RunSettingsDemoSequence);
        }

        /// <summary>
        /// 设置演示（交互窗口，阶段 2.4）：主菜单 → 设置页两级页面（Screen 压栈语义），
        /// 期间可拖音量滑条；窗口结束打印设置摘要后进入 Loading。
        ///
        /// 持久化验收：本段开头会打印"启动时设置（上次运行保存的）"—— 改动后按 Stop 再 Play，
        /// 对照两次日志即可确认音量跨运行保留。
        /// </summary>
        private static void RunSettingsDemoSequence()
        {
            Log.Info("Demo", "── 设置演示开始（阶段 2.4）：音量滑条 —— 可交互 25 秒 ──");
            Log.Info("Demo", "（操作：拖动 4 条音量滑条 → 声音同步变化；按 Esc 关闭设置页回主菜单）");
            Log.Info("Demo", "（验证持久化：改完音量/键位后按 Stop 再 Play，开头会打印本次保存的值）");

            // 本段关闭事件 Verbose 日志：拖滑条会每帧发布设置变更事件，逐条打印会刷爆 Console
            EventBus.VerboseLogging = false;
            Log.Verbose("Demo", "设置演示期间：事件 Verbose 日志已暂时关闭（设置变更事件高频）");

            _settingsMenuBackdrop = DemoPanelFactory.Build<DemoMainMenuScreen>("主菜单",
                "设置页的返回目标 · 演示 Screen 压栈与恢复", new Color(0.10f, 0.15f, 0.24f), new Vector2(660f, 380f));
            _ui.Open(_settingsMenuBackdrop);

            _settingsPanel = DemoSettingsPanel.Create();
            _ui.Open(_settingsPanel);

            LogSettingsSnapshot("设置演示开始时");
            _time.Delay(25f, FinishSettingsDemo);
        }

        private static void FinishSettingsDemo()
        {
            EventBus.VerboseLogging = true; // 恢复事件日志（后续 Gameplay 段继续演示）
            LogSettingsSnapshot("设置演示结束时");
            _ui.CloseAll(); // 关掉设置页与主菜单（演示收尾）
            if (_settingsMenuBackdrop != null)
                Object.Destroy(_settingsMenuBackdrop.gameObject);
            if (_settingsPanel != null)
                Object.Destroy(_settingsPanel.gameObject);
            _settingsMenuBackdrop = null;
            _settingsPanel = null;

            RunDiagnosticsDemo(); // 阶段 4.11：设置演示之后留一段纯调试面板窗口，再进 Loading
        }

        /// <summary>
        /// 调试面板演示（阶段 4.11）：自动打开面板并留出 2 分钟交互窗口 ——
        /// 期间可反复按 F1 开关、点按钮（时间缩放/暂停/日志级别）、在输入框敲命令。
        /// 这是本阶段唯一需要**动手**的验收段（面板本身没法自动验证），窗口给足时间慢慢玩。
        /// </summary>
        private static void RunDiagnosticsDemo()
        {
            Log.Info("Demo", "── 调试面板演示（阶段 4.11）：面板已打开，接下来 2 分钟交给你 ──");
            Log.Info("Demo", "（试：按 F1 反复开关；点 0.5× 看时间变慢；点「日志」循环级别；输入框敲 help / dump / timescale 0.5）");
            Log.Info("Demo", "（演示命令：demo.shake 0.5 触发相机震动、demo.spawn 3 出池方块、demo.save 2 存档到 2 号槽）");
            Log.Info("Demo", "（2 分钟到点自动关面板进 Loading；期间什么都不做也没关系）");

            _diagnostics.Open();
            _diagnostics.Console.Execute("help"); // 顺手把命令列表显示在报告末尾（面板会自动滚到底）
            // 用 Unscaled：这段窗口里你可能正在试 0.25× 时间缩放，用 Scaled 会把 2 分钟拖成 8 分钟
            _time.Delay(DiagnosticsWindowSeconds, FinishDiagnosticsDemo, TimerClock.Unscaled);
        }

        /// <summary>调试面板交互窗口时长（秒）。</summary>
        private const float DiagnosticsWindowSeconds = 120f;

        private static void FinishDiagnosticsDemo()
        {
            FrameRateSampler sampler = _diagnostics.Sampler;
            Log.Info("Demo", "调试面板演示结束：{0} 个分节在线；本窗口帧率 FPS {1:F1}（平均帧 {2:F1}ms，最差 {3:F1}ms）",
                _diagnostics.SectionCount, sampler.Fps, sampler.AverageFrameMs, sampler.WorstFrameMs);
            _diagnostics.Close();
            _gameState.RequestTransition(GamePhase.Loading);
        }

        /// <summary>
        /// 调试命令示例 —— 注意这些命令定义在 **Demo 层**：模板只提供命令注册机制，
        /// "无敌 / 加钱 / 跳任务"这类玩法作弊属于具体游戏，由游戏自己的系统注册。
        /// </summary>
        private static void RegisterDiagnosticsCommands()
        {
            _diagnostics.Console.Register("demo.shake", "demo.shake [0~1] —— 触发相机震动（默认 0.5）",
                args =>
                {
                    float amount = args.Length > 0 && float.TryParse(args[0], out float parsed) ? parsed : 0.5f;
                    _camera.Shake(amount);
                    return "相机震动 " + amount.ToString("F2");
                });

            _diagnostics.Console.Register("demo.spawn", "demo.spawn [n] —— 从对象池出池 n 个方块（默认 3）",
                args =>
                {
                    int count = args.Length > 0 && int.TryParse(args[0], out int parsed) ? parsed : 3;
                    for (int i = 0; i < count; i++)
                    {
                        GameObject cube = _pools.Spawn("demo_cube", new Vector3(-1.2f + i * 1.2f, 0.4f, 2f));
                        if (cube != null)
                            _pools.DespawnAfter(cube, 3f);
                    }
                    return "出池 " + count + " 个方块（3 秒后自动回收）—— 看上面「对象池」分节的活跃数变化";
                });

            _diagnostics.Console.Register("demo.save", "demo.save [槽位] —— 存档到指定槽位（默认 0）",
                args =>
                {
                    int slot = args.Length > 0 && int.TryParse(args[0], out int parsed) ? parsed : 0;
                    bool ok = _save.Save(slot, "调试面板存的档");
                    return ok ? "已存档到槽位 " + slot + "（看「存档」分节）" : "存档失败（看 Console 红字）";
                });
        }

        /// <summary>打印当前设置摘要（音量）—— 跨 Play 持久化的验收凭据。</summary>
        private static void LogSettingsSnapshot(string title)
        {
            Log.Info("Demo", "{0}：音量 Master {1:P0} / BGM {2:P0} / SFX {3:P0} / Voice {4:P0}",
                title,
                _audio.GetVolume(AudioBus.Master), _audio.GetVolume(AudioBus.Bgm),
                _audio.GetVolume(AudioBus.Sfx), _audio.GetVolume(AudioBus.Voice));
        }

        private static void OnPing(DemoPingEvent e)
        {
            Log.Info("Demo", "PingEvent #{0} 被接收（当前监听者 {1}）", e.Id, EventBus<DemoPingEvent>.HandlerCount);
            if (e.Id == 2)
            {
                // 在事件分发（广播）过程中退订自己 —— 事件中心保证安全，后续监听者不受影响
                EventBus<DemoPingEvent>.Unsubscribe(OnPing);
                Log.Info("Demo", "→ OnPing 在广播中退订成功（无异常，总线未被破坏）");
            }
        }

        /// <summary>
        /// Gameplay（首次进入）：附加 Gameplay 输入映射，随即转入 Paused ——
        /// 验证暂停屏蔽：Paused 期间映射被 InputService 自动禁用（按空格无响应）。
        /// </summary>
        private static void RunGameplayDemo()
        {
            Log.Info("Demo", "── Gameplay：输入演示（附加 Fire 映射）+ 暂停演示（冻结缩放时钟）──");

            // Gameplay 映射由游戏层构建并附加；启停与暂停屏蔽归 InputService 管
            _fireMap = new InputActionMap("Gameplay_Demo");
            InputAction fire = _fireMap.AddAction("Fire", InputActionType.Button);
            fire.AddBinding("<Keyboard>/space");
            fire.AddBinding("<Gamepad>/buttonSouth");
            fire.performed += OnFirePerformed;
            _input.AttachMap(_fireMap);

            Log.Info("Demo", "（即将转入暂停：暂停期间按空格应无任何日志 —— 映射被自动禁用即为预期）");
            _gameState.RequestTransition(GamePhase.Paused);
        }

        private static void OnFirePerformed(InputAction.CallbackContext context)
        {
            Log.Info("Demo", "[输入] 开火！设备 <{0}>（并播放 3D 枪声）",
                context.control != null ? context.control.device.displayName : "(空)");
            if (_sfxShot != null)
                _audio.PlaySoundAt(_sfxShot, new Vector3(-2f, 0f, 2f), volume: 0.9f,
                    pitch: UnityEngine.Random.Range(0.95f, 1.05f));
        }

        private static void RunPauseDemo()
        {
            Log.Info("Demo", "── Paused：时间已冻结（timeScale=0）。以下对比 Scaled 与 Unscaled 计时器 ──");
            Log.Info("Demo", "（输入屏蔽生效中：此时按空格不会出现开火日志；1 秒后恢复将自动重新启用）");
            // 依据触发时是否仍处于暂停判断结果：暂停中触发 = 冻结失效；解冻后才触发 = 冻结生效
            _time.Delay(0.5f, () =>
            {
                bool duringPause = _gameState.Current == GamePhase.Paused;
                Log.Info("Demo", duringPause
                    ? "[scaled]   0.5s 定时器在暂停中触发 —— 冻结失效 ❌"
                    : "[scaled]   0.5s 定时器在解冻后才补触发 —— 暂停期间被冻结，符合预期 ✅");
            }, TimerClock.Scaled);
            _time.Delay(0.5f, () => Log.Info("Demo", "[unscaled] 0.5s 定时器触发 —— 暂停中正常工作 ✅"), TimerClock.Unscaled);
            _time.Delay(1f, () => Log.Info("Demo", "[unscaled] 1.0s 到达，恢复游戏"), TimerClock.Unscaled);
            _time.Delay(1f, () => _gameState.RequestTransition(GamePhase.Gameplay), TimerClock.Unscaled);
        }

        private static void FinishDemo()
        {
            // 输入演示收尾：退订动作回调、移除映射（禁用由 InputService 负责）
            if (_fireMap != null)
            {
                _fireMap.FindAction("Fire").performed -= OnFirePerformed;
                _input.DetachMap(_fireMap);
                _fireMap = null;
            }
            EventBus<InputSchemeChangedEvent>.Unsubscribe(OnInputSchemeChanged);

            // 音频收尾：停 BGM 并显式回收演示生成的 AudioClip（运行期资源不受场景卸载自动清理）
            StopDemoAudio();

            // 特效收尾：回收全部特效实例 + 销毁演示用特效模板
            _vfx.StopAll();
            if (_vfxPrefab != null)
            {
                Object.Destroy(_vfxPrefab);
                _vfxPrefab = null;
            }

            // 相机收尾：停掉跟随相机 + 清理演示目标（先停相机再销毁目标，避免追已销毁对象）
            _camera.StopAll();
            if (_cameraTarget != null)
            {
                Object.Destroy(_cameraTarget);
                _cameraTarget = null;
            }
            _cameraFollow = default;

            Log.Info("Demo", "═══ 演示完成（阶段 1 核心循环 + 2.1 UI 框架 + 2.2 输入系统 + 2.3 音频系统 + 2.4 设置系统 + 3.1 配置表管线 + 3.2 存档系统 + 3.3 资源系统 + 4.9 相机系统 + 4.10 特效系统 + 4.11 调试面板）═══");
            Log.Info("Demo", "场景流转就绪：当前场景 '{0}'；Build Settings 共 {1} 个场景。" +
                "（真实切换演示：Build Settings 添加第 2 个场景后调用 SceneFlowService.LoadScene）",
                SceneManager.GetActiveScene().name, SceneManager.sceneCountInBuildSettings);

            // 清理演示资源
            EventBus<GamePhaseChangedEvent>.Unsubscribe(OnPhaseChanged);
            EventBus<DemoPingEvent>.Unsubscribe(OnPing);
            EventBus.VerboseLogging = false;
            _pools.ClearPool("demo_cube");
            if (_cubeTemplate != null)
                Object.Destroy(_cubeTemplate);
        }

        /// <summary>停止背景音乐并销毁运行期合成的演示音频素材（正式项目 clip 由 Addressables 管释放）。</summary>
        private static void StopDemoAudio()
        {
            _audio.StopAll(); // 先停播并解绑 clip：随后才能安全销毁这些运行期合成的素材
            DestroyClip(ref _bgmMain);
            DestroyClip(ref _bgmAlt);
            DestroyClip(ref _sfxShot);
            DestroyClip(ref _sfxPing);
            DestroyClip(ref _voiceLine);
            DestroyClip(ref _voiceLine2);
        }

        private static void DestroyClip(ref AudioClip clip)
        {
            if (clip != null)
                Object.Destroy(clip);
            clip = null;
        }

        /// <summary>
        /// Gameplay（恢复进入）：输入验收窗口（3.5 秒）——
        /// 打开一个弹窗作为 Esc 靶子（Cancel → TryBack 联动），期间可自由按空格开火；
        /// 窗口结束时自动判定：弹窗被关掉 = 联动通过；仍开着 = 未收到取消键（需反馈日志）。
        /// </summary>
        private static void RunPostPauseDemo()
        {
            Log.Info("Demo", "── Gameplay（恢复）：Fire 映射已自动重新启用 ──");
            Log.Info("Demo", "── 输入验收窗口（3.5 秒）：请按【Esc】关闭中央弹窗；期间可按【空格】开火（听到 3D 枪声）──");

            _escTarget = DemoPanelFactory.Build<DemoConfirmPopup>("按 Esc 关闭我",
                "Cancel → TryBack（未操作则 3.5 秒后自动关闭）", new Color(0.12f, 0.20f, 0.28f), new Vector2(480f, 260f));
            _escInstance = _ui.Open(_escTarget); // ★ 存返回值：栈里的是这个克隆体，不是模板

            // 空间音验收：开窗同时左右各响一声（弹窗自带界面音，此处补充方位判定素材）
            if (_sfxPing != null)
            {
                _audio.PlaySoundAt(_sfxPing, new Vector3(-3f, 0f, 3f), volume: 1f);
                _time.Delay(0.35f, () => _audio.PlaySoundAt(_sfxPing, new Vector3(3f, 0f, 3f), volume: 1f),
                    TimerClock.Unscaled);
            }

            _time.Delay(3.5f, () =>
            {
                // 判断依据必须是**活实例**：模板的 IsOpen 永远是 false（它没进过栈）——
                // 用模板判断会让这个验收恒为"通过"，且弹窗+遮罩会一直留在画面上。
                bool closedByEsc = _escInstance == null || !_escInstance.IsOpen;
                if (closedByEsc)
                {
                    Log.Info("Demo", "✓ Esc 联动验证通过：弹窗已被取消键关闭（Cancel → TryBack）");
                }
                else
                {
                    Log.Warn("Demo", "Esc 验收窗口结束但弹窗仍开着 —— 若你按了 Esc 却没反应，请把本页 Console 日志反馈（链路问题）");
                }

                if (_escInstance != null && _escInstance.IsOpen)
                    _ui.Close(_escInstance);                // 收尾：不留弹窗与遮罩
                if (_escTarget != null)
                    Object.Destroy(_escTarget.gameObject);  // 销毁 prefab 模板本体
                _escInstance = null;
                _escTarget = null;
                FinishDemo();
            });
        }

        private static void OnInputSchemeChanged(InputSchemeChangedEvent e)
        {
            string name = e.Scheme == InputScheme.KeyboardMouse ? "键鼠"
                : e.Scheme == InputScheme.Gamepad ? "手柄"
                : e.Scheme == InputScheme.Touch ? "触摸" : "未知";
            Log.Info("Demo", "输入方案检测 → {0}（UI 可订阅 InputSchemeChangedEvent 切换按键提示）", name);
        }
    }

    /// <summary>池化演示组件：展示 IPoolable 生命周期回调。</summary>
    public sealed class DemoCubePoolable : MonoBehaviour, IPoolable
    {
        public void OnSpawn()
        {
            Log.Verbose("Demo", "方块出池: {0}", name);
        }

        public void OnDespawn()
        {
            Log.Verbose("Demo", "方块归池（状态已重置）: {0}", name);
        }
    }
}
