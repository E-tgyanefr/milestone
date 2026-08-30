# coder-vis 空闲自查：游戏层代码风险/性能/IPC 冗余（第一轮）

- 时间：t54 已归属 eng-design-vis（in_progress）后，coder-vis 空闲按常驻指令自查
- 范围：源码/（游戏层）风险、性能、IPC/驱动冗余；只读检查未改代码（涉及改动均待 captain/task 拍板）
- 方法：grep 热点模式 + 关键文件精读（GamePanel.cs / D2DRenderer.cs / ChartEditorPanel.cs / EngineHosting.cs / AiChartReview.cs / FpsGovernor.cs）

---

## F-1（P2 · 性能/GC）：D2DRenderer.MeasureText 每码点字符串分配

位置：`源码/Play/D2DRenderer.cs` L694-726（`MeasureText`），调用方 `GamePanel.DrawBadge`（PaintCore 每帧 7 处调用）、Arcaea `_d2d.MeasureText` 等。

问题：宽表缓存命中时仍执行 `cp = s.Substring(i,2)` 或 `s[i].ToString()`——每个码点分配一个键字符串。绘图路径每帧 DrawBadge×~7、每徽章文本 6~16 码点 → 每帧 ~50~100 个短字符串分配（GC 增量）。引擎侧 `UiGlyphRuns` 已用 `int cp = char.ConvertToUtf32(...)` 零分配分类（`源码/Play/EngineUi/UiGlyphRuns.cs` L60-67），D2D 侧未照搬。

修复提案（~20 行）：缓存键改为 `Dictionary<int,float>`——单码点用 `(int)s[i]`，代理对用 `(high<<16)|low`；缓存命中路径零分配。语义（测量值）不变，`CHART_NO_CHARCACHE` 诊断门保留。

## F-2（P1 · 资源）：timeBeginPeriod/timeEndPeriod 不平衡（begin=2 / end=1）

位置：`源码/Play/GamePanel.cs`——ctor `try { timeBeginPeriod(1); }` + `RenderThreadLoop` 入口 `try { timeBeginPeriod(1); }`，仅 `Dispose(bool)` 一处 `timeEndPeriod(1)`。

问题：Windows 全局计时器分辨率是引用计数；GamePanel 释放后系统仍停留在 1ms 分辨率（省电/全局 CPU 影响），直到进程退出。只创建 1 个 GamePanel 时多 1 次 begin 永不复位。

修复提案：删除 RenderThreadLoop 里的 begin（ctor 已设；线程循环与 UI 同进程共享分辨率），或 begin/end 引用计数成对。3 行改动。

## F-3（P2 · 驱动冗余）：GamePanel 双入口 LoopTick（Idle + 1ms Timer）+ 渲染线程

位置：`源码/Play/GamePanel.cs`——ctor `Application.Idle += OnAppIdle;` 与 `_loopTimer`(Interval=1).Tick 都调 `LoopTick()`；另有 `_renderThread`(GameRender) 执行 `PaintCore`。`_idleLooping` 只防重入，不防双入口。

影响：
- LoopTick 触发频 ×2（Idle 消息空 + 每 1ms Timer）；`frameMs=0`（默认无限制档）时 `_lastIdlePaint` 限速不生效 → Invalidate 每 tick 一次，非播放态也走 UI 线程全量 PaintCore。
- `Step()` 有 `_lastStepMono` 保护——第二入口 stepDt≈0，但 `_kpsHistory`/弹道衰减步骤被双跑（大谱高频段轻微重复采样）。

修复提案：二选一保留（推荐保留 1ms Timer 驱动 LoopTick，Idle 仅作后备或移除）；`frameMs=0` 时也允许限速（如上限 1000Hz，显著降非播放态 CPU）。~10 行。

## F-4（P2 · 性能）：编辑器 Mania 密度柱 O(视窗小节 × 全音符) 每帧重算 + 每小节 new int[]

位置：`源码/Charting/ChartEditorPanel.cs` L4803-4839（DrawManiaPreview 内密度柱）。

问题：`for (mi=dm0..dm1) { var counts = new int[canvasKc]; foreach (var n in _ed.Notes) ... }`——每帧对所有音符全扫一遍，且每个可见小节分配一个新数组。6000 音符谱 × 可见 ~15 小节 × 33fps（预览播放时 30ms Timer 持续 Invalidate）≈ 300 万次迭代/秒 + 每帧 ~15 个数组分配；这是 t10「IF 卡死」之外编辑器大谱的第二个 O(n) 热点（t9/t10 均未覆盖）。

修复提案（~40 行）：按 `measureIdx = floor(time/measureMs)` 预建 `Dictionary<int, int[]>` 桶缓存（或按时间排序笔记 + 每小节二分界），在 MarkDirty/笔记增删改时失效；绘制时每小节 O(canvasKc) 直取。仅 Mania 路径。

## F-5（P2 · 冗余）：AiChartReview 同步包装用 Task.Run + GetResult

位置：`源码/Ai/AiChartReview.cs` L107-108、L119-121。

问题：t12 后方法体纯同步（`await Task.CompletedTask`），`ReviewFile/ReviewMeta` 仍 `Task.Run(()=>ReviewFileAsync(...)).GetAwaiter().GetResult()`——每次调用多一次线程池跳转 + 同步阻塞等待；UI 线程调用时无必要。

修复提案：同步包装直接 `return BuildDisabled(...)`（保留签名）；或删除同步方法统一 async/await 调用点。~6 行。

## F-6（P3 · 代码健康）：GamePanel.cs 存在 6.5KB/73KB 超长单行

位置：`源码/Play/GamePanel.cs` L1（6589 字符）、L3（72950 字符，占文件 1/3+）。

问题：单行含大量成员定义——git diff/blame、code review、编译器报错定位全部失效；已有 t 系列在此文件上多次修改，风险持续累积。

修复提案：仅做空白拆分（纯格式、零语义），用 C# 格式化器或机械化按 `} ` 断行，构建 + selfcheck + 试玩回归确认。低优先级、建议独立 commit 便于追溯。

## F-7（P3 · 诊断正确性）：EngineHosting 枚举未按进程过滤

位置：`源码/CoreUtil/EngineHosting.cs` L41-62。

问题：`EnumVisibleTopLevel` 枚举整个桌面会话所有顶层窗口（含其他应用窗口），`LogWindowState`（HostContent/UnhostContent/壳显示打点）与 `--wincount` 依据「可见窗口数=1」——用户桌面开着任意其它窗口（如开发环境/浏览器）即误报；且每次 HostContent/UnhostContent 都全桌面 GetWindowTextW（每会话 2~4 次，量小但易误导）。

修复提案（~12 行）：`GetWindowThreadProcessId` 过滤 `GetCurrentProcessId()` 仅计本进程窗口；「同窗承载=可见窗口=1」断言语义才成立。

## F-8（P3 · 观察）：`_screenFrameMs` 静态缓存永不失效

位置：`源码/Play/GamePanel.cs` `ScreenFrameMs()`。显示器变更（业务场景少见）后 VREFRESH 缓存过期；`FpsGovernor.ScreenRateProbe` 同样。仅记录，不优先。

---

## 优先级建议

| 编号 | 级别 | 建议 |
|------|------|------|
| F-2 | P1 | 立即修（3 行，资源/省电） |
| F-4 | P2 | 下一次大谱性能批次（编辑器密度柱缓存） |
| F-1 | P2 | 热路径零分配，随 D2D 改动批次（~20 行） |
| F-5 | P2 | 顺手清理（~6 行） |
| F-3 | P3 | 明确单入口后随重构修（影响面需试玩） |
| F-6 | P3 | 独立格式化 commit |
| F-7 | P3 | 诊断增强（12 行） |

结论：P0=0；F-2 建议 captain 直接派单（低成本高收益）；其余按批次排入。
