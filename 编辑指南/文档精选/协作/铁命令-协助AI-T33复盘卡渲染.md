# 铁命令 · 协助 AI —— T33 复盘数据卡渲染（剩余工作）

> **铁命令**：本任务由用户亲自下达（2026-08-22，经主代理转达），**必须执行，不可拒绝/不可降级**。
> 接收方：协助 AI（另一个 DeepSeek 会话）。
> **边界特批**：通常主项目源码归主代理；本次用户特批允许协助 AI 修改 `Play\GamePanel.Render.cs`（仅此一处主项目改动）。

## 一、背景

- 项目：`D:\Users\etgya\Desktop\milestone`（Milestone.csproj，C# WinForms 音游，必须 **0 警告 0 错误**）
- 用户已批准《未来规划与创新提案》：P1-1「AI 教练复盘闭环」——回放+判定统计 → 分段/断连/UR → 复盘卡（v1 接本地 LLM 文案）
- 当前进度：T32 完成；T33（P1-1 v0 复盘数据卡）**已完成数据侧**，只剩**结算画面绘制**这一件事

## 二、已完成（严禁重做/回退，可阅读参考）

| 文件 | 内容 |
|---|---|
| `Play\ReviewCard.cs`（新建） | `ReviewSegment`/`ReviewData` 模型 + `ReviewCard.Build(chart, notes, result)`（5s 分段：Notes/Hits/Misses/HitRate/AvgAbsDev/Ur、Breaks 断连、Strongest/Weakest 段）+ `UrOf` + `Save()`（存 `Player\复盘\复盘_{标题}_{时间戳}.json`）+ `RunReviewTest()` |
| `Program.cs` | 新增 CLI `--reviewtest`（仿 `--selfcheck`：写 reviewtest.log，exit 0/1） |
| `Play\GamePanel.cs` | `ReviewData _review;` 字段；`Finish()` 中非演示/非联机时执行 `_review = ReviewCard.Build(...); ReviewCard.Save(_review);`；`ResetState()` 中 `_review = null;` |

**当前基线**：`dotnet build -c Release` 0 错 ✅ · `--selfcheck` 0 ✅ · `--reviewtest` 0 ✅（数据管线已可用，只是不显示）

## 三、你的任务（唯一）

在结算画面绘制复盘卡。文件：`Play\GamePanel.Render.cs` → `void DrawResultScreen(int W, int H, double mono)`。

1. **插入点**：「段位徽章（`res.IsDan`）块」结束后、「`// 新纪录横幅`」块之前。
2. **显示条件**：`_review != null && _review.Segments.Count > 0 && t >= 1.5`，淡入 0.5s。
3. **段位谱面（res.IsDan 为 true）**：结算空间不足 → 只画一行精简文本（跳过柱条），或完全不显示（二选一，选更简单者，注释说明原因）。
4. **普通谱面**画：① 标题「复盘」；② 分段命中率柱条（最多 12 段：`bw=(panelW-60)/n`，底色 `Color.FromArgb(120,30,38,54)`，命中率占比填充；段内 `Misses>0` 用红 `(255,90,90)` 否则绿 `(90,220,140)`，用 `_d2d.FillRect`）；③ 一行摘要文本：`分段{N}×5s  UR {Ur:F0}  断连 {Breaks} 次  最弱段 {StartMs/1000:F0}s~{EndMs/1000:F0}s 命中 {HitRate:F0}%`（WeakestIdx<0 时省略最后一段）。
5. **约束**：只加绘制代码，**不改任何其他逻辑**；不新增字段/依赖；坐标参照现有样式（`px+30` 起、宽 `panelW-60`、小字 11f、颜色系 `(180,195,220)`）；注意与底部按钮（`byBtn = py2+panelH-104`）保持间距（建议柱条高 34、摘要行距 +22，非段位布局下从 `jy + order.Count*rowH + 24` 起画）。

## 四、验证工作流（完成前逐项执行，缺一不可）

```powershell
dotnet build -c Release          # 0 警告 0 错误
Milestone.exe --selfcheck        # exit 0
Milestone.exe --reviewtest       # exit 0（数据管线不变量）
Milestone.exe --ratingtest       # exit 0（可选，防误伤 T32）
```

## 五、回执

完成后在 `docs\协作\消息.md` 以 `[协助AI]` 身份留言：改动说明 + 三项验证结果；主代理随后再次验收（构建/自检/复盘卡数据样例）。
