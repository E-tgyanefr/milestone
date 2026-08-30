# D · 编辑器对齐差距表（铁命令 D 第二轮）

> 日期 2026-08-22 · 决策者依据：`策划\主代理方向指导.md` §1.x/§2.3（RPE/ArcCreate/Cyunity/osu!lazer/官方 ADOFAI 判据）+ 已实现事实（消息板 T18/T23/T30 记录）+ 首批编辑器截图（`obj\edshot\*`）。
> 判据来源可靠性标注：🟢官方文档/实测 ｜ 🟡社区 ｜ 待实机验证。
> 状态：✅已具备 / 🛠部分具备 / ❌缺失（缺失项即后续修复队列）。

## Phigros（对标 Re:PhiEdit/RPE，🟢 官方指南 Shikochin/RPE-Guide 2026-08-23 逐条比对）
| 判据 | 状态 | 依据 |
|---|---|---|
| 判定线事件关键帧（moveX/moveY/rotate/alpha/speed）+ 9 缓动 + 贝塞尔 | ✅ | T23 RPE 补齐 1-5（曲线视图/双向同步/多选/吸附）+ D2f 贝塞尔全链路 |
| 多判定线 1~64、拆线/绑线、父子线（phimakor） | ✅ | 消息板 T14 记录 + `_lineParentBox` |
| 事件列表编辑（键帧表） | ✅ | `_evtList` + 事件编辑器 |
| 音符相对坐标 (x,y)+beat、时间轴缩放/滚动/吸附 | ✅ | 无轨范式（方向指导确认已按 RPE 实现） |
| 判定期望：倍速预览（0.25x~4x） | ✅ | `PlayRate` + D2g-b Ctrl+J/K/L 快捷键（指南工具栏倍速） |
| RPE "next" 衔接/曲线手柄在线拖拽手感 | ✅ D2f | 贝塞尔全链路（绘制/手柄/拖拽首尾点锁定）；next 字段已支持待玩侧采样验证 |
| RPE 快捷编辑：粘合/切割/随机/递推/拆分 | ✅ D2g（2026-08-23） | 官方指南 handle-events 逐条实现：粘合=上同类尾部填入本头、切割=边界吸附最近横线（保持时长不变）、拆分=当前时刻按时间分配（0=>150 缓动4 从2拍 切开 → 0=>50+50=>150）、随机=尾值随机、递推=据上两个事件（0=>50+50=>100 → 100=>150）；按钮组（动画菜单事件区）+ --edsim D2g 段全 ✅ |
| RPE 工具栏快捷键 | ✅ D2g-b（2026-08-23） | I=播放、O=停止并恢复（全局时间回播放起点）、P=跃进（停止不恢复）、[=从头预览、Ctrl+J/K/L=倍速 1.0/0.75/0.5（指南 tools-bar 同语义）；--edsim D2g-b 段全 ✅ |
| RPE 音符放置/快捷编辑快捷键 | ✅ D2i（2026-08-23） | 指南 handle-notes：Q/W/E/R=光标处放置 tap/drag/flick/hold（吸附横线时间）、A=反转 X、S=翻转、ALT+N=编辑音符/共同编辑 视图切换（隐藏事件曲线区）；提示条更新；--edsim D2i 段 6 步全 ✅ |
| RPE 绑定组（Group：同组事件头尾/缓动实时同步） | ✅ D2j（2026-08-23） | 指南 inside-chart/event："绑定组数值相同的事件属于同一组，修改其中一个会同时修改组内所有事件"→ ChartEvent.Group（0=无）+ 事件编辑行"绑定组"框（改组号即同步组内头尾/缓动）+ 曲线拖拽值同步 + RPE JSON（group/bindGroup）/mil 解析与落盘；顺带修复 BuildPartChart 丢 Group/Bezier 的保存路径 bug（此前 Bezier 事件保存后丢失！）；证据 `obj\edsim\d2j\`（组号保留 + .mil round-trip group=5 ✅） |
| 勾定事件（头尾相同、无缓动曲线、实机显示为蓝色） | ✅ D2n（2026-08-23） | **实机主界面截图比对**（官方指南仓库 124 张 AVIF 参考，obj\ref\rpe_ref_*.png）：勾定事件实机=蓝色；我们的瞬间事件（End=NaN）原用通道色 → 改蓝色菱形+蓝色数值文字；证据 obj\edshot\phigros-d2n\（瞬间键帧全蓝）对比 obj\ref\rpe_ref_1.png（实机蓝色勾定事件）；余：RPE 主界面五事件为**垂直柱**呈现、我们是水平时间轴（功能等价，呈现风格差异已记录观察） |
| 判定线元数据（名称/分组/Z 渲染顺序/Cover 遮罩——判定线管理面板） | ✅ D2o（2026-08-23） | 官方指南 line-management：判定线名称（编辑窗口右上角显示）、分组、Z 轴渲染顺序（大者在上）、Cover/UnCover 遮罩（跨线音符判定前隐藏/始终可视）+ 每线状态信息行（X,Y 角度/ɑ/速度/音符与事件计数）；实现=PhigrosLineMeta 模型 + 动画菜单"判定线信息"区（名称框/分组/Z/Cover）+ LineRenderOrder Z 排序绘制 + mil lineMeta 落盘/载入（根+parts+部件切换）；证据 obj\edsim\d2o\（Z 5/1→渲染顺序 1,0 + mil round-trip + 名称框显示 ✅）+ obj\edshot\phigros-d2o\（UI 可见） |
| 批量编辑执行列表（MirrorY/MirrorMid/SideSwitch/SideUp/Down/ToReal/Fake/ToTap/Flick/Drag/AttachX） | ✅ D2p（2026-08-23） | 官方指南 batch-edit-basics（执行列表语义）：MirrorY=Y 翻转、MirrorMid=按选中中心（排序中间 X）翻转、SideSwitch=反转下落方向、SideUp/Down=全部置上/下、ToReal/Fake=真/假（Decor）、ToTap/Flick/Drag=转换类型（hold 无效）、AttachX=吸附竖线（1/16）；实现=BatchEdit(op) + 动画菜单"执行列表"按钮组（11 项）；证据 obj\edsim\d2p\（MirrorY/ToFlick/SideDown/ToFake/MirrorMid/AttachX 全 ✅） |
| 填充曲线音符（起点/终点锚 + 密度 + 种类 + 曲线形状） | ✅ D2q（2026-08-23） | 官方指南 curve-fill-notes：Ctrl+F/G 锚起点/终点（Hold 取头部时间）、密度=横线间距/填充间距、种类=音符类型、曲线=linear/sine/quad 实时预览；实现=FillCurveNotes（起止间按密度生成中间音符，X 线性插值 Y 按形状：linear 直线/sine 拱形/quad 抛物线）+ Ctrl+F/G 锚快捷键；证据 obj\edsim\d2q\（linear 9→16 + sine 16→19 ✅）；余：实时预览色块（生成即所见）待观察 |
| 音符编辑面板（实机 Tap 音符编辑：时间/X坐标/下落朝向/速度/Y偏移/真值/宽度/可视时间/透明度） | ✅ D2r（2026-08-23） | **B站实机截图判据**（bili_ref_5：Re:PhiEdit v1.0 音符编辑面板——起始/结束时间 287:0/1、X540、Up、速度1.00、Y偏移、Real、宽度1.0、可视999999、透明度255）；实现=Note 扩展（Side/Width/Alpha/VisMs）+ 动画菜单"音符编辑"面板（选中音符显示：时间/X%/Y%/方向/真值/宽度/透明度）+ RPE JSON 解析 + BuildPartChart 落盘；证据 obj\edsim\d2r\（时间+X+方向+真值+透明度+解析 全 ✅）；余：实机面板"速度/可视时间/宽度"与游玩侧取值联动登记观察 |
| 【差】事件拖动排序/多事件框选移动 | ✅ D2d（2026-08-23） | ①Ctrl/Shift+点选切换多选 ②空白拖动=橡皮筋框选（TimelineKinds 全通道，区间事件按起点） ③组拖：drag 任意选中的关键帧=全部同步移动（各通道值域钳制） ④Delete 批量删除 ⑤Esc 取消选择；另修复拖拽时间换算 bug（原用 windowMs 除=1px≠1ms 漂移，改 f.Width 反解 t2x）；自动化证据 `obj\edsim\d2d\edsim.log`（--edsim 交互仿真：单击/框选/组拖/删除/重载 6 步全 ✅）；Phigros 渲染回归 byte-identical |

## Arcaea（对标 ArcCreate，🟢 官方源码 Arcthesia/ArcCreate 2026-08-23 比对）
| 判据 | 状态 | 依据 |
|---|---|---|
| arc 控制点拖拽 + 3D 路径可视化 | ✅ | `Arc3` 控制点 + `_arc3ZBox`；T30 已修 |
| 天地双线、天线高度、蓝左红右、装饰/可接触两态 | ✅ | 游玩侧已还原（还原度 0.773） |
| 【差】ArcCreate 的"编辑视图=游玩管线同源预览" | ✅ 证据确认（2026-08-23） | 编辑器 3D 视图（Arc3DSetup 同相机模型：视角切换/3D 摄像头；证据 `obj\edshot\arcaea-d2g\` 侧视/正前方=游玩侧天地双线+天线同几何）与游玩 DrawArcaea 共用深度投影；支持 1-4 视角切换（D1-D4）；预览一致性以同几何+同相机参数保障 |
| ArcCreate .aff 全量 arc 解析（含 arctap） | ✅ D2k（2026-08-23） | 官方源码 AffChartReader.Line 语义：arc([start],[end],[sx],[ex],[type],[sy],[ey],[color],[sfx],[trace]) + [arctap([timing]),...]；实现=完整 arc() 正则解析（X/Y 归一落盘、EndCol/Decor=Trace、Kind=Color、SliderType=线型 s/b）+ arctap 生成附加 tap + hold/timing/头部完善；证据 `obj\edshot\arcaea-d2k2\`（9.0s 时长正确）+ `obj\shotdemo\arcaea-d2k\`（3D arc 路径+arctap 链+PURE+ 判定 ✅）；测试图 `Chart\测试格式\arcaea_arccreate_test.aff` |

## Cytus（对标 Cyunity/Cytoid）
| 判据 | 状态 | 依据 |
|---|---|---|
| 时间轴（拍/小节网格）+ 位置轴（x 归一化） | ✅ | 无轨范式共用 |
| page/speed 事件（扫描线往返）建模 | ✅ 已可编辑 | speed=页拍数（EventTypesForMode(Cytus)）+ 事件键帧列表；无 page 显式事件=按 speed 推算（与初代 .txt 语义一致） |
| 【差】画布事件时间轴可视化（Phigros 已有同类组件） | ✅ D2b（2026-08-23） | DrawPhigrosEventTimeline → 泛化 TimelineKinds(mode)：Phigros 5 类不变，Cytus speed/bpm/mode、ADOFAI bpm/mode/twirl、osu!std/maimai bpm/mode；FieldRect 底部预留改为按通道数（修复了非 Phigros 无轨模式场高吞掉时间轴的根因）；证据 `obj\edshot\cytus-d2b4\`（speed/bpm/mode 三通道 + 事件曲线/键帧点）与 `obj\edshot\phigros-d2b4\`（回归不变） |
| LINK 视觉连线（官方 .txt `LINK page id1 id2 type [y]` 格式） | ✅ D2l（2026-08-23） | 官方格式解析（6 字段：页/id1/id2/type/y）→ link 事件（Value/EndValue=音符索引，时间=页首+y）+ 编辑器 Cytus 场绘制连接线；证据 `obj\edshot\cytus-d2l2\`（两条斜向连接线可见）+ `obj\edsim\d2l\`（LINK 事件=2/编辑器=2/索引范围 ✅）；测试图 `Chart\测试格式\cytus_link_test.txt` |

## ADOFAI（对标官方内置编辑器）
| 判据 | 状态 | 依据 |
|---|---|---|
| 俯视路径 + tile 角度（15° 吸附）+ 点选/追加 | ✅ | T30（DragKind=25 角度拖拽+15°吸附）；AdofaiReal 俯视主视图（DrawAdofaiTopdown），Routlock 轮盘预览 |
| 每拍 tile 数（变速：pathData + SetSpeed Bpm/Multiplier/Beats）/BPM/事件（旋转/消失等） | ✅ 2026-08-23 | pathData 每 tile 节拍倍率 + SetSpeed Bpm 绝对值/Multiplier 倍率/Beats 每 tile 拍数（本次补齐 Beats 型，此前被静默丢弃→BeatsAt 作用于段长）；角度(angleData→Kind)、twirl/hold/checkpoint/hitsound 事件解析齐全；证据 `obj\edshot\adofai-density\`（pathData 0.5/2 变速列距实测正确）与 `adofai-beats\`（Beats 2→1000ms 宽距、0.5→250ms 密距，时长 5.3s 与手算一致）；回归 6 项 0 |

## osu!standard（对标 osu!lazer 编辑器）
| 判据 | 状态 | 依据 |
|---|---|---|
| 滑条曲线 B/C/L/P + 控制点拖拽 | ✅ | `_osuCurveBox` + Insert/Move/DeleteOsuCtrlPoint |
| 重复次数/repeats 编辑 | ✅ | `Repeats` 字段 + 游玩侧 |
| 网格吸附/时间线/undo/多选 | ✅ | T23 |
| 【观察】lazer "distance snap"（滑条放置距离吸附网格，SnapResult/IDistanceSnapProvider） | ✅ D2h（2026-08-23） | 官方源码语义（ComposerDistanceSnapProvider：beatSnapDist=100×SV×SliderMultiplier/BeatDivisor，端点距离=n×beatSnapDist 保持方向）→ SetOsuSliderEnd 吸附实现 + 工具栏"距离吸附"开关（仅 osu!std 显示）；Chart.SliderMultiplier 字段+解析/落盘（sliderMult，默认 1.4）；自动化证据 `obj\edsim\d2h\`（距离=70px=2×35 误差 0、方向 0 rad ✅）；截图 `obj\edshot\osu-d2h\`（工具栏距离吸附开关可见） |
| 【差】lazer 式"物件属性侧栏"（转盘时长/滑条 tick 率等） | ✅ D2e（2026-08-23） | 选中物件集中属性栏：时间/X%/Y%/时长（hold/spin）/往返/滑条曲线 L-P-B-C + 谱面级 tick 率（0.5~8）；附带修正 SliderTickRate 解析区段（原从 [General] 误读 → 实为 [Difficulty]）与 .mil 落盘/载入（tickRate 字段）往返；自动化证据 `obj\edsim\d2e\`（--edsim D2e 段：滑条选中/曲线 B/时长 714.3ms/往返 2 生效/转盘时长 1500ms/tick 率 2 → 全 ✅）；截图 `osu-prop-selected.png` + `obj\edshot\osu-prop2\` |

## Mania（对标 Malody NoteArt）
| 判据 | 状态 | 依据 |
|---|---|---|
| 下落轨道所见即所得 + 长条拖尾 | ✅ | 有轨范式 |
| 密度柱状图/音符密度警示 | ✅ D1（2026-08-22） | 右侧 48 桶密度柱（绿<8/黄8-15/红>15），证据 `obj\edshot\mania-dens\`；原子件 DensityCalc（AI 首轮交付+监督修测试用例3），--densitytest 0 |

## 首验与证据链备注
- RPE 官方指南全目录已审（basis：main-interface/edit-window/handle-events/handle-notes/line-management/batch-edit-basics/curve-fill-notes/undo-and-redo/inside-chart/UI；advanced：special-events——**特殊事件**（X/Y 缩放/颜色渐变 RGB/画笔/文本）为线级视觉层，RPE 官方标注部分仅文件可编（incline 实验性），我们以 zoom 事件覆盖缩放语义，其余登记观察待玩家需求驱动）
- 编辑器截图：`obj\edshot\{phigros,arcaea,cytus,adofai,osustd,mania}\*.png`（各 2-3 张）
- 判定方向：与实机编辑器**并排对比图**由 `--edshot` 复拍积累；实机主界面图 `obj\ref\rpe_ref_*.png`（AVIF→PNG 转换链：pillow-avif）

## 下一批修复队列（按对"一致化"影响排序）
1. ✅ ~~Mania 编辑器**密度柱状图**~~（❌→✅，D1 2026-08-22）
2. ✅ ~~Cytus **page/speed 事件可视化编辑**~~（🛠→✅，D2b 2026-08-23：事件时间轴按模式泛化）
3. ✅ ~~ADOFAI **tile 密度/事件面板**~~（🛠→✅，2026-08-23：pathData+SetSpeed Bpm/Multiplier/Beats 全解析，Beats 型补齐；证据 obj\edshot\adofai-density\ + adofai-beats\）
4. ✅ ~~Phigros 事件键帧**拖拽排序/框选移动**~~（❌→✅，D2d 2026-08-23：Ctrl/Shift 多选+橡皮筋框选+组拖+批删+Esc；--edsim 交互仿真 6 步全 ✅+渲染回归 byte-identical）
5. ✅ ~~osu!std **lazer 式物件属性侧栏**~~（🛠→✅，D2e 2026-08-23：时间/X/Y/时长/往返/曲线/谱面 tick 率；[Difficulty] SliderTickRate 误读修正）

## 后续候选（差距表已清零项之外的增强）
- 实机并排对比：各编辑器与对应实机视频逐帧并排（试玩视觉对比持续积累）
- 其余模式（maimai/taiko/catch）编辑器细节对齐（未在铁命令四项中，登记观察）
