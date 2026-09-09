# Milestone

Milestone 是一个全音游玩法引擎 / 游戏项目，支持多玩法、多谱面格式、谱面编辑器、AI 制谱助手、段位挑战、性能与压力自检等能力。

## 主要特性

- **多玩法支持**：Mania、Phigros、Arcaea、Cytus、osu!standard、ADOFAI、IIDX、回环作曲、Milestone 原生等多模式。
- **多谱面格式**：`.osu` / `.mc` / `.sm` / `.ssc` / `.qua` / `.aff` / `.adofai` / `.txt` / `.json` / `.mil`。
- **谱面编辑器**：支持有轨/无轨模式编辑、事件键帧、曲线、吸附、自动偏移、AI 检查等。
- **引擎化运行时**：包含 Unity 式场景/组件/生命周期、Ruleset 玩法族、UI 组件、转场动画、软渲染与窗口宿主。
- **AI 能力**：制谱检查、本地 AI 摘要、AI 演示/陪玩、AI 训练与段位压线校准。
- **自检与压测**：`--selfcheck`、`--perftest`、`--stresstest`、`--edshot`、`--edsim` 等 CLI。

## 目录结构

| 目录 | 说明 |
|---|---|
| `游戏源码/` | C# 游戏层源码（主工程 `Milestone.csproj`） |
| `编辑指南/` | 项目文档、工程说明、零基础指南、历史协作文档 |
| `构建产物/` | 构建脚本、工具与中间产物 |
| `输出产物/` | 发布包、示例谱面、日志、测试产物 |
| `.agent-teams/` | 多智能体协作任务板/消息记录 |

## 快速开始

Windows + .NET 8 环境下：

```powershell
dotnet build 游戏源码\Milestone.csproj -c Release
dotnet run --project 游戏源码\Milestone.csproj -c Release -- --selfcheck
```

常用 CLI：

```powershell
Milestone.exe --selfcheck          # 自检
Milestone.exe --perftest out       # 性能测试
Milestone.exe --stresstest out 5   # 压力测试（L1-L5）
Milestone.exe --edshot             # 编辑器截图取证
Milestone.exe --shotdemo           # 游玩自动截图
```

## 文档

详细文档入口：

- [编辑指南/README.md](编辑指南/README.md)
- [编辑指南/工程说明.md](编辑指南/工程说明.md)
- [编辑指南/零基础更改指南.md](编辑指南/零基础更改指南.md)
- [编辑指南/文档精选/](编辑指南/文档精选/)

## 关联项目

- [HybridEngine](https://github.com/E-tgyanefr/HybridEngine)：Milestone 引擎化/移植过程中使用的可编程游戏引擎。
