# D · 编辑器对齐差距表 v2（源码事故后盘点 · 方法级 + 实机证据标注）

> 日期 2026-08-24（盘点轮）· 调查员 researcher · 第 3 轮（判据素材恢复后逐帧取证）
> 背景：08-22 归档覆盖 08-23 新版（消息板 08-24 记录"丢 D2/D3/D4 + CLI 测试入口，已重建核心 --modetest/--selfcheck"）。
> 本表每行标注【证据文件】= obj\ref\ 实机帧/指南摘要中的可见依据；源码位置= Charting\ 下 文件:行+方法/字段名。
> 判据来源可靠性：🟢官方文档/实机帧（本次 14 帧已逐张取证）｜🟡旧判据文本（v1 表 + 消息板 T56-T71 官方原义记录，原参考文件丢失）
> 只报告事实，未修改任何源码。

## 0. 重点确认（方法级 grep 结果）

| # | 名单（判据表已列功能） | 状态 | 源码位置（当前 Charting\ChartEditorPanel.cs，共 6087 行） |
|---|---|---|---|
| 1 | SyncAnimPanel / SyncAnimPanelPublic | ✅ 在 | `:655` `void SyncAnimPanel()`；`:672` `internal void SyncAnimPanelPublic()`；门控 `:667` `_animPanel.Visible = GameSettings.TestModes && (ph||ar)` |
| 2 | _animPanel（右侧动画菜单面板） | ✅ 在 | `:127`（字段）、`:188`（new Panel Dock=Right W=320）、`:417`（Controls.Add(flow)） |
| 3 | SetLineEventAt / SetLineEventAtPlayhead | ✅ 在 | `:2183` `internal void SetLineEventAt(string type, double value, double atTime)`（同刻合并→PushUndo→EaseNew）；`:4089` `SetLineEventAtPlayhead`；使用点 `:5868`(moveY)/`:5874`(moveX)/`:5882`(rotate) |
| 4 | HitCurveKeyframe | ✅ 在（仅单帧） | `:5000` `(int k, int idx, ChartEvent ev)? HitCurveKeyframe(...)`；调用 `:5484`(右键缓动)/`:5517`(左键拖 DragKind=26)；无多选/框选/组拖/批删 |
| 5 | ApplyCurveEditor（贝塞尔编辑器绘制求值） | ❌ 丢失 | 无此方法；现存仅 `:4489` `ApplyEaseEditor`（Ease 公式采样）+ `:4440` 调用；ChartEvent.Bezier(:77 Models.cs)、解析(ChartParserExtra.cs:470,608)、游玩端(Play\GamePanel.cs:151,157 BezierEval)仍在；DragKind 最大 27（:2756），无 29/30 手柄 |
| 6 | PhigrosLineGeom（判定线几何） | ✅ 在 | `:4040` `void PhigrosLineGeom(...)`；父线叠加 `:4021` `ParentLineTransform`；调用 `:5609`/`:5880` |
| 7 | 绑定组 Group | ❌ 丢失 | ChartEvent 无 Group 字段（Models.cs:67-78）；ChartParserExtra 无 bindGroup/group 读取；SerializeEvents(:924-944) 无 group 写出 |
| 8 | Charting\ChartEditorPanel.Canvas.cs | ❌ 不存在 | 仅单文件 ChartEditorPanel.cs；`sealed class EditorCanvas : Control` 内嵌 `:2800`。**缺失，需在 ChartEditorPanel.cs 内恢复或重建** |

## 1. 素材盘点（本次逐帧取证）

**现存 & 已取证（14 帧 + 3 篇 md）**：
| 文件 | 内容（本次读图确认） |
|---|---|
| obj\ref\rpe-curve-fill.md | 官方指南：填充曲线音符（CTRL+F/G 锚/密度=横线间距/种类/曲线 linear·sine·quad/实时色块预览/取消·生成） |
| obj\ref\rpe-undo.md | 官方指南：CTRL+Z/Y、历史操作 ≤20 步、可撤销清单（含执行列表/批量编辑/曲线填充/曲线轨迹/预制事件）、不可撤销=宽度/删线/设置 |
| obj\ref\rpe-special-events.md | 官方指南：特殊事件=X/Y 缩放/颜色渐变 RGB/画笔/文本；实验性 incline/alphaControl/posControl/sizeControl/skewControl/yControl |
| obj\ref\rpe_f30.jpg | 常规设置面板：窗口大小 0.780/音符宽度 175/判定线默认宽度 1.700/判定线默认颜色 237 236 176/实时背景透明度倍率 75/打击音延迟1(Tap/Flick) 0/打击音延迟2(Drag) −30；左侧导航【播放谱面/导出为旧pec/常规/热键/其他/纠错相关/关于/重新谱面/导入谱面/退出编辑】 |
| obj\ref\rpe_f60.jpg | **主编辑界面**：左音符场 62% + 右**五柱垂直事件栏**（蓝柱，值 0/0/0.0/0.0/10.0，左侧时间标尺 1/2）+ 顶部工具栏（200 BPM 显示、MirrorY 下拉、4、0、1.0x、纵向拉伸、撤销/重做/保存）+ 右侧面板按钮【谱面基本信息/生成曲线轨迹/拍数管理/预制事件/判定线管理/填充曲线音符/配置缩略图/历史操作/谱面纠错】+ 状态栏 Notes/Events/TotalLines/TotalNotes/TotalEvents(v1.2.1 Public @cmdusj) |
| obj\ref\rpe_f90.jpg | **按住 R 放置 Hold**（字幕"按下R放置Hold，并在合适的位置再次按下R完成放置"）；Hold=青白圆角长条；判定线右端编号 3/2；右上角"Untitled Default"=线名/材质名；右侧面板 9 按钮；顶部工具栏含 AttachX 下拉 |
| obj\ref\rpe_f120.jpg | **旋转事件编辑面板**：起始时间 0:0/1/结束时间 1:0/1/起始角度 0.00/终点角度 0.00/缓动类型 1 In linear/**勾定** ☑/**绑定组** 0/按钮【粘合|切割|随机|递推|拆分|删除】/下方黄色缓动曲线预览图；字幕注释透明度特殊值（255=完全不透明/0=完全透明/<0=隐藏其上所有内容，rpe 扩展功能，模拟器不读取） |
| obj\ref\rpe_f150.jpg | **生成曲线轨迹面板**：起始时间/结束时间(单位 beat)/X 参数方程/Y 参数方程/X 侧时间缓动 1/Y 侧时间缓动 1/密度 4.0/【生成】；来源=右侧"生成曲线轨迹"按钮 |
| obj\ref\rpe_f180.jpg | **填充曲线音符面板**：曲线起点音符【选择】0:0/1/曲线终点音符【选择】−405.0 2:0/1/密度 1.0/音符种类 Drag/曲线类型 In Sine/【取消】【生成】；事件柱中一栏变绿（选中态）；状态栏 Notes:2 Events:5 TotalLines:2 TotalNotes:2 TotalEvents:10 |
| obj\ref\rpe_f210.jpg | **配置缩略图面板**：竖条拍数 8/底边高度 120/竖条宽度 200/竖条间隔 100/音符 x 下界 −675/音符 x 上界 675/音符半宽度 15/音符半高度 4/数字大小 1.0/每小节拍数 4/显示为拍数 ☑/跟随总进度条 ☑；缩略图=8 列迷你网格 |
| obj\ref\rpe_f3_60.jpg | **Tap 音符编辑面板**：起始时间 1:0/1/结束时间 1:0/1/X 坐标 0.00/下落朝向 Up/速度 1/Y 值偏移 100.00/真值 Real/宽度 1.0000/可视时间/秒 999999.0000/透明度 255/【取消|删除|保存】+ 底部状态栏（Notes:1 Events:5 TotalLines:2 TotalNotes:1 TotalEvents:10）+ 字幕"Y 值偏移：…以判定线锚点为原点在 Y 轴方向偏移…RPE 单位" |
| obj\ref\rpe_f3_180.jpg | **多音符编辑面板**：数值种类 Speed/数值下界 0.0/数值上界 0.0/缓动类型 1/周期数列 1/扰动 0.0/修改方式 By/【清空|删除|应用】；左下"剪贴板时间范围：1:0/1 to 1:1/2"；五柱全蓝 |
| obj\ref\rpe_f3_240.jpg | **Y 坐标移动事件编辑面板**：起始时间 0:0/1/结束时间 1:0/1/Y 坐标起点 0.00/Y 坐标终点 100.00/缓动类型 1 Out linear/勾定/绑定组 0/【粘合|切割|随机|递推|拆分|删除】+ 缓动曲线预览；事件柱中一栏变绿且值 100（选中 Y 事件高亮）；字幕"勾选勾定开关后结果中…的数值保持一致" |
| obj\ref\rpe_f3_300.jpg | Y 坐标移动事件编辑（起始 3:0/1/结束 4:0/1/起点 0.00/终点 0.00/缓动 1 Out linear/勾定/绑定组 0/【粘合|切割|随机|递推|拆分|删除】）；音符区青白长 Hold 柱；判定线右端编号 5/4；左下"剪贴板时间范围：0:0/1 to 1:0/1"；状态栏 Notes:0 Events:9 TotalLines:2 TotalNotes:0 TotalEvents:14 |
| obj\ref\rpe_f3_360.jpg | Tap 音符编辑（起始 1:1/2/结束 1:1/2/X 坐标 405.00/Up/1.00/Y 偏移 0.00/Real/宽度 1.0000/可视 999999.0000/透明度 255）；字幕"当这个值为负数时，强制将所有判定线显示为满透明度，音符的透明度为设置数值的绝对值" |
| obj\ref\rpe_f3_420.jpg | **Flick 音符编辑面板**：起始 1:1/2/结束 1:1/2/X 坐标 0.00/Up/速度 1.00/Y 值偏移 0.00/真值 Real/宽度 1.0000/可视 999999.0000/透明度 255/【取消|删除|保存】；粉色 Flick 音符条；五柱蓝 |
| obj\ref\rpe_p2.mp4 / rpe_p3.mp4 | 视频源（p2=设置+主界面教程 / p3=事件面板+音符编辑实操），可再提帧 |

**确认缺失**：bili_ref_*（含 0/1/3.jpg，全盘 0 结果）、rpe_ref_*.png、rpe-line-mgmt.md、rpe-handle-notes.md、rpe-handle-events.md、rpe-batch-edit.md、arccreate-line.cs、osu-dist-snap.cs。
→ 判据文本回退口径：v1 表+消息板 T56-T71 官方原义记录仍有效（如 arc 10 字段/ComposerDistanceSnapProvider 公式）；重抓管道=T68/T70 既有脚本。

## 2. 主表：玩法 | 编辑器 | 判据 | 现状 | 证据文件 | 源码位置（Charting\ 默认）| 优先级

### 2.1 Phigros ↔ RPE（Re:PhiEdit）

| 判据 | 现状 | 证据文件 | 源码位置 | 优先级 |
|---|---|---|---|---|
| 判定线事件关键帧 5 类 + 缓动曲线视图（横=时间纵=值，右键切缓动） | ✅ 仍在 | 🟢rpe_f3_240.jpg（Y坐标移动事件编辑+缓动预览）；🟢rpe_f120.jpg（旋转事件编辑+缓动类型 1 In linear） | ChartEditorPanel.cs:4372 `DrawPhigrosEventTimeline`（水平 5 通道，与实机五柱差异见"垂直柱"行）、:5000 `HitCurveKeyframe`、:5517-5528（DragKind=26 单帧）、:5484-5507（右键缓动菜单）、:2183 `SetLineEventAt`、:4089 `SetLineEventAtPlayhead` | — |
| 贝塞尔缓动（bezierPoints 解析+编辑器绘制+4 点手柄） | 🛠 半残：模型/解析/游玩端 ✅；编辑器绘制+手柄 ❌ | 🟡判据文本 v1 D2f + 消息板 T54（官方 bezierPoints 语义；原参考图丢失） | 解析 ChartParserExtra.cs:466-473、:608-628；游玩 Play\GamePanel.cs:151,157 `BezierEval`；编辑器 ChartEditorPanel.cs:4440（仅 ApplyEaseEditor 采样，无 ev.Bezier 分支）、:2756（DragKind 至 27） | P1 |
| 多判定线 1~64、拆线/绑线、父子线 | ✅ 仍在 | 🟢rpe_f90.jpg（两条编号判定线 3/2）、🟢rpe_f3_300.jpg（编号 5/4 多线） | ChartEditorPanel.cs:76 `SplitLineAt`、:98 `BindLine`、:4021 `ParentLineTransform`、:4040 `PhigrosLineGeom`；上限 64 `:224`；落盘 `:1857-1859`、ChartParserExtra.cs:833-839 | — |
| 事件键帧列表编辑（_evtList） | ✅ 仍在 | 🟢rpe_f60.jpg（五柱+值标注 0/0.0/10.0 等价信息） | ChartEditorPanel.cs:141-142 `_evtList`、:523 `RefreshEvtList`、:2206 `ShowEventEditor`、:2315-2319（事件编辑数值行） | — |
| 音符相对坐标+时间轴缩放/滚动/吸附 | ✅ 仍在 | 🟢rpe_f90.jpg（网格+标尺）；🟡v1 表（无轨范式方向指导确认） | ChartEditorPanel.cs:58-60、:1022-1025（_snapBox）、:2764 `SnapStepVal`、:4896 `DrawTimeBar` | — |
| 倍速预览 0.25x~4x | 🛠 控件 ✅；快捷键 I/O/P/[/Ctrl+J/K/L ❌ | 🟢rpe_f60.jpg（工具栏 1.0x 下拉） | 控件 `:68 PlayRate`、`:198-200 _rateBox`(25~400)、`:1551`；快捷键损失 `:5967-6085 OnKeyDown` 无 I/O/P/J/K/L | P2 |
| RPE "next" 衔接 | 🛠 模型/解析/游玩 ✅；编辑器克隆/保存/落盘丢 ❌（**P0 数据保真**） | 🟡判据文本 v1 D2f + 消息板 T54（next 字段已支持待玩侧采样）；原参考图丢失 | 存活 Models.cs:76 `Next`、ChartParserExtra.cs:602-606、Play\GamePanel.cs:135；丢失 ChartEditorPanel.cs:1261 `CloneEvents`、:1840 `BuildPartChart`、ChartParserExtra.cs:936-944 `SerializeEvents`、:789-806 `AddMilEvents` | **P0** |
| 曲线手柄在线拖拽（贝塞尔 4 点） | ❌ 丢失 | 🟡v1 D2f 判据文本（DragKind=30 手柄拖拽语义）+ 消息板 T54 | ChartEditorPanel.cs:2756（DragKind 至 27） | P1 |
| 快捷编辑：粘合/切割/随机/递推/拆分 | ❌ 丢失 | 🟢rpe_f120.jpg、🟢rpe_f3_240.jpg、🟢rpe_f3_300.jpg（事件编辑面板按钮组【粘合|切割|随机|递推|拆分|删除】） | 全 Charting\ 无对应方法（D2g 按钮组未重建） | P1 |
| 工具栏快捷键 I/O/P/[/Ctrl+J/K/L | ❌ 丢失 | 🟡v1 D2g-b + 消息板 T56（tools-bar 语义；原判据=官方指南 tools-bar 页已丢失） | ChartEditorPanel.cs:5967-6085 `OnKeyDown` 无对应分支 | P1 |
| 音符放置快捷键 Q/W/E/R（光标处 tap/drag/flick/hold）、A=反转X、ALT+N=共同编辑视图 | ❌ 丢失 | 🟢rpe_f90.jpg（字幕"按下R放置Hold，并在合适的位置再次按下R完成放置"） | ChartEditorPanel.cs:5969-5994（Arcaea T/H/A/S/C/V 分支）、:5995-6077（通用分支无 QWER、无 NotesOnlyMode） | P1 |
| 绑定组 Group（同组同步+mil group 往返） | ❌ 丢失 | 🟢rpe_f120.jpg、🟢rpe_f3_240.jpg、🟢rpe_f3_300.jpg（事件编辑面板"绑定组 0"框） | Models.cs:67-78 `ChartEvent`（无 Group 字段）；ChartParserExtra（无 bindGroup 读取）；SerializeEvents `:924-944`（无 group） | P1 |
| 勾定事件（蓝色）＋勾定开关字段 | ❌ 丢失 | 🟢rpe_f3_240.jpg（"勾定"☑ 勾选框）、🟢rpe_f120.jpg（"勾定"）；🟡v1 D2n（实机勾定事件=蓝色菱形；原 rpe_ref_1.png 丢失） | ChartEvent 无 Locked/勾定字段（Models.cs:67-78）；曲线绘制 `:4462-4476`（通道色菱形，无蓝色特例） | P1 |
| 判定线元数据（名称/分组/Z 渲染顺序/Cover 遮罩+线信息行） | ❌ 丢失 | 🟢rpe_f90.jpg（左上"Untitled Default"=线名/材质名）、🟢rpe_f60.jpg（右侧"判定线管理"按钮）；🟡v1 D2o 判据文本 | grep 全树无 PhigrosLineMeta/LineRenderOrder/Cover；现存仅线速/alpha 框 `:277-295` | P1 |
| 批量编辑执行列表（MirrorY/MirrorMid/SideSwitch/SideUp/Down/ToReal/Fake/ToTap/Flick/Drag/AttachX） | ❌ 丢失 | 🟢rpe_f60.jpg、🟢rpe_f90.jpg、🟢rpe_f3_60.jpg（工具栏 MirrorY/AttachX 下拉） | 全 Charting\ 无 BatchEdit/MirrorY/AttachX 实现 | P1 |
| 填充曲线音符（Ctrl+F/G 锚+密度/种类/曲线） | ❌ 丢失 | 🟢rpe_f180.jpg（面板：起点/终点选择 0:0/1、密度 1.0、种类 Drag、曲线 In Sine、取消/生成）、🟢rpe-curve-fill.md（官方指南全文在） | 全 Charting\ 无 FillCurveNotes/FillAnchorStart/End | P1 |
| 音符编辑面板（起始/结束时间/X 坐标/下落朝向/速度/Y 值偏移/真值/宽度/可视时间/透明度/取消删除保存） | ❌ 丢失且建模缺字段 | 🟢rpe_f3_60.jpg（Tap 面板全字段+Y 值偏移 RPE 单位说明）、🟢rpe_f3_360.jpg（负值透明度语义）、🟢rpe_f3_420.jpg（Flick 面板） | Models.cs:36-64 `Note`（无 Side/Width/Alpha/VisMs/YOffset）；ChartParserExtra.cs `AddPhigrosNotes`:538 起无这些字段；bili_ref_5 判据图丢失（判据文本见 v1 T69） | P1 |
| 事件键帧多选/框选/组拖/批删 | 🛠 音符级 ✅；事件级 ❌（仅单帧） | 🟢rpe_f3_180.jpg（多音符编辑=数值种类/上下界/缓动/周期数列/扰动/修改方式 By/清空删除应用——一种批量编辑形态）；🟡v1 D2d 判据文本 | 音符级 `:2687 ToggleMultiSelect`、`:2702 DeleteSelected`、`:2684-2712`、`:5577-5604`；事件级缺失 `:5514-5530` | P1 |
| 状态栏「判定线 N · 事件 M」（TotalLines/TotalEvents） | 🛠 基础在（音符/事件数）；判定线计数缺失 | 🟢rpe_f60.jpg、🟢rpe_f3_60.jpg、🟢rpe_f3_180.jpg、🟢rpe_f3_300.jpg（状态栏"Notes: n Events: n TotalLines: 2 TotalNotes: n TotalEvents: n"） | ChartEditorPanel.cs:1202-1208 `UpdateStatus`（无判定线数） | P2 |
| 垂直事件柱（五柱/柱宽=值/内嵌缓动曲线） | ❌ 丢失（现为底部水平 5 通道条） | 🟢rpe_f60.jpg、🟢rpe_f180.jpg、🟢rpe_f3_180.jpg、🟢rpe_f3_240.jpg（右五柱+值标注+绿色选中柱+左侧时间标尺）、🟢rpe_f3_420.jpg | ChartEditorPanel.cs:4372-4486（水平 barH=22 布局）；无 ColumnGeom/ValueToWidth01/YToTime/TimeToY | P2 |
| 拍:0/1 时间显示（时间条+面板；含左下"剪贴板时间范围"） | ❌ 丢失 | 🟢rpe_f120.jpg（起始时间 0:0/1、结束时间 1:0/1）、🟢rpe_f3_60.jpg（1:0/1）、🟢rpe_f3_180.jpg（左下"剪贴板时间范围：1:0/1 to 1:1/2"）、🟢rpe_f3_300.jpg（3:0/1→4:0/1） | ChartEditorPanel.cs:4896-4939 `DrawTimeBar`（分秒格式） | P2 |
| 预制事件按钮组（D3） | ❌ 丢失 | 🟢rpe_f60.jpg、🟢rpe_f90.jpg（右侧面板"预制事件"按钮）；🟡v1 D3（倍速 2x/0.5x 淡出淡入旋转平移语义）+ rpe-undo.md（"应用预制事件"可撤销项） | Charting\ 无"预制"标记 | P2 |
| 右侧功能区按钮组（谱面基本信息/生成曲线轨迹/拍数管理/判定线管理/填充曲线音符/配置缩略图/历史操作/谱面纠错） | ❌ 丢失 | 🟢rpe_f60.jpg、🟢rpe_f90.jpg（右侧面板 9 按钮全清单） | ChartEditorPanel.cs:188 `_animPanel`（现内容=事件编辑控件散列，无功能区按钮组） | P2 |
| 【新】事件编辑面板（起始/结束时间、起点/终点值、缓动类型[数字+In/Out+linear]、勾定、绑定组、粘合/切割/随机/递推/拆分/删除、曲线预览图） | ❌ 丢失（我们事件编辑行只有 时间/结束/值/结束值/缓动 5 框，无勾定/绑定组/快捷按钮/曲线预览） | 🟢rpe_f120.jpg（旋转事件编辑）、🟢rpe_f3_240.jpg（Y坐标移动事件编辑）、🟢rpe_f3_300.jpg | ChartEditorPanel.cs:2315-2319（v1/v2/v3 三框）、:307-320（_evtList 5 列） | P1 |
| 【新】多音符编辑面板（数值种类/上下界/缓动类型/周期数列/扰动/修改方式 By/清空删除应用） | ❌ 丢失 | 🟢rpe_f3_180.jpg | Charting\ 无对应面板；音符级批量仅有 ToggleMultiSelect/Nudge/Delete | P2 |
| 【新】生成曲线轨迹（起始/结束时间、X/Y 参数方程、X/Y 侧时间缓动、密度、生成） | ❌ 丢失 | 🟢rpe_f150.jpg、🟢rpe_f60.jpg（"生成曲线轨迹"按钮）；🟡rpe-undo.md（"曲线轨迹生成"可撤销项） | Charting\ 无对应方法 | P2 |
| 【新】配置缩略图（竖条拍数/底边高度/竖条宽度/间隔/音符 x 上下界/半宽高/数字大小/每小节拍数/显示为拍数/跟随总进度条） | ❌ 丢失 | 🟢rpe_f210.jpg | Charting\ 无"缩略图/配置缩略图"实现；Mania 侧仅 48 桶密度条（见 §2.6） | P2 |
| 【新】常规设置面板（窗口大小/音符宽度/判定线默认宽度/颜色/背景透明度倍率/打击音延迟 1·2） | ❌ 丢失（我们设置页无编辑器专属常规面板；打击音延迟=音频判定偏移，无对应） | 🟢rpe_f30.jpg | Forms\SettingsPanel.cs、ChartEditorPanel.cs（无对应控件）；Play\GameSettings.cs（无延迟字段） | P2 |
| 【新】判定线编号显示（线右端/数字标注） | 🛠 观察（我们有线信息行，但编号位置/呈现待对齐） | 🟢rpe_f90.jpg（线右端 3/2、标尺处 1/2）、🟢rpe_f3_300.jpg（5/4）；🟡消息板 T69 观察项"判定线编号位置（实机线上方中央 vs 我们线左端）" | ChartEditorPanel.cs:4040 `PhigrosLineGeom`（线绘制无编号标注） | P2 |
| 【新】事件柱选中绿色高亮 | 🛠 观察（我们有播放头黄色高亮+近帧白环，无"选中事件柱变绿"语义） | 🟢rpe_f180.jpg、🟢rpe_f3_240.jpg（选中栏绿色+值 100） | ChartEditorPanel.cs:4458-4476（sel=白环） | P2 |

### 2.2 Arcaea ↔ ArcCreate

| 判据 | 现状 | 证据文件 | 源码位置 | 优先级 |
|---|---|---|---|---|
| arc 控制点拖拽+3D 路径可视化（四视角 D1-D4） | ✅ 仍在 | 🟡v1 表（T30/T59 记录；ArcCreate 编辑器参考图未获取） | ChartEditorPanel.cs:2584 `InsertArcCtrlPoint`、:2605 `MoveArcCtrlPoint`、:2624 `DeleteArcCtrlPoint`、:2898 `A3View`、:2910 `Arc3DSetup`、:3568 `DrawArcPath3D`、:5973-5976（D1-D4） | — |
| 天地双线/天线高度/蓝左红右/装饰两态+V 键切虚实 | 🛠 功能 ✅；**CloneNotes 丢 Decor（P0）** | 🟡v1 表（还原度 0.773/0.734 佐证） | 功能 `:5983-5991`（V 键）、Models.cs:48 `Decor`、ChartParserExtra.cs:891；丢失 `:1226 CloneNotes`（未复制 Decor） | **P0** |
| 编辑视图=游玩管线同源预览 | ✅ 仍在 | 🟡v1 表（edshot arcaea-d2g 证据已丢失，判据文本在） | ChartEditorPanel.cs:2910-3019、:3277-3450；游玩 Play\GamePanel.cs（DrawArcaea 同几何） | — |
| .aff 全量 arc 解析（sx/ex/sy/ey/color/trace+arctap） | ❌ 丢失（残留 arc=800ms 静态旧代码） | 🟡v1 D2k + 消息板 T60（官方 AffChartReader.Line 10 字段语义；arccreate-line.cs 丢失） | ChartParserExtra.cs:195-196（`case 2: // arc：保守终点=起点、时长 800ms`）；测试谱 Chart\测试格式\arcaea_arccreate_test.aff 在 | **P0** |

### 2.3 Cytus ↔ Cyunity/Cytoid

| 判据 | 现状 | 证据文件 | 源码位置 | 优先级 |
|---|---|---|---|---|
| 时间轴（拍/小节网格）+位置轴（x 归一化） | ✅ 仍在 | 🟡v1 表（无 Cyunity 编辑器参考图） | ChartEditorPanel.cs:4896 `DrawTimeBar`、:4943 `TracklessFieldAt`(:4952-4953 Cytus 分支)、:842-853 `EventTypesForMode` | — |
| page/speed 事件建模（speed=页拍数） | ✅ 仍在 | 🟡v1 表（E-3 14/14 验收记录） | ChartEditorPanel.cs:848；ChartParserExtra.cs:211-234（PAGE_SIZE/PAGE_SHIFT）；:2206 ShowEventEditor | — |
| 画布事件时间轴按模式泛化（D2b） | ❌ 丢失（回退仅 Phigros 硬编码） | 🟡v1 D2b + 消息板 T50（cytus-d2b4 三通道证据已丢失） | ChartEditorPanel.cs:3072（`Phigros ? 120 : 0`）、:4382（kinds 硬编码）、:3972-3973（仅 Phigros 调用） | P1 |
| LINK 视觉连线（VERSION2 LINK page id1 id2 type [y]） | ❌ 丢失 | 🟡v1 D2l + 消息板 T61（官方 .txt 格式 6 字段；原 edshot 证据丢失） | ChartParserExtra.cs:208-271 `ParseCytus`（无 LINK）；测试谱 cytus_link_test.txt 在 | P1 |

### 2.4 ADOFAI（官方内置编辑器）

| 判据 | 现状 | 证据文件 | 源码位置 | 优先级 |
|---|---|---|---|---|
| 俯视路径+tile 角度（15°吸附）+点选/追加 | ✅ 仍在 | 🟡v1 表（T30/edshot adofai 证据丢失；官方编辑器截图未获取） | ChartEditorPanel.cs:3819 `DrawAdofaiTopdown`、:3917 `AdofaiTopdownHit`、:5116-5123/:5445（DragKind=25）、:5786-5803（case 25） | — |
| pathData+SetSpeed Bpm/Multiplier+angleOffset | ✅ 仍在 | 🟡v1 表（adofai-density 证据丢失） | ChartParserExtra.cs:1183-1192、:1220-1231、:1261-1272 `BpmAt`、:1274-1277 | — |
| SetSpeed Beats 型（每 tile 拍数） | ❌ 丢失 | 🟡v1 D2c + 消息板 T51（adofai-beats 5.3s 手算基准）；测试谱 adofai_beats_test.adofai 在 | ChartParserExtra.cs:1220-1231（无 beatsPerTile/BeatsAt） | **P0** |
| Checkpoint/Twirl/Hold/SetHitsound/MoveTrack/PositionTrack 解析 | ✅ 仍在 | 🟡消息板 08-21 21:50-21:56（11 项对齐记录） | ChartParserExtra.cs:1213-1252、:1304-1317 | — |
| MoveTrack 渲染消费 | 🛠 待核实项 | 🟡消息板 08-21 21:56 记录已实现 | Play\GamePanel.cs（DrawAdofaiReal）；未专项核对 | P2 |

### 2.5 osu!standard ↔ osu!lazer 编辑器

| 判据 | 现状 | 证据文件 | 源码位置 | 优先级 |
|---|---|---|---|---|
| 滑条曲线 B/C/L/P+控制点拖拽 | ✅ 仍在 | 🟡v1 表（lazer 操作截图未获取） | ChartEditorPanel.cs:2534/2545/2555（Insert/Move/DeleteOsuCtrlPoint）、:4731 `HitOsuCtrlPt`、:4700 `OsuCtrlScreenPts`、:5532-5547、:5923-5955（DragKind 23/24） | — |
| Repeats 编辑（End 联动） | ✅ 仍在 | 🟡v1 表 | ChartEditorPanel.cs:6049-6072 | — |
| 网格吸附/时间线/undo/多选 | ✅ 仍在 | 🟡v1 表 | ChartEditorPanel.cs:4956-4965（16 格）、:6016-6021（Ctrl+Z/Y）、:2687-2712 | — |
| 距离吸附（ComposerDistanceSnapProvider） | ❌ 丢失 | 🟡v1 D2h + 消息板 T57（公式语义：beatSnapDist=100×SV×SliderMultiplier/BeatDivisor；osu-dist-snap.cs 丢失） | Models.cs:91-123 `Chart`（无 SliderMultiplier）；ChartEditorPanel.cs 无"距离吸附"；ChartParserExtra.cs:1042（仅局部） | P1 |
| lazer 式物件属性侧栏+谱面 tick 率（0.5~8） | ❌ 丢失且 **SliderTickRate 回退 [General] 错区段** | 🟡v1 D2e + 消息板 T53（[Difficulty] 修正语义） | ChartParser.cs:28（`gen.GetValueOrDefault` 误读 [General]）；ChartEditorPanel.cs 无属性侧栏；Models.cs:109（字段在） | P1 |

### 2.6 Mania ↔ Malody NoteArt

| 判据 | 现状 | 证据文件 | 源码位置 | 优先级 |
|---|---|---|---|---|
| 下落轨道所见即所得+长条拖尾 | ✅ 仍在 | 🟡v1 表（Malody 截图参考丢失） | ChartEditorPanel.cs:3140-3200（DrawTracked/DrawLaneNote） | — |
| 密度柱状图（48 桶绿<8/黄8-15/红>15+DensityCalc） | 🛠 简化版在（每小节每列 红≥4/黄≥2/蓝） | 🟡v1 D1 + 消息板 08-22 D1 记录（48 桶阈值语义） | ChartEditorPanel.cs:3208-3245（内嵌密度条）；DensityCalc 类不存在 | P2 |

### 2.7 横向（验证基建）

| 判据 | 现状 | 证据文件 | 源码位置 | 优先级 |
|---|---|---|---|---|
| --modetest/--selfcheck（T72 重建） | ✅ 仍在 | —（CLI 日志 modetest.log/selfcheck.log） | Program.cs:23-68 | — |
| --edshot/--edsim/--shotdemo/--healthtest/--eventgentest/--ratingtest/--dantest/--libraryrating/--coachtest/--reviewtest/--densitytest | ❌ 全丢 | 🟡消息板 T52-T71 各轮 edsim/edshot 证据目录（obj\edshot\*/obj\edsim\* 已不存在） | Program.cs（仅 modetest/selfcheck）；Forms\MainForm.cs:175-198（仅 autoplay/play/humantest/autoshot/adofai2/uidebug/editor） | **P0** |
| --autoshot（游玩+编辑器后自捕获） | ✅ 仍在 | — | Forms\MainForm.cs:188-189；Play\GamePanel.cs `AutoShotDir`；ChartEditorPanel.cs:3111-3138 `EditorAutoShotTick` | — |
| ChartEditorPanel.Canvas.cs 拆分 | ❌ 不存在 | 🟡消息板 08-22 21:30（"6081行→2文件 EditorCanvas 独立"被覆写回滚） | Charting\ChartEditorPanel.cs:2800（EditorCanvas 内嵌） | 备注 |
| 参考素材：bili_ref_*/rpe_ref_*/rpe-line-mgmt/handle-notes/handle-events/batch-edit.md/arccreate-line.cs/osu-dist-snap.cs | ❌ 缺失（本次 3 篇 md+14 帧+2 mp4 为现存全部） | — | obj\ref\ 现存清单见 §1；重抓管道 T68/T70 | **P0** |
| 测试谱（27 个专用谱） | ✅ 幸存 | — | Chart\测试格式\ | — |
| 开发者模式三合一 | ✅ 已重建 | 🟡消息板 T72 记录 | Play\GameSettings.cs:58、CoreUtil\AppConfig.cs:42,120,171、CoreUtil\ModeSystem.cs:27-31、Forms\MainForm.cs:12-102,909-921、ChartEditorPanel.cs:667,671 | — |

## 3. 统计（本表判据行，逐行清点）

- 总项数：**57**
  - Phigros **30** / Arcaea **4** / Cytus **4** / ADOFAI **5** / osu!std **5** / Mania **2** / 横向 **7**
- ✅ 完整存活：**19**（Phigros 4：事件键帧曲线/多判定线拆绑父子/事件键帧列表/相对坐标吸附；Arcaea 2；Cytus 2；ADOFAI 3；osu!std 3；Mania 1；横向 4：modetest·selfcheck/autoshot/测试谱/开发者模式三合一）
- 🛠 部分存活：**10**（Phigros 7：贝塞尔半残/倍速控件/next 数据保真/事件级多选/状态栏判定线数/判定线编号/事件柱选中高亮；Arcaea 1：Decor 撤销丢失；ADOFAI 1：MoveTrack 待核实；Mania 1：密度简化版）
- ❌ 丢失需重建：**28**（Phigros 19 / Arcaea 1 / Cytus 2 / ADOFAI 1 / osu!std 2 / 横向 3；含 P0 5 项：Next+Bezier 保存链、Decor 克隆、arc arctap、ADOFAI Beats、tick率区段）
- 🆕 新增判据（实机图中可见、v1 未列）：**9**（【新】7 行：事件编辑面板/多音符编辑/生成曲线轨迹/配置缩略图/常规设置/判定线编号/事件柱选中高亮；+2 行既有判据补充 UI 细节：勾定开关、绑定组框、右侧功能区按钮组）

> 校验：19✅ + 10🛠 + 28❌ = 57 = 总行数。🆕 已计入上述三类。

## 4. 重建顺序建议（按"编辑↔游玩一致化+数据保真"影响排序）

1. **P0 数据保真组**：ChartEditorPanel.cs:1261 `CloneEvents`/:1840 `BuildPartChart` + ChartParserExtra.cs:936-944 `SerializeEvents`/:789-806 `AddMilEvents` 补 Bezier+Next；:1226 `CloneNotes` 补 Decor；ChartParser.cs:28 tick率区段 [General]→[Difficulty]；.aff arc 全量+arctap（测试谱在）；ADOFAI SetSpeed Beats（测试谱在）。
2. **P0 验证基建组**：重建 --edshot/--edsim/--shotdemo/--densitytest；判据素材重抓（B站/官方指南仓库管道已通；本次 14 帧+3 md 已是实机基线）。
3. **P1 交互缺项组**（按 v1"对一致化影响"序）：事件编辑面板完整化（勾定/绑定组/快捷按钮/曲线预览，证据 rpe_f120/f3_240/f3_300）→ 绑定组+勾定字段 → 判定线元数据（rpe_f90 线名/材质+右侧"判定线管理"）→ 执行列表（rpe_f60/f90/f3_60 MirrorY/AttachX）→ 填充曲线（rpe_f180+rpe-curve-fill.md）→ 音符编辑面板（rpe_f3_60/360/420，先补 Note 字段）→ RPE 快捷键组（rpe_f90 QWER 字幕）→ 事件级多选组拖 → 时间轴泛化（D2b）→ LINK → 距离吸附+属性侧栏。
4. **P2 风格/细节组**：垂直事件柱+拍:0/1+预制事件+右侧功能区按钮组（rpe_f60 实机布局）/生成曲线轨迹（rpe_f150）/配置缩略图（rpe_f210）/多音符编辑（rpe_f3_180）/常规设置（rpe_f30）/48 桶密度柱/状态栏判定线数（rpe_f60 状态栏）。

## 5. 备注（事实限定）

- ✅=源码可 grep 到对应方法/字段（行号已验证）；❌=无对应实现；🛠=部分存活（存活点与损失点分别标注）。
- 🟢证据=本次逐帧读图确认（14 帧+3 md 均已人工查看）；🟡=判据文本来自 v1 表+消息板（原参考文件/截图证据丢失）。
- v1 表 30 项判据全 ✅ 的验证证据目录 obj\edshot\*/obj\edsim\* 现不存在；证据需重建后重拍。
- T32-T38 功能文件（DifficultyRating/DanLogic/ReviewCard/CoachCard/BadNoteList/EventGen/ChartHealth）grep 全树不存在，属事故连带丢失，建议任务板另行登记。
- 本次 3 篇 md 与 14 帧为队长恢复后**实际存在**的素材；bili_ref_* 经全盘搜索确认不存在（0 结果），判据文本已回退 v1 表+消息板口径。
