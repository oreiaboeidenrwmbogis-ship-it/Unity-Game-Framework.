namespace Template.Core.App
{
    /// <summary>全局阶段切换事件（由 GameStateService 发布）。</summary>
    public struct GamePhaseChangedEvent
    {
        public GamePhase Previous;
        public GamePhase Current;

        public GamePhaseChangedEvent(GamePhase previous, GamePhase current)
        {
            Previous = previous;
            Current = current;
        }
    }

    /// <summary>
    /// 场景加载完成事件（由 SceneFlowService 发布）。
    /// 场景初始化代码在此消费 Context（如“读档进入、出生点、携带数据”），而不是去全局翻变量。
    /// </summary>
    public struct SceneLoadedEvent
    {
        public string SceneName;
        public SceneContext Context;

        public SceneLoadedEvent(string sceneName, SceneContext context)
        {
            SceneName = sceneName;
            Context = context;
        }
    }
}
