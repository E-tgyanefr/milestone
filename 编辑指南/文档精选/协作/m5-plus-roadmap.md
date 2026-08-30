# M5+ 引擎路线图规划（RhygeMaker · 前 M4 设计链已闭合——本文=后 M4 段增值产出）

> 起草：eng-design-vis（引擎策划）· captain 指令（t132 收讫 v4）：『停止重复收尾消息，转向可增值产出（如 M5+ 规划）』。
> 前提：M4（t132 设计）已冻结（§2 域库+§6.2 组件化+§6.3 M1-M30+§7 蓝本+§8 13 域+§11 拍板）；实现双线=t134（域库+CLI·eng-coder）+t138（游戏层·coder-vis）进行中。
> 定位：本文件=**M4.1 之后（M4.2-4.4 归并+引擎能力扩展+成品化）** 的里程碑骨架与缺口总表——供 captain 排期拍板；只做规划不写码。

---

## 0. 里程碑命名建议（M4.1 之后的三个台阶）

| 里程碑 | 主题 | 一句话 |
|---|---|---|
| **M5（下一里程碑）** | 域补全 + 游戏层 P1 + 编辑器 M3.5 | 把「原版对标」从 P0 核心链推到「编辑器+AI+数据」闭环；编辑器体验补齐（Unity 对齐项） |
| **M6** | 引擎能力扩展 | 让引擎从「能跑 Milestone」变「通用主流体验」：运行时 UI 树/3D 体验/渲染后端/音频/性能门 |
| **M7** | 跨平台 + 成品化 | V3-7 二期（GLFW/Vulkan/ALSA）+发布链（打包/示例库/教程）+联机子集评估 |

> M4.2-4.4（t132 §3.5 的 P1/P2 分块）**并入 M5/M6**（不再单独编号——减少里程碑碎片；§1 给出归并表）。

---

## 1. M5 = 域补全 + 游戏层 P1 + 编辑器 M3.5

### 1.1 域补全（C++ Rhythm.*——M4 设计内已签名，M5 实现补齐）

| 项 | 设计引用 | 说明 |
|---|---|---|
| story.hpp（Storyboard/Player） | §2.2 story | ResetTo/HookFired/HookRate——转场/演出（M25 主题转场复用） |
| practice.hpp（PracticeSession） | §2.2 practice | 练习模式段循环/幽灵/rate（M18 行） |
| validator.hpp（ChartValidator） | §2.2 validator | 30 项规则（t131 R-01..R-12 复用子集） |
| chartio 导入器（.osu/.adofai/.aff/.qua/.mc/.sm/.ssc） | §2.2 A-3 | ChartParser 家族映射表——曲库导入（M1 P1） |
| starter.hpp+dsp.hpp（Dsp/BeatAlignEngine） | §2.2 P1（A-7） | 校准页（M24）+起步谱生成（T7 引导） |
| 音频变速（WinMM 现状 TimeScale=1.0） | §3.3 X6 | 域 Rate 变速已定；音频侧=P1 评估（WASAPI 或 WinMM 再评估） |

### 1.2 游戏层 P1（原版对标推进——§6.3 行）

编辑器（M14/M15/M16）：六区编辑器 .mil 音符/事件编辑扩展 + 自动游玩（RunAuto+PlayController 快照复用）+ **AI 助手 t131 落地**（R-01..R-12+B 档——已设计待实现）。
数据/体验（M18/M19/M20/M21/M22/M23/M24）：练习/回放/皮肤+布局编辑器/段位（CourseData .mc）/玩家信息·我的数据/回环作曲/校准。
事件/多场（M5/M17）：事件系统（zoom/noteSpeed/scroll/Bezier=EvalEvents 端口）+多场同屏（StageSpec+多 Ruleset 实例）。

### 1.3 编辑器 M3.5 体验设计（Unity 对齐——本地 AI 评审员报告体验项）

| 项 | 状态 | 设计要点 |
|---|---|---|
| Inspector 折叠/引用选择器拖拽 | t129（P1·pending） | 折叠分组（Transform/Component 字段分组）+引用字段=拖拽/Hierarchy 指出选择器（Prefab 拖放=P1 注册项） |
| Play-Stop 运行时状态规则 | t129 | Play 中 Inspector 只读+运行时值着色（PlayController 快照语义）；Stop=快照还原（M3 已具） |
| 术语对齐（MA7·Unity 术语） | t129 | 类名/UI 标签英文（History 文档 MA7——中文=括注双语化 t125 覆盖） |
| SceneView 3D 视图 | t136（P1·已设计于 t126 §编辑器） | 2D↔3D 切换/中键 orbit·右键 pan·滚轮 dolly/地面网格+轴向线/相机组件跟随 |
| 中文/字体复验 | t125 后 | CJK 渲染复验（试玩视觉）+emoji 口径（引擎侧与游戏层一致） |

### 1.4 M5 验收（建议 10 项）

| # | 验收 |
|---|---|
| M5-1 | 域 story/practice/validator 测试绿（R-7/R-8 语义+30 项规则） |
| M5-2 | 导入器 5 家族样本导入→ChartData 一致性（.osu 家族对拍 legacy 解析） |
| M5-3 | 编辑器 .mil 编辑 round-trip（edshot 对比） |
| M5-4 | AI 助手 A 档 12 规则正确性+确定性；B 档关闭零 8080 请求断言（t131 验收） |
| M5-5 | SceneView 3D 视图走查（orbit/pan/dolly+相机跟随） |
| M5-6 | 练习/回放/皮肤/布局编辑器/段位/数据页行为 parity（§6.3 行对照） |
| M5-7 | 事件系统与多场同屏判定分场 parity |
| M5-8 | 校准页（Dsp）+起步谱生成（T7） |
| M5-9 | 回环作曲导出 .mil 一致 |
| M5-10 | 全链回归（引擎全绿+游戏层 P0 不回退） |

---

## 2. M6 = 引擎能力扩展（通用主流体验）

| 能力 | 现状（v3） | M6 目标 | 备注 |
|---|---|---|---|
| 运行时 UI 组件树 | 无（Editor 有 EditorUiKit；游戏层=页面栈自绘 Widgets） | DevLayer UI Kit（UiCanvas/UiLabel/UiButton/UiStack... 经 bind=RhygeMaker.Bind.Ui；IRenderable 基座复用）——**游戏层从页面栈平滑到组件树**（Unity UGUI 心智） | 决策 M6-1：UI 树=DevLayer 实现（C#）vs Core 组件（C++）——**建议 DevLayer C#**（性能够用+开发效率；引擎零膨胀） |
| 3D 体验 | Render3D（t126：软光栅+MeshRenderer/Camera3D+BlitRect） | 游戏层 3D 组件绑定（ms_render3d_* 或经现有组件通道）+编辑器 3D（t136）+3D 黄金帧 hash 三语言 | M6-2：3D 渲染后端（D3D11 优先——V3-4 二期声明） |
| 渲染后端 | 软渲（Win32） | **D3D11 原生后端**（IRenderer 第二实现——接口已可插拔；V3-4） | 2D parity 保持不变（确定性=软渲黄金帧）；D3D11=性能面 |
| 音频 | WinMM mci（TimeScale=1.0） | WASAPI（低延迟+变速采样）→AudioClock 采样驱动完整化（C9 v1 语义） | M6-3：WASAPI 后端=Platform 第三实现 |
| 性能门 | 无 PerfGate（v3） | PerfGate（自研——热路径 0B/帧耗断言；基线=优化矩阵 O1 的 v3 版） | M6-4：与压测链（stress-matrix 参考）挂接 |
| 物理/着色器 | 范围外（声明延续） | **保持范围外**（t92 不做项）——不因 M6 破例 | 红线宣示 |
| 异步 | P2（加载/场景切换） | M6 末尾评估（coroutine/jthread+EventBus——t116 已具部分） | M6-5 |

### M6 验收（要点）
回归全绿+软渲 parity 恒等（新增后端=黄金帧一致性可选）+PerfGate 基线成型（0B 热路径目标）+UI 树 cookbook（组件化 UI 场景示例）。

---

## 3. M7 = 跨平台 + 成品化

| 项 | 内容 | 备注 |
|---|---|---|
| 跨平台第二实现 | GLFW（窗口/输入）+Vulkan（渲染）+ALSA（音频）——接口已就绪（V3-7 二期） | 逐平台自检（Linux 冒烟：窗口+软渲+音频） |
| 发布链 | 打包（PackExport 参考）/示例库（cookbook 全量）/文档站（README+guide+checkdocs 门禁——t106 参考） | 与四目录迁移清单（§9 复检：BOM/路径/时间戳）挂钩 |
| 联机子集 | PlatformSockets（v1 参考）+零三方最小子集——**另立决策（B-8）** | M7 评估；不做进主流红线 |
| 教程/模板 | milestone-engine 模板（参考 C# 引擎 templates）→v3 版 | cookbook 教学化 |

---

## 4. 引擎能力缺口总表（M5-M7 横切——设计/实现归属）

| 缺口 | 优先级 | 归属 | 现有基础 |
|---|---|---|---|
| 运行时 UI 树（DevLayer） | M6-1 | M6 | EditorUiKit+IRenderable+ms_text_* |
| D3D11 后端 | M6-2 | M6 | IRenderer 接口+软渲（parity 面不动） |
| WASAPI 音频 | M6-3 | M6 | IAudioBackend 接口+WinMM |
| PerfGate | M6-4 | M6 | 优化矩阵（c# 参考）+stress 链 |
| 跨平台（GLFW/Vulkan/ALSA） | M7 | M7 | V3-7 声明+接口 |
| 联机子集 | M7 评估 | M7 | PlatformSockets（C# 参考） |
| 中文 ttf 字体 | P1 注册项（M3-a） | M5 后 | CjkFont（GDI 光栅）——ttf=白名单候选（决策） |
| 拖放 Prefab/引用选择器 | P1 注册项 | M3.5（t129） | — |
| 画面特效（粒子/转场） | P2 | M6 | FxParticles（C# 参考）→v3 建议=IRenderable 粒子组件 |

---

## 5. 主线对齐检查（主流引擎剩余项——t92 D1-12 对照）

| 主流语义 | v3 现状 | 待办（M5-M7） |
|---|---|---|
| UGUI 组件树 | 无 | M6-1 |
| 粒子系统 | 无 | M6（IRenderable 粒子组件） |
| 音频（剪辑/总线/3D） | 后端+时钟 | M6-3 扩展（剪辑表+总线） |
| 物理 | ✅ 范围外声明（t92 不做项延续） | 不办 |
| 着色器/材质 | 范围外（软渲=光栅） | 不办（M6-2 后评估——D3D11 材质=候选 M7） |
| 场景加载/转场 | ✅ SceneManager+Storyboard | 完成 |
| 资产管线（导入/烘焙） | AssetDatabase+容器 | M6 扩展（导入器家族） |
| 脚本（C# 组件+序列化字段） | ✅ | 完成（scriptFields 闭环） |
| 多平台 | 接口就绪 | M7 |

---

## 6. 风险与诚实声明

1. M6-1（DevLayer 运行时 UI 树）是**最大的一块引擎扩展**（≈编辑器 EditorUiKit 的 C# 移植量+布局/主题/事件）；若 captain 希望游戏层先跑→M5 游戏层 P1 可继续用页面栈（M6-1 不阻塞）。
2. M6-2 D3D11 与软渲确定性并存=双渲染面政策（软渲=确定性 parity 面——与 M4-X 命令录制回放同构；D3D11=窗口性能面）——**不破**黄金帧。
3. M7 跨平台=多链 CI（MSVC/Clang/GCC 已验证 → Linux 冒烟）成本约 2-3 人月——建议 M7 单独立项（captain 拍板）。
4. 零第三方红线：M5-M7 全部项目=自研/系统库（WASAPI/GLFW 例外=Platform 后端可插拔——GLFW 为跨平台窗口系统=接口实现白名单申请点（决策 M7-1））。

---

## 7. 决策点（captain 拍板）

| # | 决策 | 建议 |
|---|---|---|
| M5-1 | M5 里程碑范围确认（域补全+游戏层 P1+编辑器 M3.5） | 采纳（一次里程碑=对标闭环 + 编辑器体验） |
| M5-2 | t129/t131/t136 并入 M5（编辑器 M3.5 批） | 采纳（不另开里程碑；t129/t131/t136 即 M5 子任务） |
| M6-1 | 运行时 UI 树=DevLayer C# 实现 | 采纳（引擎零膨胀；性能=IRenderable 软渲足够——dense 10^3 音符级 UI 无压力） |
| M6-2 | D3D11 后端 | 采纳（性能面；parity 面=软渲不变） |
| M6-3 | WASAPI 音频后端 | 采纳（变速+低延迟——练习 rate 完整化） |
| M6-4 | PerfGate 基线 | 采纳（0B 热路径断言；与引擎压测链同） |
| M7-1 | GLFW 白名单申请 | 建议**例外白名单**（跨平台窗口系统=平台抽象必须；与 ImGui 候选分开决策）——captain 拍板 |
| M7-2 | 联机子集 | 建议**M7 评估后另立**（不并入 M5-M7 主线） |

---

*终稿*（M5+ 规划：M5=域补全+游戏层 P1+编辑器 M3.5（t129/t131/t136 并入+验收 10 项）→M6=引擎能力扩展（UI 树 C#/D3D11/WASAPI/PerfGate+3D 体验）→M7=跨平台（GLFW/Vulkan/ALSA）+成品化+联机评估；缺口总表+主线对齐检查+风险+M5-1..M7-2 决策点；待 captain 拍板后转设计文档（t-code 命名））。
