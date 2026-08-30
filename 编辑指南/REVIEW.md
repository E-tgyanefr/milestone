# Milestone 代码审查 — 极简版

> ~15,000 行 | 21 种模式 | 12 个核心文件

## P0 — 立即修复

| # | 文件:行 | 问题 | 修复 |
|---|---------|------|------|
| 1 | `GamePanel.Render.cs` | 3582 行过大 | 拆为 Render.Core + Render.Phigros + Render.Arcaea + Render.Taiko + Render.Catch + Render.Maimai（每文件 < 800 行） |
| 2 | 全局 > 20 处 `catch { }` | 渲染/训练/加载错误全部静默吞掉 | 替换为 `Logger.Error(e) + ShowToast()` |
| 3 | `GamePanel.Judge.cs` L75-79 | ADOFAI 角度窗口临时替换 `JudgeSettings.Levels`，多线程竞态（AI 模拟 + 主线程同时运行） | 传入参数替代全局替换 |
| 4 | `MpManager.cs` L323+ | `Players` 字典在消息处理线程未加锁 | `lock(_lock) { Players[...] }` |
| 5 | `MpManager.cs` L323+ | JSON 消息 `type` 字段无白名单校验，可注入 | 校验 `type ∈ {rename, hit, finish, roster, start, bye, chart}` |
| 6 | `engine/ScoreBoard.cs` vs `JudgementEngine.cs` | 两者都维护 Score/Combo/ACC，重复实现 | 废弃 ScoreBoard，统一走 JudgementEngine |

## P1 — 下一版本

| # | 文件:行 | 问题 |
|---|---------|------|
| 7 | `GamePanel.cs` 1738 行 | 拆为 Core + Step + Settle 三个 partial 文件 |
| 8 | `JudgementEngine.cs` L29 | `GetHashCode()` 跨进程不一致，缓存失效不稳定 → 用 `PresetKey.Length + PresetKey.Sum(c => c)` |
| 9 | `JudgeSettings.cs` L65-77 | `ComputeOsuHpMultiplier()` 10000 次迭代可解析求解（对数） |
| 10 | `MpManager.cs` | 无心跳检测，客户端断开后 Host 不及时感知 |
| 11 | `DifficultyRating.cs` L301 | `0.06 * d` 容差无注释来源 |
| 12 | `EditorSim.cs` L54 | 反射依赖方法名，重构易断裂 → 用接口契约替代 |
| 13 | `GamePanel.Render.cs` L3571 + 大量魔法数字 | `R * 0.30`/`70, 24, 300` 等 → 提取 `Constants.cs` |

## 逐文件一句话

| 文件 | 行数 | 一句话 |
|------|------|--------|
| `Models.cs` | 161 | OK，`Note.Type` 用 string 可扩展但拼写错误风险高 |
| `JudgementEngine.cs` | 269 | 判定核心正确，P1#8 GetHashCode 需替换 |
| `JudgeSettings.cs` | 509 | HP/AR 动态窗口/40+ 预设完整，P1#9 迭代可优化 |
| `GamePanel.cs` | 1738 | P0#7 拆分，partial 拆分设计合理 |
| `GamePanel.Judge.cs` | 548 | P0#3 ADOFAI 竞态；FLICK 窗口应 ~40ms 而非 80ms |
| `GamePanel.Input.cs` | 259 | OK，防 IME 拦截是关键设计 |
| `GamePanel.Render.cs` | 3582 | P0#1 拆分 + P0#2 空 catch |
| `GamePanel.Ai.cs` | — | 人类模拟与实战逻辑应统一路径 |
| `AiEngine.cs` | 467 | OK，21 模式预设 + AI 系统完整 |
| `AiTrainer.cs` | 243 | OK，3 模拟平均 + 72 轮自适应收敛 |
| `ReviewCard.cs` | 188 | OK，UR = 标准差 × 10 需注释 |
| `DifficultyRating.cs` | 430 | OK，14 个复杂度特征全面 |
| `EditorSim.cs` | 659 | 17 项测试覆盖好，P1#12 反射需接口化 |
| `MpManager.cs` | 393 | P0#4 线程安全 + P0#5 消息注入 |
| `ChartParser.cs` | 405 | OK，内容嗅探降级合理 |
| `ScoreBoard.cs` | 146 | P0#6 与 JudgementEngine 重复，需废弃 |
| `GraphicsQuality.cs` | 71 | OK，自动档滞回区间需增大 |
| `GameSettings.cs` | 83 | OK |

## 跨模块交互风险

1. **JudgementEngine ↔ GamePanel**: `_eng` 被主线程和 AI 线程同时访问 → 加锁或确保线程安全
2. **JudgeSettings ↔ JudgementEngine**: `Levels` 列表被 `Hit()` 临时替换 → 改为参数传递
3. **MpManager ↔ GamePanel**: 无接口契约，`OnHit` 事件在独立线程中触发 → 确保回调线程安全

## 架构评估

**优点**: 统一判定引擎 + partial class 拆分 + 21 模式注册 + Phigros 事件曲线精密实现 + Arcaea 10M 回归 + AiTrainer 离线训练可复现

**风险**: 空 catch 导致无法诊断 > 20 处关键路径无错误反馈

## 质量指标

| 指标 | 值 |
|------|-----|
| 总行数 | ~15,000+ |
| 最大单文件 | 3582 行 (Render.cs) |
| 吞异常 | > 20 处 |
| 魔法数字 | > 100 处 |
| 线程安全漏洞 | 3 处 (Levels 替换/MpManager/Eng 并发) |
| 单元测试 | ≈ 35 项 (EditorSim 17 + ReviewCard 3 + DifficultyRating 3 + DensityCalc 3 + DanLogic 4 + EventGen 5) |
