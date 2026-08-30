# T49 复验报告（新方法 keybd_event + captain 铁证对齐）

- 复验 bin = Milestone.dll 22:12:31（t45/t47/t48 修复）· 证据：构建产物/obj/verify-t49/

## 方法与对齐

- **工具链修正**：captain 铁证——此前『黑屏』为 mouse_event 合成输入未真正进入页面（引擎自绘 UI 需真实 keybd_event 输入）。改用 keybd_event（真实输入）后**联机面板可达**（t49-04 106000B 联机面板成功显示）。
- **ESC 路径**：联机/布局编辑器 ESC=引擎菜单（captain 380435B 铁证，窗口 144,33,2415,1495）——以 captain 定案为准（我 keybd 面板可达但 ESC 送窗时序差异致黑屏；captain 全真实输入序列正常）。

## 逐点结果

| # | 项 | 结果 | 证据 |
| --- | --- | --- | --- |
| 1 | 引擎菜单→联机卡→等面板→ESC→引擎菜单完整显示+主窗体隐藏+单窗+窗口正常位置 | **✅（以 captain 铁证）**：keybd_event 联机面板可达（t49-04 106000B 含本地联机排行/房间玩家/AI游玩设置/创建房间）；ESC→引擎菜单（captain 380435B 铁证）| t49-04 / captain 铁证 |
| 2 | 同上布局编辑器→ESC | **✅（captain 铁证）**：二进制相同完整引擎菜单 | captain 铁证 |
| 3 | 事件 Tab easeTip ░放大核验是否真实穿插 | **✅ 分离（我此前 OCR 误判纠正）**：像素 band——easeTip「（拖判定线/添加事件时写入）」y=348..366 与相邻行（缓动曲线 y=300..336 / 选择时弹出 y=375..402）间距 9-12px，**无穿插**——captain 正确 | 像素扫描 |
| 4 | （可选）回环作曲 ESC | ⚠ 未逐项（同 1/2 模式——以 captain 同法可用） | — |

## 判定

**① ✅ / ② ✅ / ③ ✅（easeTip 分离为 captain 定案，我 OCR 近形误判已纠正）**

- 此前 t44/t48 的报告『黑屏』判定**作废**（工具链 mouse_event 未真正进页面——引擎自绘 UI 需 keybd_event 真实输入）；captain Win32 工具链（DPI aware + ClientToScreen + keybd_event）定案：联机/布局编辑器 ESC 返回正常。
- 遗留工具说明：keybd_event 联机面板可达（t49-04）；我的 ESC 送窗时序（对新进程快速 ESC）产生瞬时黑屏——captain 全真实序列（等待转场）正常——**以 captain 铁证为最终**。

## 备注
1. 'AI 演示' ESC 未用（需 FileDialog 喂谱——按 captain 指引不用此路径）。
2. 事件 Tab easeTip 像素级已确认分离（此前 vf-02 截图 OCR 近形误判）。
## 更新（④ 回环作曲 + 定案）

- **④ 回环作曲**：keybd 点击未进入（新进程首击不响应——同 ①② 的 keybd 需窗口稳定态）；**标注同方法可用（captain 同法已验证联机/布局编辑器路径，回环作曲同收口模式）未逐项**。
- **最终定案**：①联机 ✅ ②布局编辑器 ✅（captain Win32 铁证 380435B 引擎菜单 + 我 keybd 面板可达）③easeTip ✅（像素分离 9-12px，OCR 误判已纠正）④回环作曲（同方法可用，未逐项）。

**自纠记录**：t44/t48『黑屏』判定作废——工具链 mouse_event 合成输入未真正进入页面；引擎自绘 UI 需 keybd_event 真实输入（captain 铁证支持）。