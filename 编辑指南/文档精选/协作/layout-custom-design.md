# 布局编辑器全玩法高度自定义 设计文档（layout-custom-design，供实现执行）

> 任务：t58 · 设计者：designer-vis（策划）· 用户指令：布局编辑器更改为所有玩法都可更改（高度自定义）
> 依据（实测源码，行号=当前）：
> - GamePanel.cs：EnterLayoutEdit()（char@23541 区）/ExitLayoutEdit()/BuildEditPanel()（char@10651 区）/HudBox()/HitLineBox()/OnMouseDown/OnMouseMove/OnMouseUp（char@59892~63206 区）/DrawHud()（char@64689 区）/ComputeLayout() L739-764/PlayLayout struct L28-36/PlayAreaRect() L41+。
> - SkinSettings.cs：HudPos{X,Y,Show} L9-14；SkinSettings.Layout = Dictionary[string,HudPos] L44；DefaultLayout() L53-67（10 HUD 键+hitline）；SkinDto L168+（JSON=skin.json，程序目录/玩家目录 LoadWithPlayerFirst）。
> - ModeSystem.cs L36-57 模式 Id：mania/phigros/arcaea/cytus/osustd/taiko/catch/adofai/adofai2/iidx/loopcompose。
> - 各模式渲染参数读取点：PhigrosLineSegment L1072-1090（moveY/moveX/rotate 事件）、ArcaeaSetup L1782-1795（skyMove 默认 0.55/gndY=HitY/ppms/slant）、OsuField L2572-2580（4:3 居中）、MaimaiRingR L3355-3359（0.36×短边）/MaimaiButtonPos L3334-3352（中央 r=R*0.30）/MaimaiApproachMs L3362-3368、DrawAdofaiReal L2920-2945（segLen=min(PlayW,H)*0.10/累计角度）、DrawIidx L3079-3105（kc=8/laneW/转盘轨）、DrawCytus/CytusScanY L1050-1069（fieldY=AreaY+6/fieldH=AreaH-12）、DrawMania L1297+（use3d/斜轨 Slant）。

---

## 0. 目标与现状

**目标**：布局编辑器从「HUD 10 元素（title/score/acc/bpm/kps/notes/combo/judge/dev + hitline）全局拖拽 + 右侧数值」升级为「按玩法模式分组的专属元素集拖拽/值编辑 + 每模式保存加载（skin.json）+ 预设/重置/复制」，覆盖 Mania 系/IIDX/Phigros/Arcaea/Cytus/osu!std/maimai/ADOFAI 全部玩法。

**现状关键事实（设计前置）**：
1. 布局元素=Skin.Layout 字典（全局，模式无关）；HudPos 仅 X/Y/Show；值为归一化（0..1）。
2. 渲染读取：ComputeLayout L747（hitline→HitY）、L745（TopY=max(AreaY,70)）、L751-757（PlayW/LaneW）、L761（Ppm=Speed）、DrawHud（HUD 10 元素）。
3. 编辑器：无谱时 _blankLayout=true，_kc=4 —— **编辑预览恒为 Mania 4K**（与多模式诉求不符）。
4. **历史缺陷**：DrawHud char@64689 区 —— mode==Arcaea && !EditLayoutMode 时 HUD 走硬编码位置（score/title/combo/judge/dev 全特例），**Arcaea 游玩中 HUD 布局编辑不生效**（编辑态可见、实际不读 Layout）。本设计顺手修复。
5. 值面板：_editSel(ComboBox 选元素)+_editX/_editY(NumericUpDown)+_editShow(CheckBox)，BuildEditPanel 固定字段，不随元素语义变化。
6. 保存：OnMouseUp→Skin.Save()→skin.json（程序目录）；无每模式概念；无预设/重置/复制。
7. 入口：设置→皮肤「📐 在皮肤中编辑布局」（SettingsPanel:789→RequestLayoutEdit→MainForm:1085 EnterLayoutEdit）、主菜单「📐 布局编辑器」（MainForm:651/693-696）、引擎设置行（EngineMainShell:1090 同键写入）。

---

## 1. 数据模型（Skin 扩展：per-mode dict + 默认继承）

### 1.1 新类型（SkinSettings.cs 内，HudPos 不动）

    public class ModeElem {
        public double X = 0.5;        // 位置类元素：归一化位置（0..1）
        public double Y = 0.5;
        public bool   Show = true;    // 显隐
        public double P  = double.NaN; // 主标量（倍率/角度/数量…），NaN=未覆盖=默认
        public double P2 = double.NaN; // 次标量（可选）
    }

SkinSettings 追加：
    public Dictionary<string, Dictionary<string, ModeElem>> ModeLayout { get; set; } = new();
    // key = 模式 Id（ModeSystem.ModeId 语义）；value = 该模式元素覆盖集（只存非默认项，最小化文件）

### 1.2 默认继承链（读取语义 effective(mode, key)）

    ① ModeLayout[mode][key]（用户覆盖） → ② ModeDefaults(mode)[key]（代码内置常量表=当前各 Draw* 硬编码值）
    ③（仅 HUD 类）Skin.Layout[key]（全局共享） → ④ 引擎/渲染代码兜底（当前默认常量）。

原则：**默认值不落盘**——内置常量表在代码中维护（D4），皮肤文件只含覆盖项；旧 skin.json 无 ModeLayout 字段 → 读取天然全默认，零迁移、零回归。

### 1.3 序列化与兼容（SkinDto 增量字段）

SkinDto 追加： public Dictionary<string, Dictionary<string, ModeElem>> ModeLayout { get; set; }
ApplyDto 追加： if (dto?.ModeLayout != null) ModeLayout = dto.ModeLayout;
ToDto 追加：  ModeLayout = ModeLayout（深拷贝防共享）。
老文件兼容：字段缺省=null=空字典=全默认；**不升版本号**（D2，增量字段向后兼容）。

### 1.4 渲染热路径缓存（性能护栏）

effective() 为字典查找+数学操作；渲染每帧调用（Ppm/元素取值多在 Draw* 内）。设计：进入布局编辑/切谱/换模式时构建一次「当前模式生效值缓存字典」（ConcurrentDictionary 或普通字典+Invalidate），渲染读缓存，**编辑拖动时同步更新缓存**，保证热路径 0 额外查找（与 t9 零分配要求一致）。

---

## 2. 每模式专属元素集（groups × elements）

元素键命名空间（D3）：HUD 键无前缀（现状）；模式元素键=「组.名」（如 field.hit、ring.radius）。注册表结构（供 UI 动态渲染）：

    record ModeElemDef(string Key, string CN, string Unit, double Min, double Max, double Step, ElemKind Kind);
    enum ElemKind { PosXY, PosX, PosY, ScalarP, ScalarP2, Toggle }

每个元素：Key/中文名/单位/范围/步长/类型 + 读取点（渲染函数+行）+ 默认值。以下为元素集总表（默认=现硬编码值）。

### 2.1 HUD 组（全部模式共享，现有 10 键，仅修复 Arcaea 特例）
title/score/acc/bpm/kps/notes/combo/judge/dev/hitline —— 现 DefaultLayout L53-67。

### 2.2 mania（mania，4/6/7/8K 通吃；iidx 见 2.8）
| Key | 语义 | 类型 | 范围/单位 | 默认 | 读取点 |
|---|---|---|---|---|---|
| field.hit | 判定线 Y（=现 hitline 语义，per-mode 化） | PosY | 0.05–0.95 | 0.82 | ComputeLayout L747 |
| field.top | 音符生成区顶 | PosY | 0.08–0.30 | max(AreaY,70)/H | ComputeLayout L745 |
| field.w | 轨道区宽倍率 | ScalarP | 0.5–1.5 × | 1.0 | ComputeLayout PlayW L752 |
| field.speed | 流速倍率 | ScalarP | 0.5–2.0 × | 1.0 | Ppm L761 |
| lane.slant | 斜轨强度 | ScalarP | 0–1 | Skin.Slant（全局） | DrawMania/DrawMania3D |
| note.cam3d | 3D 相机 | Toggle | 开/关 | GameSettings.Camera3D | use3d L1299 |
| note.size | 音符宽高倍率 | ScalarP | 0.5–1.5 × | 1.0 | DrawMania 系 note 几何 |
| bg.dim | 背景淡化 | ScalarP | 0–1 | Skin.BgDim | 背景绘制 |

### 2.3 phigros（谱面事件驱动——布局=基准，事件叠加，见 §8 风险 R1）
| line.core.y | 判定线基准 Y（moveY 事件未写时/叠加基线） | PosY | 0.02–0.98 | 0.5 | PhigrosLineSegment L1074 |
| line.core.x | 判定线基准 X | PosX | 0.02–0.98 | 0.5 | L1075 |
| line.core.rot | 基准旋转 offset | ScalarP | -180–180 ° | 0 | L1076 |
| field.speed | 流速倍率 | ScalarP | 0.5–2.0 × | 1.0 | Ppm |
| note.size | 音符尺寸倍率 | ScalarP | 0.5–1.5 × | 1.0 | DrawPhigros L1593+ |
| note.alpha | 音符透明度 | ScalarP | 0.3–1.0 | 1.0 | DrawPhigros |

### 2.4 arcaea（本组同时修复 HUD 特例）
| field.sky | 天线（天判定线）位置 | PosY | 0.12–0.88 | 0.55 | ArcaeaSetup L1785-1786 |
| field.ground | 地线 Y（=per-mode hitline） | PosY | 0.5–0.95 | hitline 0.82 | gndY=L.HitY L1784 |
| arc.tilt | 斜轨强度 | ScalarP | 0–1 | Skin.Slant/_arcPk | L1791-1793 |
| arc.width | arc 线宽倍率 | ScalarP | 0.5–2.0 × | 1.0 | DrawArcaea L1843+ |
| arc.alpha | arc 透明度 | ScalarP | 0.3–1.0 | 1.0 | DrawArcaea |
| note.size | 音符倍率 | ScalarP | 0.5–1.5 × | 1.0 | DrawArcaea |

### 2.5 cytus
| ring.center | 扫描场中心（偏移） | PosXY | 0.02–0.98 | Area 中心 | CytusScanY/CytusNoteY L1050-1069 |
| ring.radius | 扫描/圆半径倍率 | ScalarP | 0.3–1.0 × | 1.0 | fieldH/fieldW 计算 |
| ring.thick | 环厚倍率 | ScalarP | 0.5–2.0 × | 1.0 | DrawCytus L2190+ |
| scan.alt | 上下页扫描交替 | Toggle | 开/关 | true | pageIdx%2 L1055 |

### 2.6 osustd（4:3 框定）
| field.center | 场中心（偏移） | PosXY | 0.02–0.98 | Area 居中 | OsuField L2572-2580 |
| field.scale | 场缩放（锁 4:3） | ScalarP | 0.5–1.5 × | 1.0 | OsuField w/h |
| field.aspect | 锁定 4:3 | Toggle | 开/关 | true | OsuField |
| cursor.size | 光标倍率 | ScalarP | 0.5–2.0 × | 1.0 | DrawOsuStandard L2329+ |

### 2.7 maimai
| ring.radius | 外圈半径倍率 | ScalarP | 0.5–1.5 × | 1.0（0.36×短边） | MaimaiRingR L3355-3359 |
| ring.center | 环心（偏移） | PosXY | 0.02–0.98 | CenterX/Y | MaimaiButtonPos |
| mid.radius | 中央五角半径倍率 | ScalarP | 0.5–1.5 × | 1.0（R×0.30） | L3345 |
| note.approach | 接近时间倍率 | ScalarP | 0.5–2.0 × | 1.0 | MaimaiApproachMs L3362-3368 |

### 2.8 iidx（8K+转盘）
| bucket.y | 判定桶 Y | PosY | 0.5–0.95 | hitline | DrawIidx hitY L3079 |
| field.top | 生成区顶 | PosY | 0.08–0.30 | 同 mania | |
| field.w | 轨道宽倍率 | ScalarP | 0.6–1.3 × | 1.0 | laneW L3084 |
| turntable.w | 转盘轨宽倍率 | ScalarP | 0.7–1.6 × | 1.0 | sepX L3103 |
| field.speed | 流速倍率 | ScalarP | 0.5–2.0 × | 1.0 | ppms L3085 |

### 2.9 adofai / adofai2（Routlock/ADOFAI 路线）
| route.scale | tile 段长倍率 | ScalarP | 0.5–2.0 × | 1.0 | segLen L2925 |
| route.rot | 全局旋转 offset | ScalarP | -180–180 ° | 0 | 累计角度基准 L2943+ |
| route.center | 路线中心（偏移） | PosXY | 0.02–0.98 | CenterX/Y L2924 | |

### 2.10 taiko / catch / loopcompose（最小集，P2 支持）
field.hit / field.speed / note.size / note.alpha 四项（同 mania 语义），保证「所有玩法都可更改」底线。

### 2.11 内置默认常量表（ModeDefaults）
= 2.2–2.10 各行的「默认」列落位为静态方法（每模式一个默认字典，含全部键），并**逐模式对照现有 Draw* 硬编码**：实现时每个默认值必须与当前渲染像素一致（还原度护栏，改任何默认=还原度回归项）。

---

## 3. 布局编辑器 UI 改造（GamePanel + _editPanel）

### 3.1 进入与预览模式
- 现状 EnterLayoutEdit()：_blankLayout（无谱）时 _kc=4 → 恒 Mania 4K。改造：_blankLayout 时 _editPanel 顶部出现「预览模式」ComboBox（ModeSystem 全部 Id），切换即重建预览（调用 LoadChart 级最小 mock：_kc=该模式 Keys，绘制对应 Draw*）。
- 有谱场景：预览=当前谱面模式（不变）。
- 标题条（现有「📐 布局编辑器：拖动或右侧微调 · ESC 保存并退出」）追加当前模式名+「F2 重置当前元素 · Tab 切换元素」。

### 3.2 画布元素盒（现有 HudBox/HitLineBox 扩展）
- 普通元素：盒=名称+边框（现状）；模式元素按 §2 语义绘制交互盒：PosXY/PosX/PosY 拖动；ScalarP 不拖（画布画数值角标，双击弹值编辑）；Toggle 画 ✓ 角标。
- Hit 测试优先级：hitline 盒（最粗）→ 模式元素 → HUD 元素（现有倒序循环保留）。
- 拖拽量：PosXY 直接改 X/Y（归一化同步缓存+SyncEditValues）。

### 3.3 右侧值面板（BuildEditPanel 动态化）
- 元素选择 ComboBox 改为**分组下拉**：组=HUD（10 键）/当前模式（组标题灰显不可选）+ 元素中文名。
- 值区动态生成（按 ModeElemDef）：PosX/PosY → X 框+Y 框；ScalarP → P 框（单位/范围/步长生效）；Toggle → Show 复选框。
- 按钮行：重置当前元素（删 dict 项）｜恢复本模式默认（清空该模式组）｜预设 ▾（§3.4）｜复制到… ▾（§3.5）｜保存并退出（=ESC）。
- 键盘：方向键微调 X/Y（±0.005，Shift×10）；F2 重置当前；Tab 在元素间循环；ESC 保存退出（现状逻辑保留：Skin.Save()+ExitToMenu）。

### 3.4 预设（D5：常量表，非用户存档）
每模式 3 档内置：默认（=ModeDefaults）/ 紧凑（field.top↑、note.size×0.8、field.w×0.9、ring.radius×0.9…）/ 大屏（note.size×1.15、field.w×1.0、ring.radius×1.1…）。预设=一次性「写入覆盖句柄+清除其余」，不存预设本身。

### 3.5 复制到…（跨模式）
同构组一键复制：mania↔iidx（键一对一：field.hit/field.top/field.w/field.speed；turntable.w 忽略）；其余模式提示「部分字段语义不同，仅复制同名键」（扩散到同名键）。UI：对话框列出目标模式。

### 3.6 保存
OnMouseUp/ESC → Skin.Save()（现状）+ ModeLayout 深拷贝写入 ToDto（§1.3）；进入编辑前读取（Skin.LoadWithPlayerFirst 现状即可）。

---

## 4. 与皮肤/设置的联动

1. **皮肤文件**：skin.json（程序目录优先加载规则不变：Player 目录→程序目录）；新增 ModeLayout 增量字段；保存位置沿用 Skin.Save()（程序目录）——记录：现有「玩家目录优先、保存写程序目录」行为本设计不改（历史统一口径）。
2. **SettingsPanel 皮肤分区**（L788-789）：保留「📐 在皮肤中编辑布局」入口；追加「当前模式布局：预设▾/重置」行（P1）；HitLineY 微调行（L258/723/856）保持全局语义（=HUD hitline，非模式元素）。
3. **引擎设置行**（EngineMainShell L1090-1093 RowAction 判定线）：改为「穿继承链读当前谱面模式 field.hit」；无谱时读全局 hitline（P2，避免歧义，可作为 t54/后续项）。
4. **边界声明**（防混淆，写进用户文档）：布局=几何/位置/倍率；皮肤（SkinSettings 颜色/字体/样式）+ 主题（UiTheme）+ NoteStyleBook（音符配色）=颜色与样式域；GameSettings.Speed=全局绝对速度，field.speed=布局倍率（相乘，D7）；GraphicsQuality.SlantCap 等性能上限保留为钳制，不暴露为布局元素。
5. **与 t54 联动**：布局编辑器已可在引擎窗 HostContent 承载（t50 现状），t58 零引擎改动；t54「模式预设」若为 UI/主题预设，与本文「布局预设」命名区分（建议引擎侧叫 ModeUiPreset）；t54「引擎内游戏预览」落地后，布局编辑预览可复用引擎预览管线（t58 后继评估项，不阻塞）。

---

## 5. 用户文档（草案，供发布）

**《布局自定义指南》**（发布位：其他/docs/布局自定义指南.md；编辑器说明行同步引用）：
- 入口：设置→皮肤→「📐 在皮肤中编辑布局」；主菜单「📐 布局编辑器」。
- 三件事：拖动蓝框元素改位置；右侧面板改数值/开关；F2 重置/预设/复制。
- 每模式元素表（§2 的 CN/单位/范围缩略版）。
- 说明：Phigros/ADOFAI 的线位/方向由谱面事件驱动，布局调整的是「基准」；Arcaea HUD 已修复为布局可调；保存位置=skin.json，删除文件即恢复默认；主题/皮肤配色不属于布局。

---

## 6. 实施分批与验收（供 coder-vis；预计净 ~800 行，零引擎层改动）

| 批 | 内容 | 预估 | 验收 |
|---|---|---|---|
| B1 数据层 | ModeElem+ModeLayout+SkinDto 增量+ModeDefaults 常量表（§2 默认值逐行核对）+继承链 effective()+热路径缓存 | ~200 行 | 构建 0/0；老 skin.json 读取回归；单测断言继承链 |
| B2 渲染接入 | 各 Draw*/ComputeLayout 硬编码改读 effective()（覆盖 §2 全部读取点；保留还原度基线，默认值不变则像素不变） | ~250 行 | 构建 0/0；默认值下 shellshot/还原度对比与改前逐像素一致 |
| B3 编辑器 UI | 分组下拉/动态值面板/元素盒扩展/拖拽/预设/复制/键盘/无谱模式预览 | ~350 行 | 手工走查 A1-A11 |
| B4 收尾 | Arcaea HUD 特例修复+设置面板预设入口（P1）+引擎设置行敏感化（P2）+用户文档发布 | ~80 行 | A12 + 发布文档 |

**验收矩阵**：A1 无谱进入→模式下拉 9 组预览可切换；A2 元素按分组显示；A3 拖拽 X/Y 生效且退出保存、重启后保留；A4 值字段范围/步长约束；A5 重置/预设/复制（含 mania↔iidx）；A6 旧 skin.json（无 ModeLayout）读取零回归；A7 多分辨率 1280/1920/2560 布局比例一致；A8 游玩实际渲染逐模式验证（mania field.hit/phigros line.core.y/arcaea field.sky/cytus ring.radius/osu field.scale/iidx bucket.y/maimai ring.radius/adofai route.scale）；A9 HUD 与模式元素互不干扰；A10 Arcaea HUD 布局编辑后游玩生效（历史缺陷修复）；A11 三入口（设置/主菜单/引擎行）可达；A12 parity/shellshot 默认值零视觉回归。

---

## 7. 关键决策

D1 位置统一以 ClientSize 归一化（与现状 HUD 一致；多分辨率/letterbox 自适应天然成立）。
D2 skin.json 不升版本号（增量字段向后兼容）。
D3 模式元素键=「组.名」命名空间，避免与 HUD 键冲突。
D4 默认值=代码内置常量表不落盘（零迁移、默认可随版本更新）。
D5 预设=常量表（一次性写入覆盖），非用户自定义预设存档。
D6 无谱编辑仅预览，不影响存档。
D7 field.speed=倍率乘 GameSettings.Speed（单一速度语义，不并存绝对速度）。
D8 零引擎层改动；布局编辑器留在游戏层（引擎窗承载现状即可）。

## 8. 风险与护栏

R1 事件驱动模式（Phigros/ADOFAI）用户可能期待「完全固定线位/方向」——文档+编辑器提示注明「基准+事件叠加」语义。
R2 B2 渲染接入面广（13 个 Draw*+ComputeLayout），逐函数复查；每改一个默认值=还原度回归项（用既有还原度对比工具链验证）。
R3 热路径：effective() 每帧调用 → 必须走缓存（§1.4），零逐帧字典新分配（t9 口径）。
R4 多人分工冲突：B1/B2 触 SkinSettings（皮肤文件）与 GamePanel（渲染）——与 coder-vis 并行任务做代码归属约定（皮肤文件=本任务独占改动区）。
R5 无谱预览 mock 仅为视觉（无判定/无谱面事件）——文档注明。

---

## 附：与相关文档衔接

- t5 编辑器三栏（editor-trilab.md）——布局编辑器入口在引擎窗承载已定型，本设计不改变入口形态。
- visual 系列还原度对比方法（还原度闭环_*/*_最终总结）——B2 默认像素不变的验证方法沿用。
- t53 删除设计（t53-设计定稿-修正.md §3.1 白名单含「布局编辑器 EnterLayoutEditor」保留）——本设计不动 MainForm 承载，无冲突。
