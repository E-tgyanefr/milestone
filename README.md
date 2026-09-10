# Milestone

Milestone 是**基于 HybridEngine 构建**的全音游玩法引擎 / 游戏项目，支持多玩法、多谱面格式、谱面编辑器、AI 制谱助手、段位挑战、性能与压力自检等能力。

## 主要特性

- **多玩法支持**：Mania、Phigros、Arcaea、Cytus、osu!standard、ADOFAI、IIDX、回环作曲、Milestone 原生等多模式。
- **多谱面格式**：`.osu` / `.mc` / `.sm` / `.ssc` / `.qua` / `.aff` / `.adofai` / `.txt` / `.json` / `.mil`。
- **谱面编辑器**：支持有轨/无轨模式编辑、事件键帧、曲线、吸附、自动偏移、AI 检查等。
- **HybridEngine 驱动**：使用 HybridEngine 的场景/组件/生命周期、Ruleset 玩法族、UI 组件、转场动画、软渲染与窗口宿主；游戏层/移植层见 `HybridEngine/dotnet/Milestone.Game`。
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

> 旧版 `游戏源码\Milestone.csproj` 需要仓库外的 V2 引擎源码（通过 `ENGINE_V2_SOURCE` 或默认外置路径引用）；构建前/CI 门禁会拒绝仓库内出现引擎源码。HybridEngine 版见 `HybridEngine/dotnet/Milestone.Game`。

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

## 引擎源码隔离（硬约束）

本仓库只允许出现 Milestone 游戏层源码与**由引擎构建出的产物**（DLL/EXE/包/运行时数据）；任何引擎源码、头文件、工程文件、源码树都不得进入 Milestone。

- 旧版 V2 构建的引擎源码必须放在仓库外，通过 `EngineV2Source` / 环境变量 `ENGINE_V2_SOURCE` 引用（默认外置路径：`..\..\_engine-source-canonical\legacy-v2\引擎源码\engine`）。
- 全新 HybridEngine 版见 `HybridEngine/dotnet/Milestone.Game`，同样只消费引擎构建产物/绑定层，不复制引擎源码。
- 提交、打包、发布前运行门禁：

```powershell
pwsh -NoProfile -File tools\check-engine-source.ps1 -IncludeHistory -FailOnFind
```

发现引擎源码（含历史提交路径）时返回退出码 1。CI 工作流 `.github/workflows/engine-source-guard.yml` 会自动执行该门禁。

## 基于 HybridEngine

- [HybridEngine](https://github.com/E-tgyanefr/HybridEngine)：Milestone 所使用的基础游戏引擎。
- HybridEngine 内的 Milestone 游戏层/移植版：`HybridEngine/dotnet/Milestone.Game`。
- Milestone 通过 HybridEngine 的 C ABI / C# 绑定 / Python 绑定使用引擎能力。
