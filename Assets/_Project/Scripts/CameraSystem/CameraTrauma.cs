using UnityEngine;

namespace Template.CameraSystem
{
    /// <summary>
    /// Trauma 震动模型（**纯逻辑，可单测**）。
    ///
    /// 做法：把"连续受击/爆炸"累加成一个 0~1 的创伤值（Trauma），按秒衰减；
    /// 输出力度按**平方映射**（`force = max × trauma²`）—— 小创伤几乎不晃、大创伤很猛，
    /// 这是 Trauma 模型手感好的关键（线性映射会让小事件也显得很夸张）。
    ///
    /// 真正的画面抖动交给 Cinemachine 的 Impulse 系统（按定义曲线衰减的位移信号），
    /// 本类只负责"攒创伤 → 出力大小"这段可测的数学。
    /// </summary>
    public sealed class CameraTrauma
    {
        /// <summary>每次触发允许提交的最大创伤（0~1 的 1 端）。</summary>
        public const float MaxTrauma = 1f;

        /// <summary>创伤每秒衰减多少（越大震得越短促）。</summary>
        public float DecayPerSecond = 1.5f;

        /// <summary>trauma = 1 时对外发出的脉冲力度（映射到 Cinemachine 的 force）。</summary>
        public float MaxForce = 3f;

        /// <summary>当前创伤值（0~1）。</summary>
        public float Value { get; private set; }

        public bool IsActive => Value > 0f;

        /// <summary>累加创伤（自动夹紧到 1），返回累加后的值。</summary>
        public float Add(float amount)
        {
            Value = Mathf.Clamp01(Value + Mathf.Max(0f, amount));
            return Value;
        }

        /// <summary>按秒衰减，返回衰减后的值。</summary>
        public float Decay(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
                return Value;
            Value = Mathf.Max(0f, Value - DecayPerSecond * deltaSeconds);
            return Value;
        }

        public void Reset() => Value = 0f;

        /// <summary>创伤 → 脉冲力度（**平方映射**）。</summary>
        public float ForceOf(float trauma) => MaxForce * Mathf.Clamp01(trauma) * Mathf.Clamp01(trauma);

        /// <summary>当前创伤对应的力度。</summary>
        public float CurrentForce => ForceOf(Value);
    }
}
