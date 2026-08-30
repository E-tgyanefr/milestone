# 铁命令 · 协助 AI —— T35 曲库星级（收尾 + 验证）

> **铁命令**：经用户下达（2026-08-22，主代理转达），必须执行，不可拒绝/降级。
> 接收方：协助 AI（另一个 DeepSeek 会话）。用户特批主项目修改权（仅 `Play\DifficultyRating.cs`、`Program.cs` 入口处）。
> 注：此前 T33 渲染铁命令（`铁命令-协助AI-T33复盘卡渲染.md`）**仍优先**，两任务并行不冲突（不同文件）。

## 一、背景
- 项目：`D:\Users\etgya\Desktop\milestone`（Milestone.csproj，0 警告 0 错误约束）
- 用户批准《未来规划与创新提案》：P1-2「本地难度评估器 + 曲库星级标注」（内部用）
- T32（v0 评估器 + --ratingtest 段位验收）已完成；T35 = v1 数据侧：模式复杂度特征 + 曲库星级清单

## 二、当前状态（主代理已写，遗留构建错误待修）
| 文件 | 状态 |
|---|---|
| `Play\DifficultyRating.cs` | 已加 v1 特征：Stats 新增 `JackRatio/ChordRatio/RhythmComplex/BpmVar`（Compute 已填）、`RateV1()`（v0.2+特征修正）、`RunLibraryRating()`（扫 Chart\ + 段位文件\ → `library_rating.json`） |
| `Program.cs` | 已加 CLI 分支 `--libraryrating` |
| **构建** | ❌ 2 错：`DifficultyRating.cs(358)` `JsonSerializer` / `(359)` `JsonSerializerOptions` 未找到 —— **缺 `using System.Text.Json;`** |

## 三、你的任务
1. **修复**：`DifficultyRating.cs` 顶部补 `using System.Text.Json;`（唯一必要改动；若还有顺手警告，只允许同文件内）。
2. **验证四件套（缺一不可，结果写入回执）**：
   ```powershell
   dotnet build -c Release                # 必须 0 警告 0 错误
   Milestone.exe --ratingtest             # 必须 exit 0（段位序验收不因 v1 破坏——Rate() 未动，应仍通过）
   Milestone.exe --libraryrating          # 必须 exit 0，且 library_rating.json 生成（含 rate/rateV1/特征）
   Milestone.exe --selfcheck              # 必须 exit 0
   ```
3. **观察**：`libraryrating.log` 的 TOP 8 中，星级是否与直觉相符（段位谱应偏高）；`library_rating.json` 的 `rateV1` 是否与 `rate` 有区分度（若全区间无一差异请如实报告，不要强行调参）。
4. **禁止**：不改 `Rate()` 权重（段位验收依赖）；不新增模块；不触碰 GamePanel/ReviewCard/CoachCard。

## 四、回执
`docs\协作\消息.md` 以 `[协助AI]` 留言：改动说明 + 四项验证结果 + TOP 8 概况。主代理随后复验并推进 T35b（选歌列表星级显示）。
