using System.IO;
using Template.Core.Eventing;
using Template.Core.Logging;
using Template.Core.Services;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Template.Core.App
{
    /// <summary>
    /// 场景流转服务：异步加载/卸载（Single 模式自动卸载旧场景）、加载进度查询、
    /// 参数注入（SceneContext）与加载完成广播（SceneLoadedEvent）。
    ///
    /// 当前基于 UnityEngine.SceneManagement 直接实现（不引第三方包即可工作）；
    /// 阶段 3 接入 Addressables 后重写内部加载实现即可 —— 对外 API 保持不变。
    ///
    /// 与游戏状态机的配合（由游戏层/流程代码驱动，本服务保持独立）：
    ///     RequestTransition(Loading) → LoadScene(name, ctx)
    ///     → 收到 SceneLoadedEvent → RequestTransition(目标阶段)
    ///
    /// 注意：运行时只能加载 Build Settings 中的场景（编辑器里 Play 也如此），
    /// 加载前会校验并给出明确错误。
    /// </summary>
    public sealed class SceneFlowService : IGameService
    {
        private AsyncOperation _operation;
        private SceneContext _lastContext;
        private string _loadingSceneName;

        /// <summary>是否有加载任务进行中（同一时刻只允许一个，重复请求会被拒绝）。</summary>
        public bool IsLoading => _operation != null;

        /// <summary>当前加载进度 0~1（无任务时为 1）。UI 轮询或订阅场景事件即可驱动进度条。</summary>
        public float LoadProgress => _operation != null ? _operation.progress : 1f;

        /// <summary>正在加载的场景名（无任务时为 null）。</summary>
        public string LoadingSceneName => _loadingSceneName;

        /// <summary>最近一次加载的上下文（保留至下一次 LoadScene）。</summary>
        public SceneContext LastContext => _lastContext;

        public void Init() { }

        public void Dispose()
        {
            _operation = null;
            _lastContext = null;
        }

        /// <summary>
        /// 请求加载场景。加载完成时发布 <see cref="SceneLoadedEvent"/>（含 Context）。
        /// 失败场景（未在 Build Settings / 名为空 / 已有任务）会明确报错并拒绝，不会静默失败。
        /// </summary>
        public void LoadScene(string sceneName, SceneContext context = null)
        {
            if (IsLoading)
            {
                Log.Error("Scene", "已在加载 '{0}'，拒绝新的加载请求 '{1}'", _loadingSceneName, sceneName);
                return;
            }
            if (string.IsNullOrEmpty(sceneName))
            {
                Log.Error("Scene", "LoadScene: 场景名为空");
                return;
            }
            if (!IsSceneInBuild(sceneName))
            {
                Log.Error("Scene",
                    "场景 '{0}' 不在 Build Settings 中，无法加载。请在 File → Build Settings 中添加该场景", sceneName);
                return;
            }

            _loadingSceneName = sceneName;
            _lastContext = context;
            Log.Info("Scene", "开始加载场景 '{0}'", sceneName);

            _operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (_operation == null)
            {
                Log.Error("Scene", "LoadSceneAsync 返回 null（场景名无效：'{0}'）", sceneName);
                _loadingSceneName = null;
                return;
            }
            _operation.completed += OnLoadCompleted;
        }

        private void OnLoadCompleted(AsyncOperation operation)
        {
            _operation = null;
            string sceneName = _loadingSceneName;
            _loadingSceneName = null;

            Log.Info("Scene", "场景 '{0}' 加载完成", sceneName);
            EventBus<SceneLoadedEvent>.Publish(new SceneLoadedEvent(sceneName, _lastContext));
            // _lastContext 保留到下一次 LoadScene：场景初始化器通常在事件中同步消费，无需清理
        }

        /// <summary>检查场景（按名字，不含扩展名）是否在 Build Settings 中。</summary>
        public static bool IsSceneInBuild(string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(path))
                    continue;
                if (Path.GetFileNameWithoutExtension(path) == sceneName)
                    return true;
            }
            return false;
        }
    }
}
