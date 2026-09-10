# tools — Milestone 引擎源码隔离

`check-engine-source.ps1` 是 Milestone 的引擎源码泄漏门禁。

## 规则

- 允许：由引擎构建出的产物（`.dll` / `.exe` / `.lib` / `.a` / `.nupkg` / `.pdb` 等）以及谱面、皮肤、图片、音频等数据资产。
- 禁止：引擎源码、头文件、工程/构建文件、引擎 C# / Python 绑定源码、任何引擎源码树。
- 扫描包含被 `.gitignore` 忽略的目录；因为打包/Debug 目录同样会随整包泄漏。

## 使用

```powershell
# 只检查（发现泄漏时退出码 1，适合 CI/构建门禁）
pwsh -NoProfile -File tools\check-engine-source.ps1 -FailOnFind

# 连同 Git 历史路径一起检查
pwsh -NoProfile -File tools\check-engine-source.ps1 -IncludeHistory -FailOnFind

# 检查另一个项目目录（如 HybridProjects\MILESTONE）
pwsh -NoProfile -File tools\check-engine-source.ps1 -Path ..\HybridProjects\MILESTONE -FailOnFind

# 清理：把识别到的引擎源码根移动仓库外的隔离区
pwsh -NoProfile -File tools\check-engine-source.ps1 -Quarantine
```

## 构建依赖

旧版 V2 构建通过 MSBuild 属性 `EngineV2Source`（或环境变量 `ENGINE_V2_SOURCE`）在仓库外读取引擎源码；仓库内永远不放置引擎源码。全新 HybridEngine 版见 `HybridEngine/dotnet/Milestone.Game`。

CI：`.github/workflows/engine-source-guard.yml`

## 打包注意

`.agent-teams/`、`输出产物/`、`构建产物/` 均已被 `.gitignore` 排除，但本地直接压缩整个目录时仍可能被带入。发布推荐使用 `git archive` 或白名单拷贝；门禁脚本会扫描工作区（含被忽略目录），发现引擎源码即失败。