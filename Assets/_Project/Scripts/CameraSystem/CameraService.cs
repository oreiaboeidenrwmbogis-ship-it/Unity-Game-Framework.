using System.Collections.Generic;
using Cinemachine;
using Template.Core.Logging;
using Template.Core.Services;
using UnityEngine;

namespace Template.CameraSystem
{
    /// <summary>跟随相机的句柄（对业务是黑盒：只管拿着它去 <see cref="CameraService.Stop"/>）。</summary>
    public readonly struct CameraHandle
    {
        internal readonly CinemachineVirtualCamera Vcam;

        internal CameraHandle(CinemachineVirtualCamera vcam) => Vcam = vcam;

        public bool IsValid => Vcam != null;

        public static CameraHandle Invalid => default;
    }

    /// <summary>
    /// 相机服务：**薄封装 Cinemachine**（路线图 4.9 的要求："不要自研相机系统"）——
    /// 跟随 / 混合 / 边界 / 多机位交给 Cinemachine，本类只做四件事：
    /// <list type="number">
    /// <item>主相机与 Brain 的托管（运行期代码构建，零资产：不需要在场景里手工摆 VCam）；</item>
    /// <item>用 <see cref="CameraFollowOptions"/> 一行创建跟随相机（3D 走 Transposer、2D 走 FramingTransposer）；</item>
    /// <item>屏幕震动：**Trauma 模型**（<see cref="CameraTrauma"/> 累加+衰减+平方映射）→ 驱动 Cinemachine Impulse；</item>
    /// <item>机位切换：优先级（新创建的默认覆盖旧的；也可显式指定 <c>Priority</c>）。</item>
    /// </list>
    ///
    /// 用法：
    /// <code>
    /// var cam = ServiceLocator.Get&lt;CameraService&gt;();
    /// CameraHandle follow = cam.Follow(player.transform, CameraFollowOptions.Default3D);  // 跟随
    /// cam.Shake(0.4f);                                                                   // 受击震动
    /// cam.Stop(follow);                                                                  // 取消跟随
    /// </code>
    ///
    /// 注意：主相机必须带 <c>MainCamera</c> 标签（Unity 的 <c>Camera.main</c> 依赖它）；
    /// VCam 都建在常驻根 <c>[CameraRig]</c> 下，跨场景不丢。
    /// </summary>
    public sealed class CameraService : IGameService, ITickable
    {
        /// <summary>脉冲频道（源头与监听器必须一致，否则震动传不过去）。</summary>
        private const int ImpulseChannel = 1;

        private readonly CameraTrauma _trauma = new CameraTrauma();
        private readonly List<CameraHandle> _cameras = new List<CameraHandle>();

        private GameObject _rig;
        private CinemachineImpulseSource _impulse;
        private Camera _mainCamera;
        private int _autoPriority = 10;
        private bool _missingCameraWarned; // 主相机缺失只告警一次（避免刷屏）

        /// <summary>当前创伤值（0~1）。</summary>
        public float Trauma => _trauma.Value;

        /// <summary>当前主相机（可能为 null：场景里没有 MainCamera 标签的相机）。</summary>
        public Camera MainCamera => _mainCamera;

        /// <summary>当前由本服务创建的跟随相机数量。</summary>
        public int CameraCount => _cameras.Count;

        public void Init()
        {
            // 只建常驻根与震动源 —— **不要在这里找主相机**：
            // Init 发生在 Bootstrap.Awake（BeforeSceneLoad），此刻场景还没加载，Camera.main 必然是 null。
            // 主相机在首次使用（Follow / Shake）时就近接管，见 EnsureBrain。
            EnsureRig();
            Log.Info("Camera", "相机服务就绪：Cinemachine 虚拟相机 + Trauma 震动（主相机在首次使用时就近接管）");
        }

        public void Dispose()
        {
            _cameras.Clear();
            if (_rig != null)
            {
                Object.Destroy(_rig);
                _rig = null;
                _impulse = null;
            }
        }

        /// <summary>创伤按秒衰减。用**缩放时间**：暂停时创伤与画面一起冻结（符合直觉）。</summary>
        public void Tick() => _trauma.Decay(Time.deltaTime);

        // ── 震动 ──

        /// <summary>
        /// 触发一次震动。量级参考：轻受击 0.2~0.3、爆炸 0.6、大招/坍塌 1.0。
        /// 力度按平方映射（<see cref="CameraTrauma.ForceOf"/>）：0.3 只有 0.09 的力度，1.0 才是满力。
        /// </summary>
        public void Shake(float amount)
        {
            EnsureRig();
            if (_impulse == null)
                return;

            float trauma = _trauma.Add(Mathf.Clamp01(amount));
            _impulse.GenerateImpulseWithForce(_trauma.ForceOf(trauma));
            Log.Verbose("Camera", "震动：创伤 {0:P0}（力度 {1:F2}）", trauma, _trauma.ForceOf(trauma));
        }

        /// <summary>清空创伤（例如过场/切场景时）。</summary>
        public void ClearTrauma() => _trauma.Reset();

        // ── 跟随 ──

        /// <summary>创建一台跟随相机（不传参数用 3D 默认值）。返回句柄，用 <see cref="Stop"/> 取消。</summary>
        public CameraHandle Follow(Transform target, CameraFollowOptions options = default)
        {
            if (target == null)
            {
                Log.Error("Camera", "Follow：跟随目标为空");
                return CameraHandle.Invalid;
            }
            EnsureRig();
            if (!EnsureBrain())
                return CameraHandle.Invalid;
            if (options.FieldOfView <= 0f)
                options = CameraFollowOptions.Default3D; // 传了 default 结构体 → 用 3D 默认

            var go = new GameObject("VCam_" + target.name + "_" + _cameras.Count);
            go.transform.SetParent(_rig.transform, false);
            var vcam = go.AddComponent<CinemachineVirtualCamera>();

            vcam.Priority = options.Priority > 0 ? options.Priority : ++_autoPriority; // 后创建的默认接管
            vcam.Follow = target;
            vcam.LookAt = target;
            ApplyLens(vcam, options);
            ApplyBody(vcam, options);
            vcam.AddCinemachineComponent<CinemachineComposer>(); // Aim：始终看向目标

            var listener = go.AddComponent<CinemachineImpulseListener>();
            listener.m_ChannelMask = ImpulseChannel; // 与震动源同频道
            listener.m_Gain = 1f;                    // ⚠ 默认 0（同样只有编辑器 Reset 才置 1）：不设的话监听端把震动乘成 0
            listener.m_UseCameraSpace = true;        // 与 Cinemachine Reset() 一致：按相机空间抖，而不是世界空间

            var handle = new CameraHandle(vcam);
            _cameras.Add(handle);
            Log.Info("Camera", "相机已跟随 <{0}>（{1}，优先级 {2}）",
                target.name, options.Orthographic ? "2D 正交 + FramingTransposer" : "3D 透视 + Transposer", vcam.Priority);
            return handle;
        }

        /// <summary>停止并销毁一台跟随相机（主相机保持当前位置）。</summary>
        public bool Stop(CameraHandle handle)
        {
            if (!handle.IsValid)
                return false;

            _cameras.Remove(handle);
            Object.Destroy(handle.Vcam.gameObject);
            Log.Info("Camera", "已停止跟随相机 <{0}>（剩 {1} 台）", handle.Vcam.name, _cameras.Count);
            return true;
        }

        /// <summary>停止全部跟随相机（切场景/退出兜底）。</summary>
        public void StopAll()
        {
            for (int i = 0; i < _cameras.Count; i++)
            {
                if (_cameras[i].IsValid)
                    Object.Destroy(_cameras[i].Vcam.gameObject);
            }
            if (_cameras.Count > 0)
                Log.Info("Camera", "已停止全部跟随相机：{0} 台", _cameras.Count);
            _cameras.Clear();
        }

        // ── 内部 ──

        /// <summary>建常驻根与震动源（不依赖场景，Init 阶段即可调用）。</summary>
        private void EnsureRig()
        {
            if (_rig != null)
                return;

            _rig = new GameObject("[CameraRig]");
            Object.DontDestroyOnLoad(_rig);

            _impulse = _rig.AddComponent<CinemachineImpulseSource>();

            // ⚠ 这里必须显式配置：Cinemachine 的"可用默认值"只有编辑器里的 Reset() 才会填，
            //    而运行期 AddComponent 拿到的是**字段默认值**，那样震动完全无效：
            //      · 默认 m_ImpulseType = Legacy 且 m_RawSignal 为空 → 事件根本创建不出来（返回 null）；
            //      · 即便切成 Uniform，默认 m_ImpulseShape = Custom 且曲线为空 → 振幅恒为 0。
            //    所以下面按 Cinemachine 自己 Reset() 的等价配置写一遍（渠道/时长沿用本服务的参数）。
            _impulse.m_ImpulseDefinition.m_ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            _impulse.m_ImpulseDefinition.m_ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            _impulse.m_ImpulseDefinition.m_ImpulseChannel = ImpulseChannel;
            _impulse.m_ImpulseDefinition.m_ImpulseDuration = 0.35f;
        }

        /// <summary>
        /// 就近接管主相机（懒解析：必须等场景加载完才能找到 Camera.main）+ 确保它挂了 CinemachineBrain。
        /// 找不到带 <c>MainCamera</c> 标签的相机时只告警**一次**（免得刷屏），返回 false。
        /// </summary>
        private bool EnsureBrain()
        {
            if (_mainCamera == null)
                _mainCamera = Camera.main;

            if (_mainCamera == null)
            {
                if (!_missingCameraWarned)
                {
                    _missingCameraWarned = true;
                    Log.Error("Camera", "没找到带 MainCamera 标签的相机 —— 相机服务无法接管（Cinemachine 需要一个真实相机）");
                }
                return false;
            }

            if (_mainCamera.GetComponent<CinemachineBrain>() == null)
            {
                _mainCamera.gameObject.AddComponent<CinemachineBrain>();
                Log.Info("Camera", "已接管主相机 <{0}>（代码构建 CinemachineBrain，无需手工配置）", _mainCamera.name);
            }
            return true;
        }

        private static void ApplyLens(CinemachineVirtualCamera vcam, CameraFollowOptions options)
        {
            vcam.m_Lens.FieldOfView = options.FieldOfView;
            vcam.m_Lens.OrthographicSize = options.OrthographicSize;
            // 注意：LensSettings.Orthographic 的 setter 已标记过时，官方要求改用 ModeOverride
            vcam.m_Lens.ModeOverride = options.Orthographic
                ? LensSettings.OverrideModes.Orthographic
                : LensSettings.OverrideModes.Perspective;
        }

        private static void ApplyBody(CinemachineVirtualCamera vcam, CameraFollowOptions options)
        {
            if (options.Orthographic)
            {
                var framing = vcam.AddCinemachineComponent<CinemachineFramingTransposer>();
                framing.m_CameraDistance = Mathf.Max(0.01f, Mathf.Abs(options.Offset.z));
                framing.m_ScreenX = options.ScreenX;
                framing.m_ScreenY = options.ScreenY;
                framing.m_DeadZoneWidth = options.DeadZone.x;
                framing.m_DeadZoneHeight = options.DeadZone.y;
                framing.m_XDamping = options.Damping;
                framing.m_YDamping = options.Damping;
                return;
            }

            var transposer = vcam.AddCinemachineComponent<CinemachineTransposer>();
            transposer.m_FollowOffset = options.Offset;
            transposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTargetWithWorldUp; // 视角不随目标翻滚
            transposer.m_XDamping = options.Damping;
            transposer.m_YDamping = options.Damping;
            transposer.m_ZDamping = options.Damping;
        }
    }
}
