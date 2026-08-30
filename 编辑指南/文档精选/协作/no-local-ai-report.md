# 禁用全部本地 AI 服务调用 — 改动报告（t12）

> 用户指令：程序【不调用本地 AI】—— 不得对 Ollama / llama-server / LM Studio 等本地服务发起任何请求（HTTP / 启动探测 / 后台轮询）。
> 执行：coder-max（编写）　完成日期：2026-08-27（任务记录日期）

## 一、结论

- **全部本地 AI 服务调用已禁用**：源码中 **HttpClient / HttpRequestMessage / 端点常量（11434、api/generate）已全部移除**，静态审计 0 处残留。
- **规则类功能保留**：🎯 自动校准偏移（DSP 起音检测）、🤖 AI 检查（ChartValidator 规则校验）均为纯本地计算，不受影响、继续可用。
- **引擎内置 AI 保留**：AiEngine / AiPlayer / AiTrainer 为规则驱动（体力/紧张度/命中概率模拟，无网络），不属"本地 AI 服务"，保留并用于编辑谱面 AI 游玩。
- **启动路径零请求**：AiAccelerator.Probe / HardwareProbe 仅读注册表与 DXGI 硬件名（允许保留）；GpuGuard 仅进程名检测（允许保留，注释已同步更新）。
- 约束遵守：本次只写代码未构建（未运行 dotnet build / 未启动程序）。

## 二、改动清单（6 个文件）

### 1. 源码/Ai/AiChartReview.cs —— 核心禁用（重写）
- 新增 `public const bool LocalAiEnabled = false;`（全局禁用开关）与 `public const string DisabledNotice = "本地 AI 未启用（规则检查仍可用）";`
- 删除：`Model / Endpoint / NumPredict` 常量、`_http`（HttpClient）、`CallAsync`（HTTP POST）、`ParseResponse / CleanReply`（Ollama 响应解析）、`BuildFallback`。
- `ReviewFileAsync / ReviewMetaAsync / ReviewChartTextAsync` 及同步包装：**保留公开签名**，但全部直接返回 `BuildDisabled(...)`（提示文案 + 规则问题文本），不再读取文件、不再发起任何请求、不再有超时/轮询逻辑。
- 新增 `BuildDisabledText(issues)`：统一禁用文案 + 规则问题逐条列表（供 UI 展示）。
- 保留：`BuildPromptFromChartText / BuildPromptFromMeta`（纯字符串）、`BuildFallbackText`（规则文本）、`SafeAccelerator`（硬件标签，允许）。
- 移除 usings：System.Net.Http / System.Text.Json / System.Text.RegularExpressions / System.Threading / System.Diagnostics / System.IO。

### 2. 源码/Charting/ChartAiAssistant.cs —— 编辑器 AI 检查对话框
- `ShowAiCheckDialog`：「AI 摘要」区改为「规则摘要」区：
  - 标签：`"🤖 规则摘要 · 本地 AI 未启用（规则检查仍可用）"`
  - 文本框初始即显示 `BuildRuleSummaryText(...)`（禁用提示 + 规则统计），**不再自动调用 AI**（删除 `f.Shown += GenerateAiSummary`）。
  - 按钮 `"🤖 生成 AI 摘要"` → `"🔄 刷新规则摘要"`：点击仅同步重跑 `ChartAiAssistant.RunAiCheck(chart)`（纯规则），重建列表与摘要，零网络。
  - 问题列表构建提取为局部函数 `RebuildList`（刷新复用）。
- 删除 `GenerateAiSummary`（原后台调 AiChartReview 的方法）与 `IssueTexts`；新增 `BuildRuleSummaryText`。
- 顶栏默认文案 `"✅ 规则检查未发现问题（下方 AI 摘要可给出制谱建议）"` → `"✅ 规则检查未发现问题（本地 AI 已禁用，下方为规则摘要）"`。

### 3. 源码/Charting/ChartMentor.cs —— 制谱助手侧边面板
- 按钮 `"🤖 生成 AI 建议（本地模型）"` → `"🔄 生成规则建议（不联网）"`。
- `_aiBox` 默认文案改为 `"本地 AI 未启用（规则检查仍可用）。点上方「🔄 生成规则建议」查看内置规则校验结果（纯本地计算，不联网）。"`
- `GenAiSuggestion` 重写：删除 `Task.Run + AiChartReview.ReviewMetaAsync`；改为同步 `RunAiCheck` → `AiChartReview.BuildDisabledText(lines)` 直接回填，异常安全（try/catch/finally）。
- ② 自动对音校准（AlignCalibrate：WAV 解析 + Dsp.DetectOnsets/EstimateBpm）与 ③ 生成起步谱（StarterChartGenerator）为规则/DSP 实现——原样保留。

### 4. Program.cs —— 启动路径文案去 Ollama 字样（2 处）
- `--aidetect` 输出行与启动日志行：`"AI 加速器：…（本地 Ollama qwen3.8-27b）"` → `"AI 加速器：…（内置规则 AI · 本地 AI 服务已禁用）"`（中性描述）。
- 确认启动路径无任何本地服务请求：AiAccelerator.Probe（注册表/GPU 名启发式）与 EngineJobs.Benchmark（CPU 基准）均非服务调用，允许保留。

### 5. 源码/Charting/ChartEditorPanel.cs —— 注释同步
- `RunAiChartCheck` 内注释由"t8：问题列表 + 本地 AI 摘要（AiChartReview 后台调用…）"改为"t12：问题列表 + 规则摘要（本地 AI 服务已禁用，不发起任何网络请求）"。调用链不变（`ShowAiCheckDialog` 签名未改）。

### 6. 源码/Play/GpuGuard.cs —— 仅注释更新（逻辑保留）
- 危险进程名单（llama-server / lmstudio / koboldcpp 等）与 `AIServerHogging()` **原样保留**（任务明确：进程检测不算调用）。
- 更新过时注释："否则我们的本地 AI（制谱审查）会误触发 WARP" → 说明 t12 起程序已禁用本地 AI 服务调用，Ollama 放行仅针对用户自行运行的服务。

## 三、测试与佐证（本次未构建，以下为静态证据 + 复验步骤）

### 静态审计（已执行，全部通过）
| 检查项 | 结果 |
| --- | --- |
| 源码内 HttpClient / HttpRequestMessage / SendAsync | **0 处**（改前 1 处：AiChartReview._http） |
| 11434 / api/generate / Endpoint 常量 | **0 处** |
| 127.0.0.1 / localhost | 仅 MpManager（联机功能本机 IP 显示，非 AI 调用） |
| ReviewMetaAsync 调用点 | 仅 AiChartReview 自身禁用桩，UI 不再调用 |
| "本地 Ollama" 字样 | **0 处**（余下均为"已禁用"说明文案 / GpuGuard 进程名） |
| AiEngine / AiPlayer / AiTrainer | 无网络（规则驱动），保留 |

### 运行态复验步骤（供下次构建后执行）
1. 构建后启动程序，打开编辑器 → 点「🤖 AI 检查」：对话框应显示规则问题列表 + "本地 AI 未启用（规则检查仍可用）"提示，**不出现"AI 正在分析"**；
2. 点「🔄 刷新规则摘要」：仅重跑规则，瞬间完成（无 10~90s 等待）；
3. 制谱助手点「🔄 生成规则建议」：规则文本立即出现；
4. 网络佐证：任务管理器 / `netstat -ano | findstr 11434` 全程无 11434 端口连接；若本机装有 Ollama，`ollama ps` 无新载入模型；
5. 🎯 自动校准偏移、④ AI 检查（规则）仍可用；编辑谱面 → AI 游玩（AiPlayer 规则驱动）不受影响。

## 四、边界说明
- **编辑器"AI 辅助"现状**：🎯自动校准偏移、🤖AI 检查 为规则实现 → 可用；"AI 摘要/建议"不再调用本地模型 → 改为规则摘要提示文案（符合用户指令）。
- 引擎内置 AI 玩家（AiEngine/AiPlayer）与 AI 演示/陪玩（规则驱动）**不属于本地 AI 服务调用**，未做任何限制，编辑谱面 AI 游玩功能完整保留。
- MpManager 联机为独立功能（TcpClient），与本地 AI 服务无关，未改动。
