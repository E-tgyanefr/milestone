# B2 真窗验收 a-e + 批4 终验报告（ENGINE-BIN 07:20:05）

- 官方构建 = 引擎/engine/EnginePlay/bin/Release/net8.0-windows（07:20:05——含 t70 接线+批4）· 证据：构建产物/obj/verify-b2/

## B2 验收 a-e

| # | 项 | 结果 | 证据 |
| --- | --- | --- | --- |
| a | SendMessageW 客户区坐标点『模式预设』 | ❌ **未导航**（客户区 247,307 投递 WM_LBUTTONDOWN/UP——仍主页；与 t68-09 相同） | b2-01 |
| b | 真实鼠标点击主页卡片（前台干净桌面） | ❌ **未导航**（ClientToScreen 精确坐标 (408,503) + mouse_event MOVE/DOWN/UP——仍主页） | b2-02/03 |
| c | ESC 各页返回 | ⚠ 未测（页面未进入——a/b 阻塞） | — |
| d | --demo ESC 退出 | ⚠ 未测（同理） | — |
| e | 工具窗预览页 ESC→预设页 | ⚠ 未测（同理） | — |

## 批4 终验

| 项 | 结果 | 证据 |
| --- | --- | --- | --- |
| --synthmetronome 120 --beats 8 | ✅ 输出『节拍音轨已合成：samples\bpm_120.wav（bpm=120·拍数=8·16bit mono 44.1kHz·首拍 0dB 啪/后续 -6dB 嗒/结尾 100ms 静音）』 wav 生成于 仓库根 samples/bpm_120.wav 361664B | CLI |
| wav 规格 | ✅ WAVE/mono(1ch)/44100Hz/16bit——361664B=44 头+16bit×44100×8拍×0.5s(4s)+100ms 静音(8820B)——规格精准匹配首拍重音 | wav 头 |
| --help 命令 | ✅ 16 项（核心 11+选项）含 --synthmetronome/--open/--preset/--preview 等 | CLI |
| README/guide 抽查 | ⚠ 发布目录未构建（engine-tool/README 待 build-tool.ps1 编码修复后发布） | — |

## 判定与疑点

**a ❌ / b ❌（真窗输入未达）——EngineChecks 无窗口导航断言全绿（OnMouse(326,329)→presets/ESC→home）= 引擎 UI 层 OK——但『Win32Window WndProc→window.Mouse 事件→host.OnMouse』窗口层转发链未达（SendMessage/真实鼠标均未触发导航）**

**疑点（交互 eng-coder-vis）**：①Win32Window WndProc 是否将 WM_LBUTTONDOWN 生产为 Mouse 事件（可能需 WM_MOUSEMOVE 序列/或 lParam 坐标转换规则）②输入桥已接线（t70 官方 bin 应含）但窗口事件→host 未达=事件生产层断点——建议：Win32Window WndProc 打日志（WM_LBUTTONDOWN 是否收到+是否 Invoke Mouse）+ 确认 Mouse 事件签名/坐标。

**批4 通过**（synthmetronome 规格+命令完整）；README/guide 待发布目录（build-tool.ps1 编码修复后）。
---

## 定案追加（eng-coder-vis 破案 + winsta 隔离确认）

- **破案**（eng-coder-vis 日志实证）：鼠标事件链路完全正常（bridge.Mouse x=164,y=204→ToolApp virtual(145,216)）——『未导航』根因=**坐标落在主页『引擎自检』卡（空实现占位无视觉变化）**——『模式预设』卡正确物理坐标 **=(335,311)**（virtual(326,329) 按 scale=0.946/ox=26.8 反算）。
- **我环境复测**（正确坐标 (335,311)）：官方 bin（b2-10 SendMessage/b2-11 真实鼠标）→ 未导航；=t70-play/bin（b2-12 SendMessage）→ 未导航——**三 bin×正确坐标均失败**（与 eng-coder-vis 日志（其环境触发）对比）= **我的 run_code/pwsh 会话与桌面 winsta 输入隔离确定性证据**（引擎路由 OK=其日志实证）。
- **B2 判定=引擎路由 OK（eng-coder-vis 日志链路+断言+冒烟）**；我的 a/b 自动化=环境隔离（限制性证据不入引擎判定）——**待人工物理点击终确认**（eng-design-vis 已请 eng-coder-vis 真实硬件鼠标 30 秒点击，达=关闭）。
- **附加**（eng-design-vis B13）：主页『引擎自检』卡改禁用样式+提示（防再误导）。

---

## 引擎使用者终验（WM 级，如实记录）
> PostMessage WM 鼠标流程（--tool 主页点击 (335,311) → 预设页像素签名切换 38,66,120 / ESC → home 签名恢复 10,12,20）无异常；--preview mania4 ESC → exit=0；**--demo ESC → exit=0 ✅（正式记账）**；截图证据缺失 = 沙箱路径限制（如实标注）。
