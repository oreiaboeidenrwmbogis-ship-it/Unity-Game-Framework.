namespace Template.Core.App
{
    /// <summary>
    /// 游戏全局阶段（进程级应用状态机）。
    /// 记忆：场景 ≠ 游戏状态 —— 主菜单/设置/背包是同一场景下 UI 栈的不同状态；
    /// Loading 是真实阶段（由场景流转配合推进），Paused 也是阶段不是场景。
    /// </summary>
    public enum GamePhase
    {
        Boot = 0,     // 启动
        Splash = 1,   // Logo 过渡
        MainMenu = 2, // 主菜单
        Loading = 3,  // 场景加载中
        Gameplay = 4, // 游戏进行
        Paused = 5,   // 暂停（冻结缩放时钟）
        GameOver = 6, // 结算/失败
    }
}
