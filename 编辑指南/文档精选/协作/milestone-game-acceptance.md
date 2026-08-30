# Milestone 重制 · 游戏构造验收清单（eng-design · CAPTAIN 任务令应答）

> 起草：eng-design-vis（引擎策划）· CAPTAIN 任务令：引擎冻结（全体仅报告不修改——修复需 captain 报用户批准）→ 以引擎为准构造 Milestone（一切经引擎 ABI——ms_* 函数族/域语义/引擎壳 host——**禁止游戏层自造替代逻辑**）。
> 本清单=游戏构造的验收标准（M 表逐项）+回归夹具+分工判据——供 coder-vis（实现/harness）、引擎使用者（首测）、试玩（验收流）、captain（放行）直接引用。
> 引用基线：t132-m4-design.md §6.3（M1-M30 矩阵）/§3.6（G-1..G-15）/§8.1（P0 链）/§13.3（语义差异表=G-1 核对清单）/§16-§17（G-1 首验 441/441+R-5 Y 冻结）；引擎产物 canonical 哈希=基线不变（冻结令）。

---

## 0. 纪律验收项（每项=必答：是/否+证据）

| # | 纪律项 | 验收 |
|---|---|---|
| D-1 | 引擎哈希不变 | canonical 锁（scripts\build-lock.sha256 现行清单）与基线一致——构建产物哈希 diff=空 |
| D-2 | ABI 唯一实现面 | 游戏层逻辑全部经 ms_*（ms_rhythm_*/ms_input_*/ms_assets_*/ms_engine_* 等——以 ms_bind.h 现行清单为准；captain 口径 23 函数族）+ 域语义；**禁止**：游戏层重写判定/计分/谱面解析/时间轴/排序（自造替代逻辑=拦截项） |
| D-3 | 引擎 bug 报告通道 | 游戏侧发现的引擎 bug=**报告**（captain→用户批准）——不自行修改；报告模板=现象/最小复现/ABI 入口/建议 |
| D-4 | 改动范围 | 全部游戏层改动位于 rhygemaker\dotnet\Milestone.Game（+ 仅必要时的 游戏源码 兼容层）——引擎树零改动 |

---

## 1. M 表验收标准（游戏项 M1-M13；M14-M16 编辑器=编辑链无关项——试玩不测[仅保游戏互操作]）

> 标注：ABI 面=实现必须使用的引擎/域接口；判据=通过标准；证据=验收产物。

| # | 功能 | 标准（可观察） | 验收法 | 判据 | ABI 面 | 证据 |
|---|---|---|---|---|---|---|
| M1 | 曲库 | 扫 ChartsFolder（.mil 全库 24 样本可载入——含 多模式/段位——parse-noted+Warning 不失败）；空库文案 | 引擎使用者首测（证据先行）+cod 冒烟 | 载入数=24/24（corpus）；空库=文案 | ms_rhythm_chart_load（经 MSASSET 容器）+ms_assets_* | 首测日志+计数 |
| M2 | 选歌 | 模式分类 chips/卡片（曲名·作者·难度·徽章）/单击选中/双击游玩/▶✏🎲/空库文案 | 试玩验收流 | 操作流=原版（单击→选中高亮；双击→游玩页） | ms_rhythm_chart_meta+页面栈（引擎壳 host） | 走查+截图 |
| M3 | 游玩（10 模式） | 四族渲染（Lane/Line/Ring/Path）+音符滚动/HiT 线/判定区（组件化 PlayScene：LaneRenderer/NoteSpawner/JudgementZone/MusicDriver——t132 §6.2/§9） | G-1 harness+试玩视觉 | 渲染非空+2 帧差异>0（窗口面）；组件序=八回调+IRenderable 序 | ms_rhythm_render_field+ms_rnd_*+ms_component（场景组件 ABI） | harness 输出+截图 |
| M4 | 判定 | 四档判定+tier 计数+热换口径与域一致（Evaluate 语义=§13.3 差异表——游戏层不重写） | G-1 harness（域链 v3 headless vs 游戏域链——同 ABI） | **≥95%**（captain 判据——G-1 主门） | ms_rhythm_tracker_*+ms_rhythm_preset | harness res.json |
| M5 | HUD | 判定字/连击/ACC/进度显示=引擎壳 host 绘制（ms_text_*+ms_rnd_*——不重写渲染） | 试玩走查+引擎使用者数值抽查 | 数值=Tracker/ScoreBoard 直读（无后备自算） | ms_rhythm_tracker_result+ms_text_* | 截图+数值表 |
| M6 | 跳过开头空白 | S 键/_firstNoteTime≥3000 门限（原版语义）→Seek | 试玩走查 | 门限=3000ms；S 触发=Seek+时钟同步 | ms_engine_resize? 否——AudioClock（ms_audio? / 引擎壳）| 走查 |
| M7 | 暂停/继续 | Space/P 暂停：音频暂停+时钟冻结+恢复 | 引擎使用者首测 | 暂停=判定时钟零推进（抽查 3s 暂停=0 判定变化）；恢复=连续 | MusicDriver（AudioClock 锚 经 ABI） | 首测日志 |
| M8 | 重开 | R 键：Tracker/Board/时钟复位+Seek(0) | 引擎使用者首测 | 重开后=同谱重跑（判定计数归零） | ms_rhythm_tracker_result 复位路径 | 日志 |
| M9 | 结果 | 结算：Score/ACC/Combo/评级（Rank 映射=游戏层对等实现——§16.1 双口径：**成绩/ACC/评级=为游戏层对等（差异表 §13.3 其余行）——严禁重写判定/计分核心**） | G-1 harness 结算段+试玩 | Score/ACC 与 harness 一致；TP/Rank=双口径对等表（原版公式端口——注释引用） | ms_rhythm_tracker_result（board 真值） | res.json+截图 |
| M10 | 快捷键 | Space/P 暂停·R 重开·A 自动·S 跳过·T 斜轨（P2 标注）·V 3D（P2）·M 换部（P1）·Esc 退出 | 试玩走查逐键 | 全键=动作表（ModeInfo.KeyHint→动作）；Esc=退出干净 | ms_input_*（vk 直通——t137 G2 已具） | 走查记录 |
| M11 | 设置（P0 子集） | 判定档（热换=SetProfile 经 ABI）/键位（KeyHint 映射）/界面（分辨率+刷新率）/音量——9 分区= P1 全量 | 引擎使用者首测 | 档位切换=即时生效（no restart） | ms_input_*+窗口/视图 ABI+AppConfig（ms_assets_save_text） | 首测 |
| M12 | 曲库管理页 | 扫描/刷新/打开目录/复制路径（导入=P1） | 引擎使用者首测 | 目录操作=系统壳（引擎窗 host）提供 | ms_assets_* | 首测 |
| M13 | 全局快捷键/导航 | 页面栈导航（主菜单→选歌→游玩→结果→返回） | 试玩验收流 | 返回链=原版行为（结果页 R/Enter 重试·Back 返回·Esc 菜单） | 引擎壳 host 页面栈 | 走查 |

---

## 2. G-1 端到端 harness 验收（coder-vis 建——本次任务令 ②）

- **口径**：同谱同偏移——游戏域链（Milestone.Game 经 ms_rhythm_* 驱动）vs **原版 JudgementEngine autoplay**（游戏源码/源码/Play/JudgementEngine.cs 冻结基准）→ **判定总数/ACC/得分/评级/时间分布 1:1**；**判据 ≥95%**（captain）。
- **覆盖**：10 可用模式（Mania4-8/Phigros/Arcaea/Cytus/osu std/ADOFAI×2/IIDX/maimai）×corpus 代表样本（示例 6+测试格式 7——全键已入库样本）+自动输入（RunAuto 语义）。
- **夹具**：同谱=corpus 24 样本（tests/data/mil——ASCII 化）+mapped 到游戏模型（经 ms_rhythm_chart_load——禁止游戏侧复解析）；同偏移=AudioOffsetMs 直读。
- **差异表核对**：§13.3（档名统一四档族=显示层映射——harness 归一化对比；窗值近似/Adofai 公式=已知差异——**harness 必须输出逐项 diff 行**（tier 计数/ACC 差/得分差/时间分布桶）——而非只有聚合分）。
- **输出**：res.json（每格：judgement 计数/acc/score/时间分布桶/通过率）+per-item diff 表（§13.3 行核对=G-1 数据源）。
- **放行判据**：≥95% 通过；且差异明细=全部落入 §13.3 已知行（新差异=引擎 bug 通道报告）。

---

## 3. 回归夹具（验收/回归共用——同一事实源）

| 夹具 | 路径 | 用途 |
|---|---|---|
| corpus 24 | 渲染树 tests/data/mil（ASCII 化）+输出产物/bin/Chart/* | 曲库 24/24 载入+harness 同谱 |
| 示例 6 | 输出产物/bin/Chart/Milestone示例/ | 试玩双击体验流（Mania/Phigros/Arcaea/Cytus/IIDX/多模式） |
| 段位 3 | 输出产物/bin/Chart/段位文件/ | M1 载入+harness 拓展 |
| g1_parity | 域侧（441/441=100% 已验） | G-1 判定语义子门（回归=重跑不变） |
| 判定参考 | 游戏源码/源码/Play/JudgementEngine.cs（冻结基准）+JudgeSettings.cs | G-1 harness 基数（同谱同偏移） |
| canonical 锁 | scripts\build-lock.sha256 | D-1 引擎不变=基线 |
| 既有测试 | ms_test（core/platform/bind/editor/render3d/rhythm——19/19）+CsTestRunner+python unittest | 回归全绿（引擎回归=captain 外跑——游戏侧不得触发引擎重建） |

---

## 4. 角色分工判据（CAPTAIN 派单对应）

| 角色 | 本清单用法 |
|---|---|
| coder-vis | M1-M13 标准=实现目标；G-1 harness=§2 口径+res.json 交付；缺口实现=按 M 行 ABI 面 |
| 引擎使用者 | 首测证据=§1 验收法的「证据」列（日志/计数/截图——证据先行） |
| 试玩 | 验收流=双击体验流（M2→M3→M7→M8→M9→M10 逐键）+中文显示查证（t125 口径：无 �/无重叠） |
| eng-design | 本清单维护=活文档（M 行判据随实现调整=仅附加值/澄清——**不改冻结条款**） |
| eng-coder | 冻结待命——收到「引擎 bug 报告」才动（captain 批准后） |

---

## 5. 拦截项（任一=不通过）

1. D-1 引擎哈希变化 / D-2 游戏层自造替代逻辑（重写判定/计分/谱面解析/时间轴——**P0 拦截**）
2. G-1 harness <95%（未过 captain 判据）或差异明细出现 §13.3 以外新差异（未走引擎 bug 通道）
3. M1-M13 任一=不可操作（载入失败/游玩断链/结果不达/快捷键无响应/暂停无效/重开无效/中文乱码）
4. 引擎树被修改（任何 .hpp/.cpp/.cs 引擎文件 mtime/哈希变化——除 captain 批准通道）

---

*终稿*（游戏构造验收清单：纪律 D-1..D-4+M 表 M1-M13 逐项（标准/验收法/判据/ABI 面/证据）+G-1 harness 口径（≥95%+逐项 diff=§13.3 数据源）+回归夹具表（corpus 24/g1 441/判定参考/canonical 锁）+角色分工+拦截项 4 条——供 CAPTAIN 派单三执行的唯一验收事实源；活文档维护=eng-design）。
