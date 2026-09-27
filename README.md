# Unity 游戏框架
Unity 2022.3 模板，适用于任何类型的独立游戏。每个系统都是一个纯 C# 服务，通过类型化的事件总线连接；每个模块都位于独立的 asmdef 文件中；可选模块可以删除而不会影响核心代码。功能齐全：用户界面、输入、音频、设置、CSV 配置、版本化存档、可寻址对象、连接池、F1 调试面板。
# Unity 游戏框架

Unity 2022.3 LTS（C# 9）商业级独立游戏开发模板。适用于任何类型的独立游戏。

## 核心特性

- **服务化 + 单一启动器**：所有系统都是不继承 MonoBehaviour 的纯 C# 服务，由唯一的 `Bootstrap` 统一 `Init / Tick / Dispose`，场景里没有满天飞的 `Update()` 和单例。
- **模块边界是程序集**：一个模块 = 一个 asmdef = 一个命名空间，依赖严格单向；模块间只通过强类型事件总线（`EventBus<T>`）和接口通信，模块可整体删除。
- **工程化规范**：八条铁律 + 约 107 条测试 + 一个 F1 运行期调试面板；全面禁用 `Resources.Load`，日志走门面，异步统一 UniTask。

## 已完成的底座模块

- **核心架构**：启动器、游戏状态机（Boot→Splash→MainMenu→Gameplay↔Paused）、服务定位器、事件总线、对象池、状态机（FSM）。
- **数据与资源**：Addressables 资源服务、CSV 转强类型代码的配置表系统、多槽位存档（支持版本迁移与损坏降级）。
- **基础支撑**：输入系统、场景流转、时间管理器（双时钟/定时器）。
- **游戏系统**：UI 面板栈（uGUI）、音频系统、设置系统、相机系统（Cinemachine 薄封装）、特效池化。
- **工程化**：模块化 asmdef、自定义日志、F1 调试面板、扩展方法库。

> *注：背包、任务、对话、成就、本地化、CI、一键出包管线等玩法与管线内容留给你，模板不预写玩法。*

## 快速开始

1. 克隆仓库，用 **Unity 2022.3 LTS** 打开。
2. **必做设置**：`Project Settings → Player → Other Settings → Active Input Handling = Both`。
3. 打开 `Assets/_Project/Scenes/SampleScene.unity` → Play，控制台会输出各服务就绪日志。
4. Play 中按 **F1** 打开调试面板（FPS/内存/对象池/事件监听数/资源账本/命令控制台）。
5. 运行测试：`Window → General → Test Runner → EditMode → Run All`。

## 详细文档

详细使用指南可见 `/docs/常用API示例.md`、`/docs/功能目录.md`、`/docs/快速开始.md`。

## 代码速览

业务代码通常只需要引入 `using UnityEngine;`，通过 `G` 短入口调用所有服务：

```csharp
using UnityEngine; // 你自己的脚本通常只需要这一行

namespace Jam // 与 Assets/Scripts/Game/G.cs 同命名空间 → G 直接可见
{
    public sealed class MyThing : MonoBehaviour
    {
        private void Start()
        {
            G.Info("MyThing", "就绪");        // 日志门面（禁止直接 Debug.Log）
            G.Audio.PlaySound(clip);          // 服务：G.Ui / G.Audio / G.Time / G.Pools / G.Save / G.Config ...
            G.Time.Delay(2f, () => G.Info("A", "两秒后触发")); // 定时器天然支持暂停与缩放
        }
    }
}
