# 本地 AI 制谱助检记录（真实调用）

> 时间：2026-08-25（Ollama qwen3.8-27b，模型已加载，服务 `http://127.0.0.1:11434`）
> 方式：与 `源码\Ai\AiChartReview.cs` 的 `ReviewFileAsync(path)` 完全同规则——谱面文件全文读取（两份测试谱均 &lt;4000 字符，未触发截断）→
> 头部指令 + 谱面文本 → `POST /api/generate`（model=qwen3.8-27b, stream:false, num_predict=256），原始响应 JSON 存档于 `其他\logs\aichart_review_raw_*.json`。

## 调用 1：phigros_test.json（直读）

- **文件**：`其他\Chart\测试格式\phigros_test.json`（1462 字节，35 行）
- **Prompt**（= 固定头部 + 文件全文；头部原文）：

> 你是资深音游制谱审查助手。下面是某个谱面文件的原始文本（可能已截断）。请站在制谱者角度审查，给出 3~5 条具体、可执行的中文制谱建议（对音/结构/难度均可），每条一行，以「建议：」开头。不要客套，不要重复谱面内容，不要输出其他内容。
>
> ——— 谱面文本 ———
> ```json（原样：formatVersion 3 / judgeLineList bpm 120 / notesAbove 6 音 / notesBelow 3 音 / moveY+rotate+moveX 事件…全文见原文件）```

- **响应实测**：`done=true` `done_reason=stop`，**eval_count=2085**（远超 num_predict=256——本 Ollama 版本对 qwen3 思考模型未按请求截断，模型自然结束），
  `total_duration` 90.9s（含 load 5.9s），`eval_duration` 83.7s（≈ **24.9 tok/s** 实测）。
- **原始回复结构**：思考段（**无 `<think>` 开标签**，仅以 `</think>` 收尾——Ollama 返回时模板吞掉开标签）+ 最终 5 条建议。代码已按此修正 `CleanReply`（定位第一个 `</think>` 取其后正文）。
- **回复要点**（最终 5 条，逐字）：

> 建议：将开头 1.0 秒空白压缩至 0.5 秒或加入弱拍铺垫，避免首拍延迟导致对音模糊。
> 建议：把 1.5 秒的 Hold 与 2.0 秒的新音符错开至少 0.25 秒，减少同手/同区域叠加造成的误触。
> 建议：统一 0.5 秒固定间隔为 BPM 对齐的 1/4 拍或 1/8 拍，并在重拍使用 type1/2 强化节奏。
> 建议：将 3.0 秒开始的 rotate 与 3.0 秒 type4 分离，旋转提前 0.5 秒或延后 0.5 秒，避免视觉干扰击打点。
> 建议：结尾 Hold 到 5.5 秒、旋转持续到 6.0 秒，收束过散，建议把 Hold 尾音缩短至 4.8 秒或提前结束事件。

- **质量点评**：5 条建议全部具体到时间点/类型，命中真实问题（开头空白/长条与音符叠加/RPE 事件干扰/收束拖沓）；对"holdTime 单位不确定"做了风险标注（思考段内），无编造。

## 调用 2：mania_test.osu（直读）

- **文件**：`其他\Chart\测试格式\mania_test.osu`（1102 字节，18 行）
- **Prompt**：同上固定头部 + 文件全文（osu v14 / Mode 3 / HD8 CS4 OD8 / TimingPoint 0,500 / 18 个 HitObjects…）。
- **响应实测**：`done=true` `done_reason=stop`，`eval_count=1722`（同样未按 256 截断），`total_duration` 70.5s（load 仅 2ms——模型仍驻留），`eval_duration` 69.1s（≈ **24.9 tok/s**）。
- **回复要点**（最终 5 条，逐字）：

> 建议：将 OD/AR 下调至与实际密度匹配，或加密为 1/8 流并加入双押以支撑 Hard 标签。
> 建议：按稳定 1/4 或 1/8 拍重新对齐音符，减少游离于节拍之外的间隔。
> 建议：给两个长 hold 设置明确的结束击键或尾随节奏点，避免握持段后只有单键拖尾。
> 建议：加入 2 至 4 键和弦、相邻键跳跃或短 stream，构建可辨识的 4K 难度曲线。
> 建议：设置正式 PreviewTime 与 Countdown，确保玩家能准确进入起始节奏。

- **质量点评**：正确识别 Mode 3=mania 4K、3500~4000 的 hold 双段、Hard 标签 vs 实际低密度不匹配、PreviewTime -1 等真实问题；建议可执行。

## 关键实测发现（代码/文档已同步）

1. **`num_predict` 未被本版本 Ollama 强制执行**：`num_predict=10` 的对照测试仍生成 85 token（done_reason=stop）；两张谱面分别生成 2085/1722 token。模型直到自然结束为止。→ 单次调用耗时 70~91s，仍低于代码 120s 超时（余量 ~30s）；若未来变慢会触发"AI 调用超时"回退而非卡死。
2. **思考段无开标签**：响应中思考内容直接跟在 response 开头，仅以 `</think>` 收尾（Ollama 模板行为）。代码 `CleanReply` 已改为"定位第一个 `</think>` 取其后正文"，否则 `Suggestions` 会错取思考段首行。
3. 实测吞吐 ~25 tok/s（eval_duration 反推），与《本地AI接入说明》早年记录的 ~4.1 tok/s 不同（可能受当时条件影响）；以本次实测为准。
4. 模型回复质量：对音/结构/难度建议都具体可执行；思考段内含对格式不确定性的风险标注，无编造。

## 回退策略验证（代码行为，伪调用）

- 服务不可达（`http://127.0.0.1:11434` 拒绝连接）→ `AiReviewResult{Offline=true, Error="AI 服务不可达（…）", Text="【AI 离线】…"+规则文本}`，正常返回不抛异常。
- 超时（>120s）→ `OperationCanceledException` 捕获 → 同上，原因"AI 调用超时（> 120000ms）"。
- 空回复 / 思考段占满预算 → "模型未返回有效内容（回复为空或思考段占满 256 token 预算）"回退。
- 文件不存在/读失败 → 立即回退，不发请求。
- 规则回退文本内容：无校验问题时给通用手工检查清单；有校验问题时逐条列出（供 t8 接入 validator 问题列表）。
