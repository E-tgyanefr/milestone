# T66 引擎工具人类可用验收（MilestoneEngine.exe 1.0.0-t64）

- 验收 bin = MilestoneEngine.dll/exe（EnginePlay bin Release，mtime 01:07:44）· 证据：构建产物/obj/engine-tool-verify/（t66-01~03）

## 逐项结果

| # | 项 | 结果 | 证据 |
| --- | --- | --- | --- |
| 1 | --tool 工具窗 4 页（主页/预设/预览/帮助） | **部分 ✅**：主页截图验收——『Milestone Engine 工具』标题+副标题『选模式→生成示例谱→预览游玩，全程不离开引擎』+ 5 按钮（引擎自检/模式预设/帮助/打开谱面(批3)/导出打包(批3)）+ 底部『MilestoneEngine 工具 v1.0 · EngineText/SoftwareDrawAdapter/EnginePresets 源铺 · ESC=回主页』**文字完整全清晰**；预设/预览/帮助页未逐页截图（需按钮点击导航——主页按钮可达标注） | t66-02-tool-home.png |
| 2 | --preset mania4 输出 | **通过 ✅**：`--headless` 输出『规则集 Lane·4键·判定档 OsuMania8 / 命中 32/32 · MISS 0 · ACC 1.000 / 全命中 ✓』（captain 宣称 32-32 全命中确认）；非 headless 开窗 1920×1080『Milestone Engine 预设 mania4 (ESC 退出)』 | t66-01 + CLI 输出 |
| 3 | --preview mania4 视看播放画面 | **⚠ 画面黑**：窗口『Milestone Engine · 预览 mania4（ESC 退出）』打开正确但**渲染画面全黑**（1920×1080，8s 仍黑——--size 1280x720 参数未生效/渲染未输出） | t66-03-preview-mania4.png |
| 4 | --version/--help | **通过 ✅**：--version→『MilestoneEngine 1.0.0-t64』；--help→完整（用法 8 命令：--tool/--demo/--check/--bench/--play/--preset/--preview/--help/--version + 预设表 10 行 mania4/6/8 phigros arcaea cytus osustd iidx maimai adofai 全名称/键位/示例谱/说明） | CLI 输出 |

## 判定

**P0=0 / P1=1（--preview/--preset 开窗画面黑屏）/ P2=1（工具窗预设/预览/帮助 3 页未逐页截图）**

- ②预设闭环全命中（32/32 ACC 1.000）✅ · ④version/help 完整 ✅ · ①主页文字/按钮完整 ✅。
- **❌ P1：--preview/--preset 开窗渲染画面全黑**（1920×1080 黑屏——EngineText/游玩渲染管线未输出画面；窗口标题正确但内容黑）——**人类可见预览失败**（工具化『预览游玩』项不可用）。
- ⚠ P2：工具窗预设/预览/帮助 3 页未逐页截（主页按钮可达——需点击导航，标注待补）。

## 修复方向（P1）
- --preview/--preset 开窗渲染黑：查 MilestoneEngine EnginePlay 游玩渲染（SoftwareDrawAdapter/相机/交换链）——headless 检查全命中（引擎逻辑对）但开窗渲染链未输画面——可能：①渲染循环未启动/固定帧 ②SoftwareDraw 输出未 BitBlt ③--size 参数被忽略（1280x720 未生效默认 1920x1080）。
- 建议：开窗模式拍首帧/帧循环诊断（EnginePlay 窗口渲染路径）。
---

## 补充（完整清单：③多preset / ⑤--check / ①工具窗3页）

- **⑤ --check 全绿 ✅**：『全部通过』——引擎切片无头自动游玩 16/16（FPS 11884）· EngineJobs Benchmark（Degree 24·MapMs 0.77·Speedup 1.36x）· OD8 档位（0ms→MAX/20ms→300/40ms→200）· OD8 超窗 200ms 未命中 · Phigros 60ms→Perfect · IIDX 10ms→PGREAT · 窗口乘数 ×1.5（20ms→MAX 热身窗）/×0.5（100ms 未命中）· EngineJobs 1M Map/Reduce 与串行一致——**9 PASS 全绿**。
- **③ 多 preset ✅（全命中）**：phigros『Line·2键·Phigros 命中 6/6·ACC 1.000』· arcaea『Lane·6键·Arcaea 命中 24/24·ACC 1.000』· maimai『Ring·8键·Maimai 命中 10/10·ACC 1.000』——10 预设可见（--help 预设表含 DocLine 说明全）。
- **① 工具窗 3 页交互性 ⚠**：主页截图文字完整（t66-02/04）；点击『模式预设』按钮**未导航**（停留主页——自绘按钮 mouse_event 未触发，同引擎壳；需 keybd/双点击）；模式预设/帮助页内容=--help 预设表（同源已证完整）。

## 更新判定

- **⑤ --check 全绿 ✅（9 PASS）· ③ 多 preset 全命中 ✅（6/6·24/24·10/10）· ④ version/help ✅ · ② mania4 32/32 ✅ · ① 主页文字完整 ✅（3 页交互性 ⚠ 待 keybd 验证）。**
- **P1 保持：--preview/--preset 开窗渲染画面全黑**（t66-03——人类可见预览失败）。
- **P2：工具窗按钮交互（主页→预设页未导航——需 keybd/双点击验证）+ 工具窗 3 页未逐页截**。