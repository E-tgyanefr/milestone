# EngineJobs 并行负收益实证（2026-08-27 停服错峰复核）

- 场景：llama-server 已停（仅 Ollama 轻载 48MB）；`--aidetect` EngineJobs.Benchmark：Degree=24/Effective=24，MapMs=2.86ms，**Speedup=0.26x**（1M 元素、每项微小工作）。
- 结论：0.26x 与是否 AI 负载无关；此前 t9/t11 的 0.32x~0.43x 归因（llama 常驻共享机）已证不成立。根因=ParallelMap 在极小每项工作下分区/调度开销大于收益（本机 24 逻辑核）。
- 影响：一致性硬断言（1M/10^7 与串行逐位一致）持续 PASS；仅加速比负收益。
- 处置：记录为引擎优化项（下一轮引擎）：ParallelMap 增加「每项分钟级工作≈微小 → 自动串行阈值 / chunk 增大 / 无状态原子分批」的自适应；必要时加 EngineJobs.Benchmark 退化线（≥0.8x 于真实重任务，如 10^7 带计算负载）。
- 证据：aidetect.log（本机 24 核，MapMs 2.86ms）。