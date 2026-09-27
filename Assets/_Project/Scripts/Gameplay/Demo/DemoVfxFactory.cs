using UnityEngine;

namespace Template.Demo
{
    /// <summary>
    /// 演示用特效预制体 —— 运行期用代码搭一个粒子系统（零资产，与 DemoUiPanels / DemoAudioClips 同思路）。
    /// 真实项目的特效是美术做的预制体、走 Addressables 按地址加载（见 VfxService.PlayAsync），本类随 Demo 一起删除。
    /// </summary>
    internal static class DemoVfxFactory
    {
        /// <summary>一次性爆点：寿命 0.5 秒、球状发射 30 颗 —— 够短，便于看到"播完自动回收"。</summary>
        public static GameObject CreateBurst()
        {
            var go = new GameObject("DemoVfx_Burst");
            // 先关掉再装配：激活状态下 AddComponent<ParticleSystem> 会立刻开始播放，
            // 此时改 duration 会被引擎拒绝（"Setting the duration while system is still playing is not supported"），
            // 而且模板本体留在场景里也会被摄像机看到。
            go.SetActive(false);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.6f;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = 0.5f;
            main.startSpeed = 4f;
            main.startSize = 0.22f;
            main.startColor = new Color(1f, 0.72f, 0.25f);
            main.maxParticles = 60;
            main.gravityModifier = 0.4f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;                                  // 一次性爆发，不持续发射
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            // 演示用内置 shader（正式项目用美术资产自带的材质）；找不到就退到 Sprites/Default，避免出现粉色方块
            Shader shader = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            renderer.material = new Material(shader);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            return go;
        }
    }
}
