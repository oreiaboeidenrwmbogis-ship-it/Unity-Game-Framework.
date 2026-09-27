# Systems — 游戏系统模块（分类目录）

按阶段建立独立子模块（每个 = 独立文件夹 + asmdef）：

- 输入系统 `Input` → `Template.Input`
- 场景流转 `Scenes` → `Template.Scenes`
- 时间管理 `Time` → `Template.Time`
- 音频系统 `Audio` → `Template.Audio`（与引擎命名空间冲突处理见模块内文档）
- 设置 / 存档 / 任务 / 背包 / 成就 …按规划展开

依赖方向：`Systems/*` → `Template.Core`，禁止反向。
