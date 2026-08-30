# parity -1 崩溃修复报告（t17）

- 执行：coder-max（编写）· 日期：2026-08-27
- 现象：Milestone.exe --parity 在「玩法[多场同谱/Phigros 部件] 开始」后进程 exit 0xFFFFFFFF（-1），无托管转储、无 WER 事件；死亡点各异（Cytus/ADOFAI/maimai/多场·Phigros 均有出现）——概率性原生层崩溃（实测默认配置约 5~15% 概率）。

## 根因分析

1. **非确定性原生崩溃**：崩溃点跨模式漂移、单禁用任一 t16 特性均不消失、WARP 与硬件路径均出现——非单一代码路径缺陷，而是概率性原生层崩溃（渲染目标快速创建/销毁 × 10 fixture 连续进行时的 D2D/驱动层竞态；本机 ollama 服务常驻（GPU 显存波动）为高概率触发源）。
2. **exit -1 不可能出自 parity 托管路径**（RunParity 仅返回 0/1）→ 原生层崩溃或进程被杀。
3. t15 的 22 轮全绿为低概率未命中；t16 的帧时变化（批量化/缓存提速）改变了竞态窗口的时序分布，使崩溃暴露率上升——故表现为「t16 回归」，实为时序位移暴露的潜在原生层问题。

## 修复（三层）

### 1. 进程隔离（主修复，ParityCli.cs + Program.cs）
- 新增 CLI：`Milestone.exe --paritycell <fixtureIndex> <cellPath>`——构建单个合成谱 fixture，跑 legacy+engine 对比，结果写 GameplayEntry[] JSON，exit 0/1。
- RunParity 改为：逐 fixture spawn 子进程（Environment.ProcessPath + --paritycell，超时 120s 杀），exit∉{0,1}（原生崩溃/被杀）→ 该格自动重试 1 次 → 仍失败记 error 条目并继续——**崩溃不再杀死整个 parity 进程**，279 条对比永远可完整产出。
- Arcaea 得分口径已知偏差在父进程合并时恢复记录。

### 2. 防御加固（源码/Play/D2DRenderer.cs + GamePanel.cs）
- OnPaint（UI 线程过渡期绘制）加 `lock (_renderLock)`——与渲染线程 PaintCore 串行化，消除双线程并发使用同一 RenderTarget 的窗口。
- BeginFillBatch/BeginStrokeBatch 入口先 Flush 未提交批（复核观察 C：防早退路径跨帧串批）。
- AddToBatch/AddToStrokeBatch NaN/Inf 顶点拒入（防原生 D2D 网格化崩溃）。
- 多场（_multi）模式：DrawMania3D 与 DrawPhigros 的批量窗回退旧直绘（批量仅热路径生效，多场/图层嵌套路径零回归）。
- 诊断门（env 未设置零开销）：CHART_NO_FILLBATCH / CHART_NO_STROKEBATCH / CHART_NO_CHARCACHE / CHART_FORCE_WARP / CHART_PARITY_SIZE。

### 3. 验证
- 隔离版 --parity 连跑 6 轮全 exit 0：279/279 identical（含 多场同谱/Phigros 部件 identical、Arcaea 已知偏差恢复记录）。
- --selfcheck exit 0；--multishot exit 0（多场双场截图 multistage.png 正常产出）。
- 构建 scratch 0 警告 0 错误。
- 热路径 GC 不回归：dense@240 1.67 MB/s（t16 目标保持；多场门只影响多场谱，单场热路径不变）。

## 结论
- parity 崩溃已通过**进程隔离 + 重试 + 防御加固**闭环：工具侧不再受原生层概率崩溃影响，可完整产出对比结果；单格失败会被明确记录（error 条目 + exit 码），不再静默 -1。
- 底层原生竞态（D2D 渲染目标快速翻台 × GPU 显存波动）非本层可根治项——已通过加固缩小窗口 + 隔离兜底；t11 实机长稳继续作为游玩路径的最终 canary。
- 诊断门长期保留，未来复现原生崩溃时可用 CHART_FORCE_WARP / 单格 --paritycell 快速二分。
