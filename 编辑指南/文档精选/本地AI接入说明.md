# 本地 AI 接入说明（Qwen3.8-27B · Ollama）

> 目标：本机本地大模型作为**协作伙伴**（代码审查 / 架构方案讨论 / 谱面与难度分析），
> 供编码代理与开发者随时调用 —— **不接入游戏程序本身**。

## 现状（2026-08-25 实机勘测更新）

| 项 | 值 |
|---|---|
| 模型 | Qwen3.8-27B（gguf 量化档 Qwen3.8-27B-UD-IQ3_XXS，约 10.9 GB；gguf family 识别为 qwen35 / 27.3B） |
| 模型文件 | `D:\models\Qwen3.8-27B\Qwen3.8-27B-UD-IQ3_XXS.gguf` |
| Ollama | v0.32.15，安装于 `C:\Users\etgya\AppData\Local\Programs\Ollama` |
| Ollama 服务 | 即开即用（安装器注册自启动），监听 `http://127.0.0.1:11434` |
| 注册模型名 | `qwen3.8-27b`（即 `qwen3.8-27b:latest`，digest ce20b21d4cd8，10 GB） |
| 模型定义文件 | `milestone\obj\Qwen3.8Modelfile`（SYSTEM 简体中文助手 + PARAMETER temperature 0.6 / num_ctx 8192 / num_predict 2048） |
| 实测性能 | 首载 ~21 s；生成约 4.1 tokens/s（PS 显示 10%/90% CPU/GPU；nvidia-smi NVML 不可用，未能核实 GPU 型号/占用细节） |

> 注：文档原记录的 DeepSeek-R1-Distill-Qwen-14B（Q4_K_M, 8.99 GB）及 `D:\models\DeepSeek-R1-Distill-Qwen-14B\*.gguf` **本机不存在**（D:\models 下只有 Qwen3.8-27B 目录），故按实际模型注册。

## 常用命令

```powershell
# 查看模型
ollama list

# 直接对话（终端交互模式，安装目录已加 PATH）
ollama run qwen3.8-27b "用一句中文介绍你自己"

# 原始 HTTP（流式/非流式均可）
curl.exe -s http://localhost:11434/api/generate -d "{\"model\":\"qwen3.8-27b\",\"prompt\":\"你好\",\"stream\":false}"
```

## 注册方式（已执行成功）

```powershell
# 1. 编辑 obj\Qwen3.8Modelfile（FROM 指向实际 gguf，参数用 PARAMETER 小写指令）
# 2. 导入（10.9 GB，本机实测约 50 s 完成）
ollama create qwen3.8-27b -f obj\Qwen3.8Modelfile
```

## 说明与注意

- **Modelfile 语法**：参数必须写 `PARAMETER <name> <value>`（如 `PARAMETER temperature 0.6`）；裸写 `TEMPERATURE`/`NUM_CTX` 会被 Ollama 0.32 拒绝（报错 `command must be one of ... "parameter" ...`）。
- Qwen3.8-27B 有思考/推理行为：回答前输出 `<think>…</think>`；给足输出上限（num_predict 2048）避免思考阶段被截断。
- 模型加载后 5 分钟不调用会自动卸载（keep_alive 默认）；再调用需重新加载 ~21 s。
- 本模型生成偏慢（~4.1 tokens/s），适合**短输出**的简单任务（一句话/短分析）；超长分析耗时明显，需合理控制提示词规模。
- 若日后换/加模型：新 .gguf 放入 `D:\models\`，改 `obj\Qwen3.8Modelfile` 的 `FROM` 路径，再 `ollama create <名字> -f obj\Qwen3.8Modelfile`。
---

## ⚠️ 2026-08-25 冻结事故修复（重要：与游戏共存须知）

**事故**：`llama-server -m D:\models\Qwen3.8-27B\Qwen3.8-27B-UD-IQ3_XXS.gguf -ngl 99 -t 16 -c 16384 --parallel 2`（模型 10.9GB 全量塞入 12GB 显存）运行时，打开 Milestone（Direct2D 硬件渲染）→ NVIDIA 报错 / 全机冻结（驱动 OOM/TDR）。

**已修复（引擎侧）**：程序启动加入 GpuGuard（源码\Play\GpuGuard.cs）：检测到 AI 服务进程（llama-server/ollama/lmstudio/koboldcpp/text-generation-ui 等）占用 >1.5GB 时，自动置 GameSettings.ForceWarp → D2DRenderer.CreateTarget 跳过硬件尝试直接走 WARP 软件渲染（稳定可玩），并弹窗一次提示。硬件目标创建失败同样直接 WARP（原 catch 流程修复为 _rtWindow==null 判定）。

**共存建议**：打游戏时把 llama-server 显存占用调小（推荐 `-ngl 12`，约 1.5~2GB 显存；或 `-ngl 0` 纯 CPU），或直接停止：
```powershell
Get-Process llama-server -ErrorAction SilentlyContinue | Stop-Process -Force
# 低显存重启（游戏共存推荐）
D:\llm\llama\llama-server.exe -m D:\models\Qwen3.8-27B\Qwen3.8-27B-UD-IQ3_XXS.gguf --host 127.0.0.1 --port 8080 --alias qwen3.8-27b -ngl 12 -t 8 -c 8192 --parallel 2
```
---

## 2026-08-26 追加：Ollama 误触发 GPU 保护 → WARP 白块修复

现象：主菜单全部按钮为白色矩形（引擎壳 D2D 实显）。

根因链：GpuGuard 启动检测把 **Ollama 本地 AI 服务**（内存 >1.5GB）误判为危险进程 → 置 ForceWarp=true → D2D 走 WARP 软件渲染 → **WARP 路径把 UI 填色呈现为白色**；同时该保护每次启动都弹窗。

修复（双保险）：
1. GpuGuard 危险名单收窄为真正可能整卡占满显存的服务（llama-server/LM Studio/koboldcpp/text-generation-ui 等），**放行 Ollama**（官方按需加载/自动卸载，不会整卡常驻）——本地 AI 功能正常且不再误触发 WARP。
2. 引擎壳（EngineMainShell）渲染改为 **GDI 软件路径**（GdiDrawAdapter→Bitmap→窗口，与 --shellshot 取证同路径）：颜色逐像素正确、无显存/驱动依赖、任意后端一致——即使某天真的走 WARP，UI 也不会白块。

验证：实机屏幕抓取——修复前壳窗口按钮区白 55%，修复后 **white=4%（仅文字）· dark=77% · blue=18%（蓝色开始按钮回归）**；构建 0/0；--selfcheck/--enginerun EXIT 0。
