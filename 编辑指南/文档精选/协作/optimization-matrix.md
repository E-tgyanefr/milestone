# 全项目优化矩阵（设计）：性能/代码质量/启动分发/体验（optimization-matrix）

> 起草：eng-design-vis（引擎策划）· 任务 t101 · 用户：对所有项目优化。
> 产出：四维度 ×【现状/目标/动作/owner/验收】+ 优先级 P0-P3 + 预计工作量。只做设计不写码。
> 现状快照：t101 时刻实测（GamePanel.cs=3261 行/单行最大 52,017 字符；目录版 Milestone.exe=9.2MB(+deps)；已知发布单文件参考值=游戏 156MB/引擎工具 64.7MB（以发布目录为准）；热路径基线=0B 分配/12000FPS；长稳=60min +20MB 稳定）。

---

## 0. 总原则

1. **基线锁定优先**：引擎热路径 0B/12000FPS 为红线（每批回归不得劣化 >30%——EnginePerfGate）。
2. **零行为变更**：所有优化=结构性（拆分/AOT/缓存/改名），行为不变（parity/EngineChecks/试玩视看=回归闸）。
3. **优先顺序=P0（红线/门禁）→P1（体验/质量面）→P2（打磨）→P3（远景）**；owner 三线（引擎 eng-coder-vis / 游戏层 coder-vis / captain 构建验收；试玩&引擎使用者=验证员）。

---

## 1. 性能（Performance）

| 项 | 现状 | 目标 | 动作 | owner | 验收 | 优先级 |
|---|---|---|---|---|---|---|
| P-1 引擎热路径 | 0B/次分配·12000FPS·帧 P99 1.1ms（t9/t11 验证） | **锁定基线**（≥现有，劣化 >30%=FAIL） | **EnginePerfGate**：EngineChecks 热路径断言（判定/UI测量/字体缓存 0B 基准）+压测单元格回归（stress-matrix 子集每批跑） | eng-coder-vis | 每批基准对比表（P99/分配/吞吐）；劣化>30% 即阻止合入 | **P0** |
| P-2 启动时间 | 工具单文件 64.7MB/冷启动（参考）；游戏 156MB | 引擎工具 AOT ≤30MB、启动 ≤1.5s（SSD） | AOT/裁剪 profile（PublishAot+裁剪）+ReadyToRun（JIT 版默认保留）+懒加载（音频/谱面/编辑器模块延后） | eng-coder-vis | build-tool.ps1 加 AOT 档；体积/启动断言（stopwatch 冷启动≤1.5s） | **P0** |
| P-3 内存/GC | 长稳 60min +20MB（<100MB 红线）；GC 12.5MB/s WARN（SG-02，E4 已修） | 长稳 +≤30MB/60min；GC ≤5MB/s（热路径） | **MemoryGate**：压测采样断言（WS 增长/GC 率）；回归=stress-final 对照 | eng-coder-vis+coder-vis | 60min 长稳复跑对照（增长/GC 双达标） | P1 |
| P-4 渲染（Editor/游戏） | Editor 脏渲染已（t37 基）；游戏 D2D 池化后（t16） | Editor 帧 P99 ≤8ms；游戏维持 | Editor 视图网格/参考层缓存+Pick 命中优化（场景树→视图高亮） | eng-coder-vis | Editor PerfTest 帧耗断言；试玩视看无卡顿 | P2 |

## 2. 代码质量（Code Quality）

| 项 | 现状 | 目标 | 动作 | owner | 验收 | 优先级 |
|---|---|---|---|---|---|---|
| Q-1 警告清零 | CS1584（XML cref 缺参引用）/IL3002（AOT 单文件警告）残留 | 双工程 0 警告 0 错误 | 修复 CS1584（doc cref 全修）；IL3002 分类处理（可修=修；不可修=Suppress+注释理由） | eng-coder-vis | 双工程（引擎/游戏）构建 0 警告 0 错误（CI 断言） | **P0** |
| Q-2 巨型文件拆分 | GamePanel.cs=3261 行/单行最大 52,017 字符 | 每行 ≤200 字符；文件拆分（partial 分区） | 格式化（行宽）+按域拆分 partial（Play/Hud/Audio/Input/MultiStage 区）；ParityCli 回归闸 | coder-vis | 0 行为变更（parity/selfcheck 绿）；行宽断言（100% 文件 ≤200） | **P1** |
| Q-3 XML doc 存量+PublicAPI 门禁 | SDK 新增面覆盖；存量类缺 | doc 覆盖 ≥80%；公开类型 100% 在门禁 | 批B 存量补全+PublicAPI 生成器校验（枚举程序集公开类型 vs 清单=漂移断言） | eng-coder-vis | 门禁断言 100% 通过；doc 抽查 20 类 ≥80% | P1 |
| Q-4 命名空间分层 | v2 新码 MilestoneEngine.*；存量 ChartPlayer | 核心零 Rhythm 引用；分层落位 | t92 D10/批B 段2（codemod+双门禁反射断言+csproj 排除 Rhythm 目录） | eng-coder-vis | 核心程序集无 Rhythm 类型（门禁绿）；全链绿 | P2 |

## 3. 启动与分发（Startup & Distribution）

| 项 | 现状 | 目标 | 动作 | owner | 验收 | 优先级 |
|---|---|---|---|---|---|---|
| D-1 AOT 20-30MB 路线 | 64.7MB 参考（引擎工具） | 引擎工具单文件 ≤30MB（AOT 档） | PublishAot profile+裁剪+Native 层（t88 批C 联动）；build-tool.ps1 参数化 -aot | eng-coder-vis | AOT 单文件 ≤30MB+运行冒烟（--check/--tool） | **P0** |
| D-2 两形态发布 | build-tool.ps1 已有（目录版+单文件） | 双形态+版本化 | 加 AOT 档+version.json/CHANGELOG 自动生成+产物校验（dll 时间戳哨兵已有） | eng-coder-vis | 发布目录 README/版本一致；t111 首跑验证 | P1 |
| D-3 游戏单文件 | 156MB 参考 | 优化（P3：裁剪审计；非本轮阻塞） | 清单审计（依赖/dll 裁剪可行性） | coder-vis+captain | 后续专项 | P3 |

## 4. 体验（Experience）

| 项 | 现状 | 目标 | 动作 | owner | 验收 | 优先级 |
|---|---|---|---|---|---|---|
| E-1 编辑器快捷键/菜单/布局记忆 | 布局固定；无快捷键（Play 工具栏点选） | Unity 味：W/E/R 变换工具（P1 后置：拖拽）、Play/Stop/Pause、Ctrl+S 保存场景；布局 F 键+持久化（AppConfig） | t90 P1/P2 实施（MA7 术语+格局齐）；布局记忆=配置存 JSON | eng-coder-vis | 试玩走查 8 项（快捷键/菜单/布局记忆/保存） | **P0（编辑体验主线）** |
| E-2 cookbook 完善 | 6 篇（A-F） | 12 篇（补 G 世界 API/H 资产/EDITOR 使用） | 补 6 篇+每篇可编译运行断言（EngineChecks 模板化） | eng-coder-vis | cookbook 全编译+运行断言绿 | P1 |
| E-3 文档一致性 | README/guide/API doc 分散 | 术语表（Unity 味：Hierarchy/Inspector…统一文案）+文档校验脚本（命令表/链接/术语漂移=0） | 术语表文档+checkdocs.ps1（命令 13 条/链接/术语核对） | eng-coder-vis+captain | 校验脚本漂移=0；试玩抽查 | P1 |
| E-4 快捷方式/安装 | 解压目录直用 | 双击入口+快捷方式（可选安装器） | 安装脚本（复制布局+快捷方式创建）；文档说明 | coder-vis | 安装后双击可达（试玩验证） | P2 |

---

## 5. 汇总与排期建议

| 优先级 | 项 | 预计工作量 | 建议排期 |
|---|---|---|---|
| P0 | P-1 热路径 Gate / D-1 AOT / E-1 编辑器体验 / Q-1 警告清零 | 引擎侧 ~2-3 周（与 E1/t93 并行） | 立即（与 Editor E1/E2 同步） |
| P1 | P-2 启动/ P-3 MemoryGate / Q-2 GamePanel 拆分 / Q-3 doc+门禁 / E-2 cookbook / E-3 文档一致性 | 游戏层+引擎 ~2-3 周 | 紧随 P0；GamePanel 拆分=游戏层主线（t95+ 候选） |
| P2 | P-4 Editor 渲染 / Q-4 命名空间分层 / E-4 安装器 | ~1-2 周 | 阶段 C/D 后 |
| P3 | D-3 游戏单文件 / 物理·着色器（范围外预研） | — | 远景 |

**风险控制**：所有动作=零行为变更（回归闸=EngineChecks/parity/试玩视看）；AOT 档与 JIT 版并行（默认 JIT 保稳定）；GamePanel 拆分用 partial+格式化脚本（保 parity 绿）；基准劣化门禁=合入硬门槛（P-1）。

---

## 6. 关键决策点（供 captain 拍板）

| # | 决策 | 建议 |
|---|---|---|
| O1 | 基线锁定 | EnginePerfGate（热路径断言+压测网格子集每批回归）——P0 硬门槛 |
| O2 | AOT 档 | 引擎工具 AOT ≤30MB 目标；JIT 默认/AOT 可选双档发布 |
| O3 | GamePanel 拆分 | 格式化+partial 分区（0 行为；parity 回归闸）；行宽≤200 |
| O4 | 编辑器体验 | 快捷键/菜单/布局记忆=编辑体验主线（与 Editor E1-E4 同步） |
| O5 | 文档一致性 | 术语表（Unity 味）+checkdocs.ps1（命令/链接/术语漂移=0） |
| O6 | 优先级 | P0 三件（热路径 Gate/AOT/编辑器体验+警告清零）先行；其余按第 5 节 |

---

*终稿*（t101 设计交付；四维度矩阵+优先级/工作量/O1-O6；未写代码；等待 captain 拍板后派（P0 建议即派 eng-coder-vis（引擎）与 coder-vis（Q-2 游戏层）））
