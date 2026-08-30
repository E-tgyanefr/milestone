# 编辑器 M3.5 体验设计细化（Unity 对齐批——t129/t131/t136 规格基线）

> 起草：eng-design-vis（引擎策划）· 指令：captain『有新增值产出再报（如…编辑器 M3.5 体验细化）』——本文件=M3.5 的签名级设计（供 t129/t131/t136 实现引用；与 t130（AI 面板集成）/t126（SceneView 3D）/t125（双语）互链）。
> 原则：Unity 对齐（MA7 术语）+ 零第三方（EditorUiKit 自绘）+ 确定性（面板状态序列化可复现）+ 不破坏 M3 既有（六区/快照/中文）。

---

## 0. M3.5 范围

| 项 | 来源 | 交付 |
|---|---|---|
| Inspector 折叠/分组 | t129（本地 AI 评审员第 1 批） | EditorUiKit+InspectorPanel 扩展 |
| 引用选择器拖拽（Prefab/资产引用） | P1 注册项+t129 | Inspector 引用字段控件+Hierarchy/Project 拖放 |
| Play-Stop 运行时状态规则 | t129 | Play 模式 Inspector 只读+运行时值着色；Stop 还原（快照已具） |
| 术语对齐（MA7） | t129+engine-mainstream-alignment | 面板/按钮英文标签核对+残留中文清理（括注双语保留——t125 定案） |
| SceneView 3D 视图 | t136（t126 §编辑器已设计） | 本文件=交互签名细化（t126 的补足） |
| 中文/字体复验 | t125 后 | CjkFont/双语渲染+emoji 口径复验（试玩+引擎使用者） |

---

## 1. Inspector 折叠/分组（EditorUiKit 扩展）

### 1.1 控件
```cpp
// uikit.hpp 增补（EditorUiKit 内——零第三方）
class FoldableSection {           // 可折叠分组头（▲▼ 图标+标题+可选勾选框）
public:
  void Draw(IRenderer&, const Rect&);            // 命中检测→toggle（点击头=折叠切换）
  bool Open() const; void SetOpen(bool);
  const std::string& Title() const; int ContentHeight() const;   // 折叠=ContentHeight 0
  void SetPersistent(bool on, const std::string& key);           // 折叠态持久化（EditorConfig JSON）
};
class PropertyRow {                // 字段行（label+编辑控件+工具提示）
public:
  enum class Kind { Float, Int, Bool, Vector2, Vector3, Quat, String, Enum, AssetRef };
  void Draw(IRenderer&, const Rect&);            // 未映射类型=只读灰显+类型名
  Kind Kind() const; const std::string& FieldName() const;
};
```

### 1.2 InspectorPanel 规则
- 分组（Inspector 顶部→底）：对象头（名称+激活勾+Tag）→ Transform 组 → 每组件一组（组件名+Enable 勾+折叠头）。
- 默认展开=Transform+脚本组件；折叠组=记忆（persist key=instanceId+组件类型——**持久化于 EditorConfig JSON（ms_assets_save_text）**——确定性（同会话重开一致））。
- 字段类型映射=ReflectionRegistry 字段表（RHYGEMAKER_REFLECT）：double/int/bool/string/Vec2/Vec3/Quat/枚举（int 表达）/AssetRef（资产 guid 字符串——见 §2）。

### 1.3 验收（A-组）
- IN-1：折叠头点击 toggle（命中区=头行±4px）；折叠组不绘制内容区域（ContentHeight=0）。
- IN-2：折叠态持久化（保存-重开编辑器=同上）。
- IN-3：未映射字段=灰显只读+类型名（不崩）。

---

## 2. 引用选择器拖拽（Inspector AssetRef + Hierarchy/Project 拖放）

### 2.1 资产引用字段控件
- 字段类型=AssetRef（guid 字符串）；Inspector 行=「类型名+guid 前 8 位+…+『选择』按钮」。
- 选择=下拉/弹层（ProjectPanel 过滤当前类型——M3.2 ProjectPanel 已有 List+双击）；拖放=Hierarchy/Project 节点拖到字段行（命中高亮→释放=赋值+CommitField）。

### 2.2 拖放管线（跨面板——Editor 内部事件）
```cpp
// editor_app.hpp 增补（Editor 内部——不触 Core）
enum class DragKind { GameObject, Asset, Field };
struct DragPayload { DragKind Kind; ms_id GoId; std::string AssetPath; std::string FieldType; };
// EditorApp::BeginDrag(payload) / DnD 目标面板命中（HierarchyTree/ProjectPanel/Inspector 字段）
// OnDrop=目标面板处理（Hierarchy 丢弃=设为子对象；Inspector AssetRef=赋值；Inspector GO 引用=赋值）
```
- 拖拽视觉=虚线框+目标高亮（EditorUiKit DrawRect 虚线——**新增 DrawDashedRect 助手（uikit 内）**）。
- 释放语义：Project→Hierarchy 空白=InstantiatePrefab（M3.4 重解析实例化已具）；Project→Inspector AssetRef=赋值 guid；Hierarchy→Inspector ComponentRef=赋值对象 id；Hierarchy→Hierarchy=SetParent（keepWorld 默认 false）。

### 2.3 验收（D-组）
- DR-1：AssetRef 字段=选择器弹层（当前类型过滤）→赋值→PropertySet 持久化（场景保存/重载一致）。
- DR-2：Project→Hierarchy=实例化 Prefab（子树完整+AssetRef 记录——M3.4 语义复用）。
- DR-3：Hierarchy→Inspector=字段赋值+场景保存回环。
- DR-4：拖拽中目标高亮+释放无事件泄漏（无窗口捕获残渣）。

---

## 3. Play-Stop 运行时状态规则

### 3.1 规则表
| 模式 | Inspector | Hierarchy | Project | Toolbar |
|---|---|---|---|---|
| Edit（非 Play） | 读写+折叠+引用拖拽 | 树编辑（增删改/拖放） | 浏览/双击加载 | ▶Play/保存/新建 |
| Play | **只读**（字段灰显+运行时值着色：改动画=琥珀/只读=灰——组件字段=「运行时值（只读）」样式） | 只读+Play 标记（▶ 徽章——运行时增删对象标记） | 浏览（加载禁用——防切换场景破坏播放态） | ▶→■Stop/保存禁用/⏸Pause |
| Pause | 同 Play（只读） | 只读 | 只读 | 继续/Stop |
- Play 中组件增删=**禁止**（按钮灰+提示「运行中不可修改（Stop 后重开）」——与 Unity 行为一致；运行时场景对象增删=编辑器不参与（PlayController 快照隔离——运行修改弃用=ED4 已定）。
- Stop=快照还原（M3.3 已具）——**运行时改动丢弃**（语义保持；若未来要「保留到编辑」=P2 决策点——不默认）。

### 3.2 验收（P-组）
- PS-1：Play 进入→Inspector 全部只读+组件字段着色（运行时值=琥珀高亮）；Toolbar 保存/新建灰。
- PS-2：Play 中加组件按钮灰+提示；Hierarchy 拖放不生效（无副作用）。
- PS-3：Stop→快照还原（运行修改弃用——M3.3 断言复用）。

---

## 4. 术语对齐（MA7）

- 核对表（MainstreamAlignment D-表）：类名=英文（已有：HierarchyPanel/InspectorPanel/SceneView...）；面板标题=英文+中文括注（t125 双语定案——不改回纯英文）；按钮 Tooltip=英文主+中文括注。
- 遗留清理：Console 日志前缀（[Edit]/[Play] 段区分——MA7 术语）；Inspector 组件头注释（类型完整名 brevity）——**实现清单**。
- 验收（T-组）：TL-1 面板/按钮标签对照表 diff 为零（英文主+中文括注）；TL-2 Console 前缀=Edit/Play 可区分。

---

## 5. SceneView 3D 视图（t136——t126 的交互签名补足）

- 切换：SceneView 工具栏 2D/3D 按钮（或按 G 循环——**推荐按钮**）；3D 状态=EditorConfig 持久化。
- 交互（含选中对象时）：
  - 中键拖=orbit（绕焦点；焦点=选中对象中心或场景原点；Y 轴锁定可选——决策 **M3.5-D1 已拍板采纳** ✅：默认自由 orbit（无 Y 锁定）——与 Unity 默认一致）；
  - 右键拖=pan（画面平移，深度平面）；
  - 滚轮=dolly（透视：视野距离；正交：正交缩放）；
  - 焦点：双击对象=聚焦（F 键=聚焦选中）。
- 显示：地面网格（25% alpha+主轴 X/Z 线）+轴向线（RGB=XYZ 长度 1.5）+选中对象 wireframe 高亮（MeshRenderer 几何=Render3D 数据——取出线框）。
- 相机跟随：选中 Camera3DComp 组件对象=「相机预览」模式（SceneView 显示该相机视角——**t126 已有语义**；M3.5=入口接线：选中相机组件→SceneView 顶部徽章[相机预览]+Esc 退出）。
- 验收（V-组）：SC-1 2D/3D 切换+持久化；SC-2 orbit/pan/dolly 三手势（600 帧无漂移（焦点锁定））；SC-3 网格+轴向线可见性（黄金帧外=不确定帧——**3D 视图=编辑器视口（非黄金帧面）**）；SC-4 相机预览模式（选中→预览→Esc 返回）。

---

## 6. 中文/字体复验（t125 后续）

- 复验清单：六区标题/工具栏按钮/右键菜单/树行/Inspector 字段/Console 中文 log（t125 断言已有——试玩视觉复验=无重叠/无漏字/无 �）。
- **t133 基线（已闭环——本次复验以之为准）**：editor_text.hpp 共享 DrawUiText（CJK GDI 12pt≈16px / ASCII VGA 2×=16px 同基线；kTextLineH=20；TextCenteredY）+uikit fontScale 默认 2.0+控件行距随字号+TabBar Measure 同步；CJK gPt 保持 12 与 VGA 16px 同基线——**复验新增项：文字 2x 可读性（试玩——六区/按钮/树行/Inspector/Console=无重叠/无截断）**（eng-coder-vis t133 建议）。
- emoji 口径：引擎侧（Editor）与游戏层（引擎壳）一致（码点×1.15 槽宽+分串测量——t34/t6 口径）——**M3.5 收口**：Editor 若出现 emoji=按口径（Editor 目前=VGA+CJK——无 emoji 位图；决策 **M3.5-D2 已拍板采纳** ✅：Editor UI 不使用 emoji（VGA font 无 emoji；用文字/符号替代——与 t125 双语策略一致））。

---

## 7. 与前序设计互链

- t130（AI 助手）：六区集成已设计（Toolbar AI 徽章/Inspector AI 面板/Console 过滤/Project 扫描）——M3.5 只做接线（t131 实现引用）。
- t126（3D）：SceneView 3D=本文件 §5 细化（不重复）。
- t119（M3）：六区/快照/EditorUiKit——本文件=增量。
- t129（本地 AI 评审第 1 批）：本文件 §1-§4=评审项规格化。

---

## 8. 验收汇总（M3.5 全量）

| 组 | 项 | 说明 |
|---|---|---|
| A | IN-1..3 | Inspector 折叠/分组/持久化/未映射只读 |
| D | DR-1..4 | 引用选择器/拖放管线/释放语义/无泄漏 |
| P | PS-1..3 | Play-Stop 规则（只读/禁用/还原） |
| T | TL-1..2 | 术语对齐 |
| V | SC-1..4 | SceneView 3D 交互 |
| F | 复验 | 中文/emoji 口径收口（无 �/无重叠） |

---

*终稿*（M3.5 体验设计：Inspector 折叠分组（FoldableSection/PropertyRow）+引用选择器拖拽（DragPayload 管线＋DrawDashedRect）＋Play-Stop 规则表＋术语对齐（MA7）＋SceneView 3D 交互细化（orbit/pan/dolly/焦点/相机预览）＋中文复验（D2：Editor 不用 emoji）；验收 A/D/P/T/V/F 六组；互链 t129/t130/t131/t126/t119/t125；决策 M3.5-D1（自由 orbit）/D2（无 emoji）——**已拍板（D1 采纳/D2 采纳）✅——规格基线转 t129/t131/t136 实现引用**（最终版）。

---

## 9. t129 实现评审记录（eng-design-vis · t129 完成后）

> 评审对象：t129（M3.5 §1-§4：IN/DR/PS/TL 四组）——eng-coder-vis 实现+自验（ms_test_editor 16/16＝12+4 新用例；全链 68 用例；ctest 5/5；0 警告；黄金帧不变；editor ALIVE；双源 MD5=0）。

### 9.1 验收 mapping（逐项=通过）

| 项 | 证据（实现/测试行号） | 判定 |
|---|---|---|
| IN-1 折叠 toggle+命中 | uikit.hpp L121-137（FoldableSection：Open/SetOpen/Toggle/HeaderH 命中）+test L294-328（ToggleGroupAt 头行命中→open=false→OnPersistFold） | ✅ |
| IN-2 折叠持久化 | editor_app.hpp L74-76（SaveEditorConfig/LoadEditorConfig）+test L326-327（SaveEditorConfig→Assets.Exists(mcfg)） | ✅（断言=文件存在；值往返=试玩复验补） |
| IN-3 未映射灰显 | uikit.hpp L146/148（Kind=Unmapped+TypeHint；ReadOnly）+editor_panels.cpp L208/245（playMode/Unmapped/AssetRef 抑制编辑） | ✅ |
| DR-1 AssetRef 赋值 | test L350-377（SimulateDropAsset→OnDropAssetRef 赋值闭环）——**选择器弹层=按标注 P1**（偏离①） | ✅（弹层 P1 登记） |
| DR-2 Project→Hierarchy=实例化 | editor_app.cpp L247-255（HandleDrop：空白区 .msprefab→EditorInstantiatePrefab） | ✅（实现存在） |
| DR-3 拖放闭环 | test L350-377 | ✅ |
| DR-4 虚线框视觉/无泄漏 | uikit.hpp L118（DrawDashedRect）+DropTargets 填充+释放无捕获 | ✅ |
| PS-1 只读+运行时着色 | editor_app.cpp L369/399（SetPlayMode）+inspector PlayMode 行（RuntimeHot=琥珀）+toolbar 保存灰（L403 LogWarn+return） | ✅ |
| PS-2 增删禁用 | editor_app.cpp L432（AddNewGo playing_ 门控+警告）；SaveScene L403 同 | ✅ |
| PS-3 Stop 快照还原 | editor_app.cpp L388-399（恢复+SetPlayMode(false)）+test L331-346（AddNewGo 无副作用+根数还原） | ✅ |
| TL-1 双语标签保留 | t125 定案（双语主+括注）——保存按钮中文样式保留（L119-123） | ✅ |
| TL-2 Console [Edit]/[Play] | editor_app.cpp L371/396/403/432 前缀+test L379-386 | ✅ |

### 9.2 偏离记录（2 项——全部采纳+注解）

| # | 偏离 | 处置 |
|---|---|---|
| ① | **DR-1 选择器弹层=标注 P1**（当前=拖放赋值+SimulateDropAsset 闭环；弹层交互未做） | 采纳——P1 登记（弹出过滤选择器随 t136/后续批；不阻塞） |
| ② | **DR-2 语义覆盖实现**（HandleDrop .msprefab 空白=InstantiatePrefab 存在——非仅测试语义） | 采纳——实现粒度=设计（✓） |

### 9.3 观察项（不阻塞·登记）

- O-1：IN-2 持久化单测=文件存在性；折叠值往返（保存-重开=同折叠）留试玩复验。
- O-2：PS-2 的 Hierarchy 拖放门禁（Play 中）——Inspector 行有 playMode_ 门控；**Hierarchy 树拖放未见于门控（HandleDrop 无 playMode_ 检查可见）——P1 观察**：Play 中拖放若生效=运行时增删（会被快照隔离弃用——无破坏但建议禁用；随 t131/复验批处理）。
  - **✅ 已修复（t129 评审后即闭环）**：HandleDrop 加 playMode_ 门控——`if (playing_) { LogInfo("[Play] drop ignored (runtime)"); return; }`（editor_app.cpp:258-259）＋SimulateDropAsset 早前已有 playing_ 检查（双保险）；editor 23/23 保持；全链 87/ctest 6/6/0 警告/黄金帧不变/双源 MD5=0（修复已同步双树）——**O-2 观察项清零**。
- O-3：TL-1 面板级标签核对表=试玩复验项（t136 同批）。

### 9.4 结论

**t129 通过**（A 组 IN×3/D 组 DR×4/P 组 PS×3/T 组 TL×2 全部 ✅；偏离 2 项全采纳；观察 3 项登记）。V 组（SceneView 3D）=t136 承接口径不变（§5/§7）。实现质量=与设计基线一致（uikit 增量/管线/快照复用合理；0 警告/黄金帧不变）。

