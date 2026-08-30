# 同谱多玩法同屏（MultiStage）设计契约

> 状态：设计契约（只做文档，不涉代码）。作者：verifier（t26，2026-08-26）。
> 目标：同一份 .mil 谱面中最多 4 个「舞台」（不同玩法/键数/位置/大小）同屏实时判定与渲染，
> 旧谱（无 stages）零改动兼容；编辑器提供多场面板与可视拖框定位；复用现有部件（parts）机制最小代价落地。

---

## 0. 术语与现状基线

- **部件（part）**：现有 ChartPart（Models.cs）＝单层玩法容器（Mode/KeyCount/Notes/Events/LineParents/LineMeta）。编辑器已有多部件机制：`_parts`/`_partIndex`/`_partBox`（工具栏部件框）+ AddPart/DeletePart/SwitchPart/SaveActivePart/ApplyPartToEditor，运行时 M 键 SwitchNextPart 单部件切换（GamePanel.SwitchNextPart）。
- **舞台（stage）**：本契约新增概念 = 部件 + 屏幕矩形布局（+ 可选键位映射）。stage i ⇔ parts[i]（同索引），stage 只新增「位置/大小/键位」元数据，音符/事件容器仍复用 parts。
- **引擎实例**：GamePanel 现有 `readonly JudgementEngine _eng = new JudgementEngine(true)`（源码\Play\JudgementEngine.cs）；多场 = 每场独立引擎实例、共用全局时钟。
- **渲染签名**：DrawPhigros/DrawArcaea/DrawCytus/DrawOsuStandard/DrawAdofai/DrawAdofaiReal/DrawIidx/DrawMaimai/DrawLoopComposer/DrawMania(W,H,now,PlayLayout,..) 已统一接受布局（PlayLayout{TopY,HitY,PlayX,PlayW,CenterX,..}），是复用的基础。
- **裁剪能力**：D2DRenderer 已有公开 `PushQuadLayer(x1..y4)/PopLayer`，可作矩形遮罩裁剪（EngineUi 的 D2DDrawAdapter.PushClip 即其包装）。

---

## 1. 数据模型

### 1.1 内存模型（Models.cs）

```csharp
public class Stage
{
    public int Id;           // 舞台 id（0=主舞台）；= parts 索引
    public string Name = ""; // 显示名（默认 = 部件名）
    public GameMode Mode;    // 与绑定部件一致（冗余，读/写时校验）
    public int KeyCount;     // 与绑定部件一致（冗余）
    public double X = 0, Y = 0, W = 1, H = 1;   // 归一化 0..1（相对 GamePanel ClientSize）
    public Keys[] KeyMap;    // 可选：本场键位覆盖（null = 默认配置表 GameSettings.GetKeys(kc)）
    public bool Cam3D = false; // 可选（未来项，本版恒 false）
}
```

- `Chart.Stages : List<Stage>`（null/空 = **旧谱单主场**：隐式 stage0 = 根字段(Mode/KeyCount/Notes/Events)，rect=(0,0,1,1)，KeyMap=默认）。
- `Note.Field : int = 0`：路由归属（stage id）。加载时按所在 parts 容器赋索引；部件内默认不再逐音符序列化（容器隐含）。
- `ChartEvent.Field : int = 0`：事件按 field 隔离（与音符同规则：容器隐含，字段仅内存/兼容输入用）。
- **约束**：`Stages.Count == Parts.Count`（若文件有 stages）（加载时自动对齐：Parts 不足 → 补空部件；stages 不足 → 剩余部件不显示）。无 parts、有 stages 的"平铺"文件（根 notes 带 `field`）由解析器展开成 parts[] + stages[]（见 1.3）。

### 1.2 .mil 序列化（format 仍为 "milestone-1"）

新增可选顶键 `stages`（数组）；**音符/事件容器不复刻**——沿用现有 `parts[]`（与 stages 同索引），根字段 = 第 0 舞台镜像（与现有「根字段=第 0 部件」约定一致）。

```jsonc
{
  "format": "milestone-1",
  "mode": "mania", "keys": 4,
  "notes": [ /* 舞台0 镜像（可选） */ ],
  "parts": [
    { "name": "左场 4K", "mode": "mania", "keys": 4, "notes": [ ... ], "events": [ ... ] },
    { "name": "右场 IIDX", "mode": "iidx", "keys": 7, "notes": [ ... ], "events": [ ... ],
      "lineParents": [], "lineMeta": [] }
  ],
  "stages": [
    { "id": 0, "name": "main",  "x": 0.00, "y": 0.0, "w": 0.50, "h": 1.0 },
    { "id": 1, "name": "iidx",  "x": 0.50, "y": 0.0, "w": 0.50, "h": 1.0,
      "keyMap": ["S","D","F","J","K","L","O"] }
  ]
}
```

- stage 对象字段：`id`（int，必填=索引）、`name`（string，可选）、`x/y/w/h`（double，0..1，缺省 0/0/1/1）、`keyMap`（string[] 可选，键名 = System.Windows.Forms.Keys 枚举名，如 "D","F","J","K","Space"）。
- **mode/keys 不在 stages 对象内冗余存储**——以 parts[i] 为准（写时强一致：编辑器保存前同步 stage.Mode/KeyCount = parts[i]）。读入时若 stage 内也写了 mode/keys（兼容外部工具），仅校验不一致时以 parts 为准并记 hint（不报错）。
- **平铺兼容输入**：根 `notes[]` 中的音符可带 `"f":<stageId>`（stage 存在时）→ 解析器把该音符分派到 parts[f]（自动建缺失 parts），并赋 `Note.Field=f`；无 stages 或 f 越界 → 归 0（Hint 级问题，不失败）。编辑器保存永远输出容器形态（stages+parts），不写 f。
- 事件平铺兼容：根 `events[]` 同样接受 `"f"`（分派到 parts[f].Events + ChartEvent.Field）。

### 1.3 加载归一化规则（ParseMil 扩展）

1. 无 `stages` → 走现有逻辑（Stages=null，单主场）。
2. 有 `stages`：
   - 读 stages → Chart.Stages；按 1.2 构造/校验 Chart.Parts；
   - 根 notes/events 若带 `f` → 按 1.2 分派；根无 f → 视为舞台 0 镜像（与 parts[0] 一致时去重/忽略）；
   - 每场音符赋值 `Note.Field = stage.Id`（parts[i] 音符 → Field=i）；每场事件 `ChartEvent.Field = i`；
   - 钳制：x=Clamp01(x)，w=Clamp(0.08..1)，x+w>1 时缩 w；H 同；Id 重排为 0..n-1（按数组序）。

---

## 2. 判定与输入路由

### 2.1 每场独立判定引擎，共享全局时钟

- `List<JudgementEngine> _stageEngs`（`new JudgementEngine(true)`，TotalNotes=该场音符数）；`_eng` 保留为 stage0 别名（legacy 路径不变）。
- 时钟：Step() 计算一次 `now = RawMs() + GameSettings.Offset`，分场传给 `StepStage(s, now)`（现有 Step 体参数化：miss 扫除/_autoIdx/AiStep/Hold 滑动等全部以"场"为粒度）。
- **判定档位（JudgeSettings）全局共享**：按主场（stage0）mode 应用（ApplyForChart 现有调用不变）。多场不同 mode 时各场使用同一窗口表（限制记录于 §5，段位/HP 模型亦共享：HP 只按主场 eng 结算——段位挑战(DanActive)仍在多场下禁用）。

### 2.2 键盘路由与冲突规则（契约核心）

构建 `Dictionary<Keys, List<(int stage, int col)>> _stageKeyRoute`：
- 每场默认 keyMap = `GameSettings.GetKeys(stage.KeyCount)`（现有表：4K=DFJK、5K=DF[Space]JK、6K=SDFJKL…）；stage.KeyMap 非空 → 覆盖（按数组序 → col 0..n-1）。
- 特殊场零调整：Adofai/AdofaiReal 沿用现有 `Space/D→col0` 覆盖；OsuStandard 场不参与列键路由（见 2.3）；LoopComposer 禁止入多场（§5）。

**按键 Down 路由算法（HandleDown 多场化）**：

1. `cands = _stageKeyRoute.TryGetValue(key) ? ... : {} `。
2. 对每个 (stage,col) 候选：在该场 notes 上执行**现有 HandleDownAt 的搜索逻辑**（近窗 LowerBound(now-lastW)..earlyLimit、Phigros X 区间/Cytus scanY 门控、col 匹配等——把现有函数体参数化为 `FindCandidate(stageNotes, stageLayout, mode, col, now)`），收集每场第一个候选 `(stage, note, dev)`（每场至多一个：现有语义即"该场最近命中"）。
3. 冲突决议：
   - 唯一候选场 → 直接 Hit（现行为不变，单场/单映射 key 与现状同构）。
   - 多场候选 → 取 `|dev| = |now - n.Time|` **最小**者（更近窗）；|dev| 差 < 0.5ms 视为平局 → 取 **stage.Id 小者（主舞台优先）**。
   - 无任何候选 → 忽略（与现在无候选时一样）。
4. 赢家场 Hit 后，其余候选场**不判定**（该音符窗口内留待该场 miss 扫除；跨场同刻同键的音符天然错开谱面时极少触发，可接受）。
5. 记录 `_stageDowns[stage]` 键按下状态（多场 HOLD 释放需要按场路由：**Up 路由 = 该键在当前按下记录中绑定的 (stage,col)**，与 Down 决议一致，避免"按下赢主场、抬起却放掉右场 HOLD"）。

**举例（任务场景：左场 Mania 4K + 右场 IIDX 7K）**：
- 左场 keyMap 默认 DFJK；右场 7K 默认 SDFJKL+O（或显式 keyMap）。D/F/J/K 两场共用（冲突键），S/L/O 仅右场。
- 按 D：左场搜索 col0、右场搜索其 D 对应 col → 各得候选/无候选 → 冲突决议按第 3 条。S/L/O 仅右场命中，行为同现状。
- 谱师可用 `keyMap` 为右场改键（如 "S","D","F","J","K","A",";"）避免冲突；未改键时依赖近窗决议保证不"双判"。

### 2.3 触屏 / 鼠标路由

- **触屏场（Phigros/Arcaea/Cytus/Maimai/AdofaiReal）**：OnMouseDown → 命中测试：按 stages **数组逆序**（后添加者在上层，z-order=索引序）取第一个包含落点的场 → 坐标转场局部（pt-场源）→ `TapAtLocal(stage, ptLocal, now)`（现有 TapAt 体参数化：radius/mode gate/NoteScreenPos 均用该场 L）。同点落在两场重叠矩形 → 上层场（禁止重叠仍是最佳实践，§5 钳制）。
- **osu!standard 场**：鼠标/键位（Z/X/Space）全部先按座标命中场 rect（OsuTap/Cursor 状态按场隔离）；spinner 长按上下行按场记录。
- **LoopComposer 场**：多场禁用（§5）。

### 2.4 播放/AI/回放

- Autoplay：`_autoIdx` 分场（现有字段 → `int[] _stageAutoIdx`；AiStep/DemoAi/CompanionAis 按场路由候选——AI 每场独立找最近未判定音符）。
- 录影/回放：ReplayEvent.Code 不变；回放走与实时完全相同的多场路由（契约：回放=实时同函数）。
- 击中特效/音效：按命中场局部坐标爆点（全局粒子列表 + 场标识），声音共用现有 SoundFx 路由。

---

## 3. 渲染

### 3.1 PaintCore 多场分支

```csharp
if (_stageCount > 0 && !EditLayoutMode) {
    _d2d.Clear(...); 画背景一次（现有逻辑，全屏）;
    foreach (var s in _stagesL) {
        var rect = StageRect(s);                    // X*W, Y*H, W*W, H*H（钳制 1.1/1.3）
        _d2d.PushQuadLayer(rect.X, rect.Y, rect.Right, rect.Y,
                           rect.Right, rect.Bottom, rect.X, rect.Bottom);
        var Ls = ComputeLayoutFor(rect, s.KeyCount); // 见 3.2
        DrawStageContents(s, rect.W, rect.H, now, Ls); // switch(s.Mode) → 现有 Draw*(rect.W, rect.H, now, Ls,...)
        _d2d.PopLayer();
        DrawStageFrameAndLabel(s, rect);              // 边框 + 舞台名（小字）
    }
    DrawGlobalHud(now);                               // 总评 HUD（3.3）
    DrawResultScreen/W 进度条等（现状）；
} else { /* 现状单场路径零改动 */ }
```

- **场内容**：`DrawStageContents = switch(s.Mode)` 完全复用现有 DrawXxx（签名已统一 (W,H,now,PlayLayout,..)），**不改 Draw* 函数体**（3D 例外，见 3.4）。
- **裁剪**：PushQuadLayer/PopLayer（D2D 层级遮罩，现成 API）保证音符/判定线不溢出场矩形。
- 局部 HUD（每场）：场顶小条标题+连击+判定（可选开关 `skin.stageHud`，默认开）。

### 3.2 ComputeLayoutFor 重构（唯一必须动布局数学的地方）

- 现 `ComputeLayout()/PlayAreaRect()` 依赖 ClientSize + ShowRightPanel（Mania/IIDX 300px）+ osuStd 4:3 选项。
- 重构：`ComputeLayoutFor(RectangleF base, int kc, bool useRightPanel)`——多场时 base=场 rect、useRightPanel=false、osuStd 框定按场 rect 内等比（复用现有 512×384 逻辑参数化）。单场路径调用 `ComputeLayoutFor(PlayAreaRect(), _kc, ShowRightPanel)` 保持行为逐字节一致（防回归）。
- 场布局与全局 L 均产出 PlayLayout；场内容绘制用场 L（映射名/位置全部局部化——Draw* 内所有使用 L 的坐标天然随 L；**审计清单**：DrawHud 直接读 ClientSize.Width/Height → 多场分支改为传入场 rect 局部 HUD；DrawRightPanel 仅单场；背景 DrawImage 保持全屏一次）。

### 3.3 全局 HUD 与结算

- 总评 HUD：Score=Σ各场、ACC=加权平均（取 Σ(weight)/Σ(notes)，现有 RecomputeAcc 口径按引擎汇总）、Combo/MaxCombo=分场显示——契约：全局大字走"主舞台"判定帧（_lastJudge 来自命中场），顶部小字总分。
- 结算（SongEnded → GameResult）：合并各场 `_stageEngs` 的 Hits 字典（同名档位累加）、Score=Σ、Acc=weighted、MaxCombo=max(各场)；Hits 明细表列出分场统计（"4K: 345/350 · IIDX: 700/715"）；段位（DanActive）多场禁用。

### 3.4 3D 场（可选，本版不做）

- stage.Cam3D 仅保留字段；DrawMania3D 使用全屏楔形布局 + 凸多边形表（_mania3DQuads），局部相机重构成本高 → **本版多场一律 2D**（契约登记为可演进项）。

---

## 4. 编辑器

### 4.1 「多场」面板（新增）

- 入口：工具栏新增「🎛 多场」切换（打开面板）；面板仅在多模式图内可用。
- 结构（复用部件机制，零新状态）：
  - **场列表 ComboBox**（= 现有 `_partBox` 的"舞台视角"：条目 `名 · 模式 · 键数 · (x,y,w,h)`）＋「➕ 场」「🗑 场」＝ 现有 AddPart/DeletePart 直接复用（场与部件同生命周期）。
  - 属性区：X/Y/W/H NumericUpDown（0..100%，0.5 步进）+ 模式/键数（沿用现有模式与键数编辑器）+ 场名 TextBox + 「keyMap 自定义」（可选文本 "D,F,J,K"→Keys[]）。
  - **拖框定位**：按钮「📍 定位场」进入 StagePlaceMode —— 画布上按住拖出矩形（吸附 5%），松手 → 归一化写回当前场 X/Y/W/H；已存在场：按住场边框四角手柄缩放/整体拖动（一版先做"新建拖框 + 选中场四角手柄"，钳制重叠/最小尺寸）。
- **音符归属**：当前场 = 当前部件（现有 `_partIndex/_notes/_events` 即场内容）；工具栏场选择器（= _partBox）切换即切场——**音符添加/事件编辑/删除全部零改动**（归属 = 当前部件，Note.Field 在保存/加载时按部件索引写）。
- **无轨模式**：编辑器无轨时间线视图不变（仍按当前场音符编辑）；场矩形覆盖层只在 StagePlaceMode 或「显示场框」勾选时绘制（EditorCanvas 现有绘制循环尾部追加：场虚线框/场名/序号/选中高亮）。

### 4.2 兼容行为

- 无 stages 旧谱：面板显示"单主场（全屏，未启用多场）"；保存时只有 ≥2 场才写 stages[]；1 场时不写（旧谱零 diff）。
- 已有 parts（M 键切换族）打开：若未启用多场 → parts 逻辑原样；启用多场 → 每 part 自动获得默认布局（均分列：第 i 场 x=i/n,w=1/n,h=1）并可拖改；stages[] 自此写入。
- 保存：BuildChart 末尾 `c.Stages = _stages`（SaveActivePart 后由面板状态同步 Mode/KeyCount/rect/keyMap 到 Stage 与 parts[i]）。

---

## 5. 兼容与限制

| 项 | 规则 |
|---|---|
| 旧谱 | 无 `stages` → Stages 空 → 运行/编辑走现有单场路径（代码 0 分支差异；序列化无 diff） |
| parts-only（M 键切换） | M 键行为不变；**多场模式（stages>0）下 M 键禁用**（toast「多场模式无部件切换」）；stages 与 parts 共存于文件（stages 是布局层） |
| 上限 | **≤4 场**；编辑器第 5 场添加被拒（提示）；场 rect 最小 8% 宽高、钳制 x,y∈[0,1]、w,h∈[0.08,1]，x+w≤1 |
| 重叠 | 编辑器禁止（重叠场保存前警告；运行期 z-order=数组序，后场在上层） |
| 判定档位 | 全局共享（按主场 mode）；每场独立档位/HP/段位 = 演进项（多场不支持 Dan 挑战） |
| 特殊玩法 | LoopComposer 禁止入多场；MpActive 联机时强制单场（多场渲染禁用）；已移除玩法部件照旧不可玩 |
| 性能 | ≤4 场×每场一次 Draw*（向量绘制）；PushLayer 仅为矩形遮罩，开销小；建议总音符 ≤2000；PaintMs 超预算走现有 GraphicsQuality.AutoAdapt（含 WARP 兼容，GpuGuard 互不干扰） |
| 键位冲突 | 默认按 §2.2 决议（近窗最小 |dev|，平局主场）；谱师可用 stage.keyMap 显式分键（推荐） |
| 音频/BPM/Offset | 全局共享（一份音频、一份 BPM 时间线、一个 Offset 输入到渲染与判定——各场音符时间同基准） |
| 回放 | 与实时同路由函数，向前兼容（老回放=单场路由，不受影响） |

---

## 6. 落地顺序与最小改动路径

### Phase 1 — 模型与序列化（先行，无 UI 影响）

| 文件 | 位置 | 改动 |
|---|---|---|
| 源码\Charting\Models.cs | Chart 类、Note、ChartEvent | +Stage 类（~25 行）；Chart.Stages（1 行）；Note.Field（1 行）；ChartEvent.Field（1 行） |
| 源码\Charting\ChartParserExtra.cs | SerializeMil | 写法：c.Stages 非空 → 写 `stages` 数组（id/name/x/y/w/h/keyMap）（~40 行） |
| 源码\Charting\ChartParserExtra.cs | ParseMil | 读 `stages` → Chart.Stages + parts 对齐 + Note.Field/ChartEvent.Field 赋值 + f 平铺分派 + 钳制（~80 行） |
| 源码\Charting\ChartParser.cs | 无改动（IsMilestone 不变） | — |

验证：新写 stages 的 .mil 用 chartcheck/edsim 往返一致；旧 .mil 解析结果与改动前逐字节字段一致。

### Phase 2 — 运行路由（GamePanel.cs 为核心）

| 改动点 | 位置 | 说明 |
|---|---|---|
| 新字段 | 类头部 | `_stages`(List<Stage>)、`_stageEngs`、`_stageNotes`、`_stageKeyRoute`、`_stageAutoIdx`、`_stageHolds` 等（~30 行） |
| LoadAndPlay/StartMpPlay | 现有函数体后段 | 构建场列表（无 stages → 单场兼容 + 归并 `_eng`∥field0） |
| ResetState | 现有函数体 | 分场 Reset：每场引擎 Reset/TotalNotes；构建 _stageKeyRoute；`PrepareModeNotes` 分场 |
| HandleDown/HandleUp | 现有函数体 | 多场路由 + 冲突决议（§2.2 算法，~80 行）；单场路径保持现状 |
| TapAt | 现有函数体 | rect 命中 → 局部 TapAtLocal（~40 行）；单场 = 全屏 rect 命中（行为不变） |
| Step | 现有函数体 | 分场循环（miss 扫除/自动/AI/Hold）；单场 = 单元素循环（行为不变） |
| PaintCore | 现有函数体 | 多场分支（§3.1）；单场走现状 |
| ComputeLayoutFor | 新增 + PlayAreaRect 重构 | 布局参数化（~60 行）；单场行为逐值不变（edsim/截图回归） |
| 结算合并 | Finish/Step 结算段 | GameResult 聚合（~40 行） |

### Phase 3 — 编辑器（ChartEditorPanel.cs）

| 改动点 | 位置 | 说明 |
|---|---|---|
| 「多场」面板 | _toolbar 构建段 + 新面板类 | 场 Box/➕🗑/X/Y/W/H/定位模式（复用部件按钮/面板样式，~250 行） |
| StagePlaceMode | 画布鼠标回调 + EditorCanvas 绘制 | 拖框/四角手柄 + 场框绘制（~120 行） |
| BuildChart / LoadChart | 现有函数体 | 写/读 Chart.Stages（~30 行） |
| 无轨视图 | 不改 | 兼容保持 |

### Phase 4 — 验证（verifier 纳入 --edsim/自检）

1. 新建样例 其他\Chart\测试格式\multi_stage_test.mil（2~3 场：左 4K Mania + 右 Phigros + 可选 7K IIDX）。
2. Program.cs CliTestTools.RunEdsim 增断言组：「多场」= 场数/场 rect 序列化往返/场切换音符归属/保存含 stages/加载 Stage.Count（~10 断言）。
3. --shotdemo 多场谱截图（human 回归口径）；--selfcheck 全绿；宿主+引擎构建 0 警告 0 错误；单场截图与改动前一致（防回归）。
4. 若后续加 --chartcheck 类工具，stages 也进入 RunAiCheck 视图（每场独立 ChartData，ValidatorOptions 按场 mode/keys）。

### Phase 5 — 文档与示例

- 本文档 + 其他\README.md 多场小节（与 t10 文档任务协同）；引擎侧 DemoRunner 不改（多场为宿主功能）。

**总改动估计**：Models.cs ~30 行 / ChartParserExtra.cs ~120 行 / GamePanel.cs ~350 行 / ChartEditorPanel.cs ~400 行 / 验证工具 ~100 行 —— 全部位于现有文件新增或参数化处，无新项目、无新依赖、无引擎库改动。

---

## T27 实现记录（2026-08-26，verifier 实现、未构建）

已按本契约落地 v1（全部实现、待 verifier 统一构建验证）：

- **引擎**：ChartModel.cs +StageSpec {Id,Name,Mode,KeyCount,X/Y/W/H} + ChartData.Stages；RhythmCore.cs RhythmNote.StageId；ChartValidator.cs +ValidateStages（STA_COUNT >4 场 / STA_RECT rect 归一化 / STA_IDX 索引与音符引用）；DemoRunner.cs +MultiStageCheck（[多场同屏]：两场同谱=StageSpec + StageId 分场（4K 轨场 4 + Phigros 线场 3，同全局时间）→ 两场独立 Ruleset 无头全命中（4/4、3/3）→ 跨场输入 0 命中（互不串场）→ Validator 注入 STA_COUNT/STA_RECT/STA_IDX 全检出）。
- **格式**：Models.cs +Stage/Note.Field/ChartEvent.Field/Chart.Stages；ChartParserExtra.cs SerializeMil 写 stages[]（id/name/x/y/w/h/keyMap；无 stages 不写）+ ParseMil 读 stages（钳制/对齐）+ AddMilNotes/AddMilEvents 读 "f" + 平铺 f 分派 + 部件音符 Field=索引。
- **宿主游玩（GamePanel.cs）**：StageRT 运行时（Notes/Eng/KeyCol/AutoIdx/SweepIdx/Holds）+ BuildStageRuntime（stages 存在=多场；否则单场兼容 _eng）+ 每场独立 JudgementEngine（stage0=_eng）+ 键盘路由 MultiKeyDown（每场 keyMap 块候选 → |dev| 最小、±0.5ms 平局取小场号；Up 按 Down 决议释放）+ MultiTapAt（场 rect 命中、逆序 z-order、转局部）+ MultiStageStep（场 1..n 漏判扫除/自动游玩/长条完成）+ PaintMultiStages（PushQuadLayer 裁剪 + ComputeLayoutFor 局部布局 + 复用 Draw* + 边框/场名/合并 HUD）+ 结算合并（_score/_acc/_maxCombo/_hits 多场感知，Finish 零改动）+ 旧谱/单场路径零改动。
- **编辑器（ChartEditorPanel.cs）**：「🎛 多场」开关 + X/Y/W/H 数值定位（StNum/SyncStageBoxes/ApplyStageRect）+ 场选择器=现有部件框（当前场=当前部件，音符写入零改动）+ AddPart/DeletePart/SwitchPart 同步 stage 布局 + LoadChart SyncStagesFromChart + BuildChart 写回 c.Stages（>1 场）。
- **取证**：Program.cs +--multishot "<outDir>"（RunMultishot：构造双场谱 → .mil stages 往返断言（含旧谱单主场兼容）→ 自动游玩 → 离屏双场截图 multistage.png → multishot.log）；新建样例 其他\Chart\测试格式\multi_stage_test.mil。
- **v1 限制（登记）**：事件/锁定几何沿用主场上下文（各场独立事件渲染待 v2：Draw* 事件源参数化）；拖框定位=预留（本版数值定位满足「拖框或数值」）；多场禁段位挑战/AI 演示/联机/LoopComposer（单场路径不受影响）；多场长条 = 头判+松开/到期完成（无打断扣分）。

## 附：与现有功能的关系速查

- 多部件（M 键切换）：**保留**（parts-only 谱）；多场模式是"同时展示"的新范式，二者由 stages 有无区分。
- LoopComposer / 联机 / 段位挑战：多场禁用（§5）。
- AI 演示/陪玩：每场独立 AiPlayer 路由（§2.4），无断言改动。
- 引擎库（MilestoneEngine）：**零改动**（多场为宿主呈现层语法；ChartValidator 每场独立调用已满足查错）。
