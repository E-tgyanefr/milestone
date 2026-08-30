# t5 预研：三栏布局改造点 + t1 P0/P1 宿主修复坐标速查（coder-max）

> 性质：t5（游戏层实现）执行前的预研速查。所有坐标已在当前源码逐一验证。
> 依据：其他/docs/协作/editor-trilab.md（t2 规范）、其他/docs/协作/engine-design-t3.md（t3 引擎设计）、构建产物/obj/diagnosis/诊断报告.md（t1）。

## 一、编辑器三栏布局改造点（ChartEditorPanel.cs，已核对）

### 现状结构
| 区域 | 代码位置 | 现状 |
| --- | --- | --- |
| ctor 装配 | :204-221 | 顺序 dock：_canvas(Fill,index0) → _toolbar(Top,AutoSize) → _animPanel(Right,320)。注释 :215-219 警告**不得 BringToFront 工具栏**（会遮画布） |
| 右侧 _animPanel | :137, :226-253 | Panel Dock=Right Width=Ui.P(320)；TabControl _rightTabs 4 Tab（事件/判定线/音符/Arcaea），ItemSize=78×26 |
| Tab 内容 | :245-253 NewTabFlow | 每 Tab 一个 FlowLayoutPanel AutoScroll（复用模式） |
| 工具栏 | :1318-1540 BuildToolbar | FlowLayoutPanel Dock=Top AutoSize WrapContents；组卡片 G(title)（:1369-1388），六组：①谱面:1398 ②音符 ③事件 ④AI与校准 ⑤音频·运输 ⑥视图；_status 在 :1534-1535 |
| 画布 | :136, :208, :3906 | EditorCanvas 内嵌类，Dock=Fill，全部绘制/交互不动（规范 §1.1 中栏零改动） |
| 试玩 | :2815-2821 PlayTest | BuildChart→Parts=null→TestPlay?.Invoke(chart, dir) |
| TestPlay 事件 | :202 | 宿主接线 MainForm.cs:1113-1133（LoadAndPlay + HostContent(_game) 同窗承载） |
| 快捷键 | :7850+ EditorCanvas.OnKeyDown | **F1-F12 全部未占用**（grep Keys.F 0 处）→ F5/F6/F8/F9/F10 可直接新增 |
| 折叠持久化 | AppConfig.cs | 现有字段 :11-46；需新增 editor.PanelLeftMode/PanelRightMode + 最近谱面（近8条） |

### 改造方案（按 editor-trilab §1）
1. ctor 新增 _leftPanel（Dock=Left，宽 clamp(round(W×200/1280),160,240)）与 _canvasHost（Panel Dock=Fill 承载 _canvas）；_rightPanel=_animPanel 改宽 clamp(round(W×300/1280),240,360)。
2. BuildToolbar 组①（谱面:1398-1418：新建/打开/属性/保存/导出×3/模式/部件/多场）+组②（音符模板）拆入 _leftPanel；组③事件、组④AI与校准拆入右栏新 Tab；组⑤播放类按钮进「预览」Tab；组⑥视图+保存/撤销/试玩/主菜单+_status 留工具栏（Ht≈96）。
3. _rightTabs 扩为 6 Tab：属性/事件/判定线/音符/预览/AI（ItemSize 78×26 → 自适应 ~72×24；300px 放 6 个会超宽 → 需 SizeMode=Normal 或 ItemSize≈46×24，**实现注意点**）。
4. 折叠：左栏右缘/右栏左缘 8px 把手（Panel MouseEnter/Click 切换宽 48/0）；F8 左、F9 专注、F10 右；AppConfig 持久化。
5. 自适应：面板 Resize 事件按 §1.4 公式重算；画布<640 自动先左收轨再右收。
6. 双收态「≡ 面板」按钮 + Drawer（浮动 Panel Dock=Right 临时覆盖）。

### 自动游玩/AI 游玩接线（§2/§3）
- 新增 public event Action<Chart,string> TestAutoplay（与 TestPlay 并列 :202）；PlayTest 同款拦截（无音符/无音频弹窗，:2817）。
- MainForm.cs:1113 同款接线 → _game.StartAutoplay(chart, dir) + HostContent(_game)；ESC 走现有 ExitToMenu→回编辑器（_testPlayReturn 机制复用，:1117）。
- AI 游玩：StartAiDemo(chart,dir,level) / LoadAndPlay+AddCompanionAi(lv)×1~3；等级下拉 AiEngine.Levels（AiSetupForm.cs:57 同款取值）；「vs 我」差值行改 GamePanel 陪玩面板首行（徽章体系全复用）。
- 预览前 _audio.Pause()（WPF MediaPlayer），_canvas.Enabled=false 防双渲染；退出同步 _time。
- 大谱 >2000 音符 toast 提示（§2.5.4）。

## 二、t1 P0/P1 修复坐标（源码已逐一验证）

| 问题 | 责任 | 文件:行（已验证） | 修法 |
| --- | --- | --- | --- |
| P0-1 设置页签不可点 | **t6 引擎侧** | 引擎/engine/Ui/LegacyControls.cs:126 InvokeClick() 空实现 | InvokeClick 按命中 cell 算 idx → Select(idx)；壳接线 EngineMainShell.cs:692-698 已就绪（TabChanged→RebuildSettingsRows），引擎修好即通 |
| P0-2 ▲▼ 命中错位 590px | t5 | 源码/Play/EngineUi/EngineMainShell.cs:845-858 AddArrows（up/dn X=272 固定） | 叠层 X 随 UiNumberBox 实际绘制宽计算，或箭头命中区并入 UiNumberBox 内部 |
| P0-3 曲库统计/筛选重叠 | t5 | 源码/Play/EngineUi/SecondaryPages.cs:110-113（_folderStats y=100,h=24；filterRow y=132） | filterRow y=132→156；stats 行限高 20；标题与首行 +8px |
| P0-4 启动 1280×800 固定 | t5 | 源码/Play/EngineUi/EngineMainShell.cs:77 ClientSize = new Size(1280,800) | ctor/OnShown 改用 ViewportPolicy.StartupSize(工作区)（ViewportPolicy 由 t6 提供，t3 文档 #7 已归 t5 接线） |
| P1-1 玩家信息按钮溢出 | t6 根治+t5 复查 | 引擎 UiGlyphRuns.cs:45-72（emoji 测量≠绘制宽）+ SecondaryPages 玩家信息页按钮 | 引擎侧 emoji 1.15em 估宽统一口径（t3 #194）；t5 复查按钮文案 |
| P1-2 我的数据行距 9px | t5 | SecondaryPages.cs:341 y += 34 | 改 y += 44 |
| P1-3 段位标签行距 | t5 | SecondaryPages.cs:238-239（DanHead y=140, DanSetLabel y=182） | 间距 +10px |

## 三、t5 执行边界与依赖
- **t5 只动 源码/**；引擎库文件（LegacyControls.cs / UiGlyphRuns.cs / ViewportPolicy 提供）由 t6 执行——P0-1 修复不在 t5 范围（EN-8 已对齐）。
- P0-4 依赖 t6 先落地 ViewportPolicy（t3 文档变更表 #7 标注 t5 接线；若 t6 未完成则 t5 先以 Screen.PrimaryScreen 简易实现占位）。
- 三栏自适应依赖 EN-5（letterbox 统一）但不阻塞：栏宽公式只认逻辑客户区，可先行。
- 与 t12（本地 AI 禁用）无冲突：AI 游玩=规则驱动 AiEngine，不接 LLM（editor-trilab D3/§3.5.4 已明确）。

## 四、t5 就绪度
- 依赖：t1 ✅completed、t2 ✅completed（规范 editor-trilab.md 已落地）→ **t5 已可开始**。
- 预研结论：全部改造点与修复坐标已核验，可直接进入实现。