# MilestoneEngine.exe 使用指南（引擎工具）

> 面向人类用户：从双击到打包的完整流程。命令表/预设表请以 `--help` 实测为准（README 引擎工具章节同源）。

## 快速开始（五步主流程）

### 1. 双击 → 引擎工具窗

```powershell
MilestoneEngine.exe                # 无参数 = --tool：工具窗（主页/模式预设/预览/帮助 4 页）
```

- 主页 5 卡片：引擎自检 / 模式预设 / 帮助 /（打开谱面·导出打包=批3 后激活）。
- **键盘导航**：ESC=回主页；工具窗 UI 点击需**真实鼠标事件**（合成 mouse_event 不进入自绘 UI，可见 t49 取证结论）。
- 中文路径：建议 UTF-8 控制台（PowerShell 7 或 `chcp 65001`）或直接 `& '...\MilestoneEngine.exe' --open '中文路径.mil'`（RunOpen 失败诊断含字符数+非 ASCII 提示）。

### 2. 选模式预设

```powershell
MilestoneEngine.exe --preset mania4 --headless   # 无头 acc（规则集/命中/MISS/ACC/全命中判定）
MilestoneEngine.exe --export-mil maimai          # 预设示例谱导出 .mil → exports/（补示例缺口）
```

- 预设 id：mania4/6/8 phigros arcaea cytus osustd iidx maimai adofai（`--help` 预设表含判定档/键位/示例谱）。

### 3. 预览

```powershell
MilestoneEngine.exe --preview mania4             # 独立窗自动游玩（4K 下落；ESC 退出）
MilestoneEngine.exe --preview mania4 --size 800x500 --secs 5   # 指定尺寸+自动关闭
```

- 预览渲染：轨道/音符/判定线/命中-漏判色条/完成色带（EnginePreviewHost 直渲，LetterboxRenderer 坐标变换）。
- 注意：`--size` 受系统 DPI 放大影响（PerMonitorV2：物理像素 = 逻辑 × DPI/96；1.5 倍缩放时 800×500 显示为 1200×750）。

### 4. 打开谱面

```powershell
MilestoneEngine.exe --open "其他\Chart\Milestone示例\Mania 4K 示例.mil"
MilestoneEngine.exe --open "其他\Chart\Milestone示例\Phigros 示例.mil" --headless   # 只打 ACC
```

- 支持：.mil（milestone-1 子集：metadata/notes(t,c,e,type,x,y)/bpm 变速事件）；mode→族自动匹配预设（mania 按 keys 4/6/8）。
- 目前不支持：.osu/.adofai/.aff（P2 候选）；parts/stages 多模式谱=根场语义。

### 5. 导出打包

```powershell
MilestoneEngine.exe --export "其他\Chart\Milestone示例\Mania 4K 示例.mil" [out 目录 | --out <dir>]   # --out 指定产物目录（默认=谱面目录）
```

- 前置：`pack-template.exe` 须与 MilestoneEngine.exe 同目录（构建 PublishPlayer 后拷贝；发布脚本 build-tool.ps1 自动处理）。
- 产出：`<曲名>.exe`（=[pack-template][payload][int64 len][MILSTPK1]，与 Milestone.exe --pack 同格式；双击自动播放/ESC/播完关）。
- **pack=4K 演示口径**：任意模式归一化 4K 时间列（Col%4 / X×4）——语义简化，文档明示（引擎使用者 T2）。
- 无模板时：友好报错（MessageBoxW 提示 + 用法说明）。

## 发布两形态（T6）

| 形态 | 适用 | 布局 |
|---|---|---|
| 目录版 | 本地/内网快速分发 | `EnginePlay/` 文件夹（.exe + 同名 .dll/.deps/.runtimeconfig；复制整个文件夹+README） |
| 单文件版 | 便携分发 | `dotnet publish ... -p:SelfContained=true -p:PublishSingleFile=true`（~67MB；无内嵌谱即工具；打包谱面见 --export） |

发布脚本：`构建产物\engine-exe\build-tool.ps1`（publish EnginePlay + 拷贝 pack-template.exe + 示例谱目录 samples/ + README/guide 打包进发布目录）。

## 节拍音轨（示例谱 audio 指向）

```powershell
MilestoneEngine.exe --synthmetronome 120 --beats 8   # → samples\bpm_120.wav（--version 当前 1.0.0-t69）
```

- WAV 规格：44 字节头 / 16bit mono 44.1kHz / 首拍 0dB 啪 + 后续拍 -6dB 嗒 / 结尾 100ms 静音（纯 BCL 合成零依赖）。
- .mil 示例谱 audio 字段指向 `samples/bpm_<bpm>.wav`（零版权零体积）。

## 常见问题

1. **窗口黑屏？** 直渲修复后不应出现；若复现请报（三方复验已过：PrintWindow/live 双路径）。
2. **中文路径打不开？** 根因=PowerShell 5.1 `Start-Process -ArgumentList` 中文经 GBK 控制台重编码截断（**引擎侧 LoadFile 无截断点**，试玩已复测确认）。解法：PowerShell 7/UTF-8 控制台（chcp 65001）、`ProcessStartInfo` .NET 直接传参、或直接 `& '...\MilestoneEngine.exe' --open '中文路径.mil'`；失败诊断含字符数/非 ASCII 提示+建议文案。
3. **--size 和屏幕尺寸不符？** DPI 放大（PerMonitorV2）所致，非引擎缺陷。
4. **打包缺模板？** 放 pack-template.exe 到 exe 目录或跑 build-tool.ps1。
5. **--export 后 exe 无法打开？** 无双击权限时先 `--play <pack>` 或重跑 --export（模板必须同目录）。

## 引擎内开发工作流（C# API）

```csharp
using ChartPlayer;
var p = EnginePresets.Get("mania4");            // 预设元数据（10 项）
var rs = EnginePresets.BuildRuleset("mania4"); // RulesetFactory.Build(描述符)
var chart = EnginePresets.BuildSampleChart("mania4");   // 增强示例谱（Lane hold/Ring 滑星/Path 拐角）
var ctx = EnginePresets.BuildContext("mania4");
var r = EnginePresets.RunHeadless("mania4");   // 无头闭环（acc）
// 引擎窗预览：new ToolApp() → EngineApp.Run(...)；--preview 同：new EnginePreviewHost("mania4") → EngineApp.Run(...)
```

> 引擎工具/预设/预览/导入导出完整实现见 `引擎\engine\Tool\`（ToolApp/PreviewController/RulesetRenderer/EnginePreviewHost/ChartImport/PackExport/MilExport/MetronomeSynth/LetterboxRenderer）与 `引擎\engine\EnginePresets.cs`、`Platform\`（EngineText/SoftwareDrawAdapter/WinAudioBackend）。