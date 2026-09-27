using UnityEngine;

namespace Template.CameraSystem
{
    /// <summary>
    /// 跟随相机的参数（值类型；用 <see cref="Default3D"/> / <see cref="Default2D"/> 起手，按需改字段）。
    ///
    /// 3D / 2D 走不同的 Cinemachine 跟随组件：
    /// <list type="bullet">
    /// <item>3D（透视）→ <c>CinemachineTransposer</c>：按世界偏移跟随 + 阻尼，视角稳定不翻滚；</item>
    /// <item>2D（正交）→ <c>CinemachineFramingTransposer</c>：按屏幕位置 + 死区跟随（2D 的标准做法）。</item>
    /// </list>
    /// </summary>
    public struct CameraFollowOptions
    {
        /// <summary>相对目标的偏移：3D 用它当相机落点；2D 只用 z 的绝对值当距离。</summary>
        public Vector3 Offset;

        /// <summary>阻尼（越大越顺滑；0 = 硬跟随）。</summary>
        public float Damping;

        /// <summary>透视视场角（3D 用）。</summary>
        public float FieldOfView;

        /// <summary>正交相机（2D 游戏设 true）。</summary>
        public bool Orthographic;

        /// <summary>正交尺寸（2D 用；约等于屏幕半高对应的世界单位）。</summary>
        public float OrthographicSize;

        /// <summary>2D：目标在屏幕上的位置（0.5 = 居中）。</summary>
        public float ScreenX;
        public float ScreenY;

        /// <summary>2D：死区大小（目标在这个范围内移动时相机不动，避免持续微抖）。</summary>
        public Vector2 DeadZone;

        /// <summary>优先级：0 = 自动递增（后创建的覆盖先创建的）；要精确控制多机位就显式指定。</summary>
        public int Priority;

        /// <summary>3D 跟随（第三人称视角）默认值。</summary>
        public static readonly CameraFollowOptions Default3D = new CameraFollowOptions
        {
            Offset = new Vector3(0f, 1.5f, -6f),
            Damping = 1f,
            FieldOfView = 60f,
            Orthographic = false,
            OrthographicSize = 5f,
            ScreenX = 0.5f,
            ScreenY = 0.5f,
            DeadZone = Vector2.zero,
            Priority = 0,
        };

        /// <summary>2D 跟随（横版/俯视）默认值。</summary>
        public static readonly CameraFollowOptions Default2D = new CameraFollowOptions
        {
            Offset = new Vector3(0f, 0f, -10f),
            Damping = 1f,
            FieldOfView = 60f,
            Orthographic = true,
            OrthographicSize = 5f,
            ScreenX = 0.5f,
            ScreenY = 0.5f,
            DeadZone = new Vector2(1f, 1f),
            Priority = 0,
        };
    }
}
