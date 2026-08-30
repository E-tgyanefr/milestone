# 制谱 AI 与加速器说明

> 范围：本地 AI 制谱助检（qwen3.8-27b 直读谱面/校验问题摘要）+ AI 加速器路由（NPU>GPU>CPU）。
> 配合文档：《本地AI接入说明.md》（模型安装/事故记录）、《本地ai-制谱助检记录.md》（真实调用样例）。

## 1. 模型与调用

| 项 | 值 |
|---|---|
| 模型 | Qwen3.8-27B（gguf IQ3_XXS，约 10.9 GB；Ollama 注册名 `qwen3.8-27b` / `qwen3.8-27b:latest`） |
| 服务 | Ollama（系统服务自启动），监听 `http://127.0.0.1:11434`；**不要**用 llama-server（`-ngl 99` 曾占满 12GB 显存导致游戏冻结，已被停止；若要用必须 `-ngl 12` 低显存启动） |
| 端点 | `POST http://127.0.0.1:11434/api/generate`（非流式） |
| 请求体 | `{"model":"qwen3.8-27b","prompt":"…","stream":false,"num_predict":256}` |
| 超时 | 默认 **120s**（实测单次 70~91s：首载 ~6~21s + 生成 70~85s；余量 ~30s）；所有调用 try/catch，绝不阻塞编辑器 |
| 实测速度 | **~25 tok/s**（t7 实测：2085 token/83.7s、1722 token/69.1s；旧文档 ~4.1 tok/s 记录以本次实测为准）；注意 **num_predict=256 未被本版本 Ollama 对 qwen3 强制执行**（模型自然结束时 stop，实测生成 85~2085 token） |
| 生成行为 | 模型有 `<think>` 思考段，且 Ollama 返回时**只有 `</think>` 收尾、无 `<think>` 开标签**——代码按第一个 `</think>` 定位正文剥离思考段 |

### 代码入口（`源码\Ai\AiChartReview.cs`）

| API | 用途 |
|---|---|
| `ReviewFileAsync(path)` / `ReviewFile(path)` | **直读谱面**：文件文本截断至 ~4000 字符 → 中文 prompt → 模型 |
| `ReviewMetaAsync(title, mode, version, bpm, noteCount, issues)` / `ReviewMeta(...)` | **元数据+校验问题**：问题列表摘要化（≤30 条）→ 中文 prompt → 模型 |
| `ReviewChartTextAsync(text)` | 直接给谱面文本（无文件时） |
| `BuildPromptFromChartText` / `BuildPromptFromMeta` | 纯 prompt 组装（可复用/测试） |
| `BuildFallbackText(reason, issues)` | 规则回退文本（离线时展示） |

返回 `AiReviewResult`：`Ok`（模型是否给有效建议）、`Offline`（是否回退）、`Text`、`Suggestions`（≤5 条）、`Error`（回退原因）、`ElapsedMs`、`Prompt`、`Accelerator`（NPU/GPU/CPU 标签）。

### 回退策略（离线/失败永不阻塞编辑器）

1. 模型离线（连不上 11434）→ 回退：规则文本 + **【AI 离线】** 标注 + 原因（服务不可达/超时/异常）。
2. 超时（默认 120s，`CancellationTokenSource` 强制取消）→ 同上，原因"AI 调用超时"。
3. 空回复 / 思考段占满 `num_predict 256` 预算（该模型有 `<think>` 行为）→ 回退并注明。
4. 输出清理：去掉 `<think>…</think>` 与 Markdown 围栏；按行拆出 ≤5 条建议供 UI 逐条显示。
5. 任何路径都不向调用方抛异常；读文件失败、参数异常同样回退。

## 2. 加速器优先级（NPU > GPU > CPU）

**接口（t1 `源码\Ai\AiDevice.cs`，已就绪）**：

```csharp
public enum AiDeviceKind { Cpu, Gpu, Npu }          // 首选顺序：Npu > Gpu > Cpu

public static class AiAccelerator
{
    public static AiDeviceKind Probe();              // 探测：NPU 存在 → Npu；否则 GPU 硬件 → Gpu；兜底 Cpu
    public static AiDeviceKind Preferred();          // 与 Probe 相同（路由方调用）
    public static string Name(AiDeviceKind kind);    // "NPU"/"GPU"/"CPU"
    public static string Describe();                 // "AI 加速器首选: … | CPU: … | NPU: … | GPU: …"
}
```

**探测依赖（t1）**：

- `HardwareProbe.Probe()`（`引擎\engine\HardwareProbe.cs`）→ HardwareInfo{ CpuBrand, CpuCores, CpuLogical, TotalMemoryBytes, IsNpuPresent, NpuName }；NPU=处理器名启发式：含 `AI Boost`(Intel Core Ultra NPU) / `Ryzen AI`(AMD) / `Snapdragon``X Elite``X Plus`(高通) / `Ultra 5|7|9`(Intel NPU 代) → `IsNpuPresent=true`。
- `RenderBackend.IsHardware / GpuName`（`源码\Play\D2DRenderer.cs`）→ 是否有硬件 GPU（WARP=软件光栅化不算）。

**路由约定**：

1. AI 功能（本文件的制谱助检/摘要、以及其他推理）显示 `Accelerator` 标签：`AiChartReview` 内部 `SafeAccelerator()` = `AiAccelerator.Name(AiAccelerator.Preferred())`，任何失败回退 "CPU"（`AiReviewResult.Accelerator`）。
2. 会话启动打印一行：`AI 加速器：NPU/GPU/CPU（本地 Ollama qwen3.8-27b）`——**t8 已集成**：`Program.Main` 启动时 `Logger.Info + Console.WriteLine`；`--aidetect` 输出同样包含该行（另有 t1 的 AiDeviceKind 首选与 AiAccelerator.Describe）。
3. **纯算法对音（DSP/BeatAlignEngine，t6）不涉及加速器**：CPU 矢量化/多线程（EngineJobs，t1）。
4. NPU 目前只做"探测+显示"——Ollama 后端不会自动走 NPU；若后续用 ONNX/OpenVINO NPU 后端，路由点仍是 `AiAccelerator.Preferred()`。

## 3. 局限（必须知晓）

- **~4 tok/s**：只能做**短摘要/短建议**（本功能 prompt ≤600 字 + 输出 256 token 上限）；不适合长谱面逐段分析、不适合结构化规划/多文件任务（历史实测定性：该模型不胜任清单式规划/校验）。
- **直读截断 4000 字符**：超长谱面（如 4455 音 IF=Infinity）只看得见开头，结构建议有限；长谱用"元数据+校验问题"模式更有效。
- **思考段占预算**：模型有 `<think>` 行为，256 token 可能被思考吃掉导致空回复（代码已做空回复回退；记录文档含实测样例）。
- 首载 ~21s：连续使用建议 keep_alive（Ollama 默认 5 分钟）。
- **与游戏共存**：Ollama 由系统管理；llama-server 必须 `-ngl 12`（约 1.5~2GB 显存）或纯 CPU，否则冻结；程序侧 GpuGuard（`源码\Play\GpuGuard.cs`）检测 AI 服务占用 >1.5GB 时自动 WARP 软件渲染保底。
- 不接游戏判定/渲染热路径；制谱助检只读谱面文本/元数据，不改谱面（应用建议由制谱者手动）。

### 编辑器接入（t8 已集成）

- 编辑器工具栏「🤖 AI 检查」→ `ChartAiAssistant.RunAiCheck(chart)`（ChartValidator 规则检查）→ `ShowAiCheckDialog`：问题列表（双击跳时间）+ 底部「AI 摘要」区（打开即后台自动生成，按钮可重试）。
- AI 摘要 = `AiChartReview.ReviewMetaAsync(标题/模式/难度/BPM/音符数, 问题文本行≤30条)`；后台线程调用不阻塞 UI（Task.Run + BeginInvoke 回填），离线/超时/空回复自动回退（【AI 离线】+ 规则文本），标签显示加速器与耗时。
- 接线点：`源码\Charting\ChartAiAssistant.cs`（IssueTexts/TotalNotes/ShowAiCheckDialog/GenerateAiSummary，依赖 t7 AiChartReview）+ `源码\Charting\ChartEditorPanel.cs`（RunAiChartCheck 改调 ShowAiCheckDialog）。

## 4. 相关文件

- `源码\Ai\AiChartReview.cs`（本功能，t7）
- `源码\Ai\AiDevice.cs`（t1：AiDeviceKind/AiAccelerator）
- `引擎\engine\HardwareProbe.cs`（t1：NPU/CPU 探测）
- `源码\Play\GpuGuard.cs`（AI 服务显存防护）
- `其他\docs\本地AI接入说明.md`（模型安装/事故记录）
- `其他\docs\协作\本地ai-制谱助检记录.md`（真实调用样例）
- `其他\docs\协作\制谱AI与加速器说明.md`（本文档）
