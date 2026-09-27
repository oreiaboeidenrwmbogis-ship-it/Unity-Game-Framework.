using System.Collections.Generic;
using Template.Core.Eventing;
using Template.Core.Logging;
using Template.Core.Services;
using Template.Core.Timing;

namespace Template.Core.App
{
    /// <summary>
    /// 游戏全局状态服务（进程级唯一，注册顺序在 Bootstrap 中管理）。
    ///
    /// 职责：
    /// 1. GamePhase 合法流转：非法转移被拒绝并 Error 日志（状态机绝不进入死角）；
    /// 2. 切换副作用集中管理：进入 Paused → 冻结缩放时钟（TimeService.Freeze）；
    ///    离开 Paused → 恢复；
    /// 3. 每次切换发布 EventBus&lt;GamePhaseChangedEvent&gt; —— 音频/输入/任务等系统
    ///    订阅该事件各自响应，GameStateService 不认识任何具体系统。
    ///
    /// 场景加载的配合模式（游戏层驱动，本服务不管场景）：
    ///    请求 Loading → SceneFlowService.LoadScene(...) → 收到 SceneLoadedEvent → 请求目标阶段。
    /// </summary>
    public sealed class GameStateService : IGameService
    {
        // 合法转移表（邻接表）。扩展新阶段时同步维护这里 —— 非法转移会在这里被拦截并报错。
        private static readonly Dictionary<GamePhase, GamePhase[]> AllowedTransitions =
            new Dictionary<GamePhase, GamePhase[]>
            {
                [GamePhase.Boot] = new[] { GamePhase.Splash },
                [GamePhase.Splash] = new[] { GamePhase.MainMenu },
                [GamePhase.MainMenu] = new[] { GamePhase.Loading },
                [GamePhase.Loading] = new[] { GamePhase.Gameplay, GamePhase.MainMenu }, // 加载失败可退回主菜单
                [GamePhase.Gameplay] = new[] { GamePhase.Paused, GamePhase.MainMenu, GamePhase.GameOver },
                [GamePhase.Paused] = new[] { GamePhase.Gameplay, GamePhase.MainMenu },
                [GamePhase.GameOver] = new[] { GamePhase.MainMenu, GamePhase.Gameplay },
            };

        private TimeService _time;
        private GamePhase _current = GamePhase.Boot;

        public GamePhase Current => _current;

        /// <summary>是否处于玩法阶段（含暂停 —— 暂停只是 Gameplay 的冻结态）。</summary>
        public bool IsInGame => _current == GamePhase.Gameplay || _current == GamePhase.Paused;

        public void Init()
        {
            _time = ServiceLocator.Get<TimeService>();
        }

        public void Dispose()
        {
            // 退出时若处于暂停，恢复时间缩放，避免编辑器遗留冻结状态
            if (_current == GamePhase.Paused)
                _time.Unfreeze();
        }

        /// <summary>
        /// 请求转移。非法转移输出 Error 并忽略；目标等于当前阶段时直接返回。
        /// </summary>
        public void RequestTransition(GamePhase target)
        {
            if (target == _current)
                return;
            if (!IsAllowed(_current, target))
            {
                Log.Error("GameState", "非法状态转移 {0} → {1} 被拒绝（请检查 GameStateService 合法转移表）",
                    _current, target);
                return;
            }
            ApplyTransition(target);
        }

        private void ApplyTransition(GamePhase next)
        {
            GamePhase previous = _current;

            // 阶段副作用（进入/离开时的框架级行为）
            if (next == GamePhase.Paused)
                _time.Freeze();
            else if (previous == GamePhase.Paused)
                _time.Unfreeze();

            _current = next;
            Log.Info("GameState", "阶段 {0} → {1}", previous, next);
            EventBus<GamePhaseChangedEvent>.Publish(new GamePhaseChangedEvent(previous, next));
        }

        private static bool IsAllowed(GamePhase from, GamePhase to)
        {
            if (!AllowedTransitions.TryGetValue(from, out GamePhase[] targets))
                return false;
            return System.Array.IndexOf(targets, to) >= 0;
        }
    }
}
