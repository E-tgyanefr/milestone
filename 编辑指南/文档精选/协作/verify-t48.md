# T48 定点复测报告（bin 22:12:31：t48 修复）

- 复测 bin = Milestone.dll 22:12:31（晚于源码 22:09:48；selfcheck 0）· 证据：构建产物/obj/verify-t48/（t48-01~03）

## 三点复测

| # | 项 | 结果 | 证据 |
| --- | --- | --- | --- |
| 1 | AI 演示 ESC→引擎菜单页正常显示（非黑屏）+ 主窗隐藏/单窗/MainWindowHandle≠0 | **部分 ❌**：MainWindowHandle=**2953004（≠0 ✅）**+ 单窗=1 ✅——但**引擎窗仍显示黑色 GamePanel 空态**（「Milestone：在主菜单选择「开始游戏」进入选歌」+ FPS 0——**与 t44 相同，未重绘到引擎菜单**） | t48-01-ai-esc.png |
| 2 | 联机对局中 ESC→返回引擎菜单（非黑屏） | **不通过 ❌**：MainWindowHandle=**19402428（≠0 ✅）**但**纯黑屏**（11744 字节——联机 ESC 返回仍黑屏） | t48-02-online-esc.png |
| 3 | 普通游玩 ESC→回引擎壳 UI（t50 UnhostContent，非黑屏） | **不通过 ❌**：--play 游玩中 ESC 后**纯黑屏**（12383 字节——UnhostContent 未重绘引擎壳 UI） | t48-03-play-esc.png |

---

## 判定

**① 部分（句柄恢复 ✅ 但 UI 黑屏）** · **② ❌ 纯黑屏** · **③ ❌ 纯黑屏**

### 根因（与 t44 相同）
- **t48/t50 只恢复了 MainWindowHandle（句柄 ≠0 达标）但「返回引擎菜单/引擎壳 UI」的 GoTo("menu")/UnhostContent 重绘调用仍缺失**——ESC 后引擎窗显示黑色 GamePanel 空态/纯黑（UI 未重绘）。
- AI 演示（①）显示出黑 GamePanel 空态（「在主菜单选择…」提示=F6 播放器空态），联机/游玩（②③）为纯黑——**同一根因链：退出后 UI 未重绘**。

【修复方向】**ExitToLibrary/UnhostContent 在恢复主窗句柄后调用 GoTo("menu")**（引擎菜单重绘）——当前仅 Show 主窗未重绘页；联机 ESC 同路径（未触发 GoTo）。修复后三点应显示**完整引擎菜单**（顶部菜单/功能卡片齐全）。