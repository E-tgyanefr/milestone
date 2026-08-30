# Milestone 各玩法核心玩法与视觉参考

> 依据公开资料整理的实机参考摘要，用于游玩界面与谱面编辑器的还原设计。
> 来源：[萌娘百科](https://zh.moegirl.org.cn)、[osu! wiki](https://osu.ppy.sh/wiki/zh/Gameplay/Judgement/osu!mania)、[Rotaeno 中文维基](https://wiki.rotaeno.cn)、[鸽屋咕咕咕 SDVX 介绍](http://www.gugu.fun/sdvx/)、[MaiLib](https://github.com/Neskol/MaiLib) 等。
> 还原对照细则见 `research_tmp/gameplay_visual_spec.md`（20 模式实机视觉特征）与 `research_tmp/chart_editors_spec.md`（同类谱面编辑器调研）。

## 引擎运行架构建模（Unity 式）

本章还原设计在引擎侧由新增的"Unity 式运行时"承载——不同玩法、场景、UI、转场都用同一套对象/组件/场景树组合，宿主只负责渲染：

- **场景对象/组件**：`GameObject` + `Component` + `Scene`/`SceneManager`/`GameLoop`；组件生命周期 `Awake→OnEnable→Start→Update→LateUpdate→OnDisable→OnDestroy`，父先于子遍历，失活/销毁精确触发。
- **Ruleset 组合（可搭建所有游玩法）**：`abstract Ruleset` + `ChartContext` + `LaneRuleset`/`LineRuleset`/`RingRuleset`/`PathRuleset`，用既有 `LaneField`/`LineField`/`RingField`/`PathField`/`TimeDepthMapper`/`InputMapper`/`JudgementProfile`/`JudgementTracker`/`ScoreBoard` 组合出 固定下落 / 自由线场 / 环形触摸 / 路径轨道 四大玩法族，判定闭环无头可自检。
- **引擎驱动 UI（重做 UI）**：`IUiDraw`（Rect/RoundedRect/Text/Ellipse/Line/Image/PushClip/PopClip/MeasureText，颜色 `RgbaColor`）+ `UiPanel`/`UiButton`/`UiLabel`/`UiCard`/`UiStackLayout`（`Component` 子类）+ `UiTheme` 新主题。
- **场景转场动画**：`SceneTransition` 五风格 `Fade / Slide / Wipe / CircleReveal / Beam`（`Progress/IsComplete/Completed/OutAlpha/InAlpha/OffsetX`），引擎只算时序/缓动，宿主 `SceneCompositor` 读参数画。

> 设计契约：`docs\协作\engine-unity-arch.md`（含精确签名与自检断言）。

## 还原要点（v12：删减至 9 玩法 + 单一谱面多模式回归）
- **游玩区域框定（v12）**：游戏内容渲染在框定的游玩区域内（淡蓝细框线标示）——区域 = 全窗口扣除右侧面板（Mania/IIDX）与最小边距，尽可能大；轨道/判定线/场横贯整个区域（移除 1200px 轨道硬顶，`界面缩放` 默认 1.0 = 铺满区域，0.25~1.6 可调）；Cytus 场横贯全区域（不再缩到 70% 高）、osu 4:3 场按区域最大化；判定几何（扫描线门控/命中点/点击位置）与渲染同口径
- **移除玩法（v25 更新）**：taiko / catch / Deemo / Muse Dash / Rotaeno / Dynamix / Lanota / Tone Sphere 与先前已移除的 WACCA / SUS / SDVX / Pump 共 13 种玩法**彻底删除**——GamePanel 绘制/判定/输入分支、编辑器全部编辑分支、格式解析器、嗅探分支、UI 过滤器全部移除；`ModeSystem` 注册表条目保留 `Removed=true` 作为开发者重新接入的插件点。可玩玩法 10 种：Mania / maimai / Phigros / Arcaea / Cytus / osu!standard / ADOFAI / IIDX / Routlock / **回环作曲 LoopComposer**（创新玩法：谱面=玩家演奏）
- **单一谱面多模式回归（v12）**：`.mil` 重新支持 `parts` 数组（每部件独立 mode/keys/notes/events，共享 BPM/音频）；游玩端 `PickPart` 弹窗选部件、游戏中 `M` 键切换部件（整局重开）；编辑器「部件」下拉 + 加/删部件按钮，保存时写回 parts 数组；已移除玩法部件自动跳过
- 默认 **2D 直轨** 视角（与原音游一致），`T` 开斜轨、`V` 开 3D
- **音符样式复刻**：Mania 白色圆角竖条+判定线下彩色键位（osu!mania 经典）· Phigros 白 TAP/红 FLICK/半透明 DRAG 条 · Arcaea 横向胶囊音符（地面白/天空冰蓝）· Cytus 彩环+白核+内环收缩+DRAG 点串 · taiko don 白内环鼓面 · IIDX 蓝转盘+Charge 绿长条 · catch 接盘表情 · osu!standard 白粉接近圈
- **键盘游玩修复（人类实机游玩发现）**：① 窗口禁用 IME——中文输入法会拦截字母键使按键全部变成 ProcessKey 而失效；② IsInputKey 全键直通——音符键不再走对话框键/字符路径；③ 空格暂停修复——空格作为音符键的模式（5K/7K/8K 及 adofai）按 _keyCol 实际映射判断，不再误触发暂停
- **人类游玩自检**：`Milestone.exe --humantest "谱面路径"` 进程内模拟真人游玩（±15ms 偏差 / 8% 漏键 / hold 按住松开，走真实按键判定路径）；`tools\human-run.ps1` 一键运行并回读结算。9 种玩法验证通过（Mania S 95% / Phigros B 88.89%（双线 A 92.86%）/ Arcaea A 94.74% / Cytus A 93.75% / IIDX B 89.47% / 多模式示例 B 83.33% 等；自动游玩全模式 SSS 100%）
- **Arcaea**：左侧回忆收集率竖条 + 天空线随 moveY 事件浮动 + 天空音符冰蓝
- **Phigros**：白色判定线 + 线上落点刻度（无全场轨道线）、TAP 白色竖条、FLICK 红色箭头条
- **ADOFAI**：红蓝双轨道球（火红/冰蓝）对称绕路径旋转
- **判定术语按原作**：Arcaea=PURE+/PURE/FAR · Cytus=C.PERFECT/PERFECT/GOOD · IIDX=PGREAT/GREAT/GOOD/BAD · taiko=良/可 · osu!std=300/100/50 · Phigros=Perfect/Good/Bad · ADOFAI=PURE/PERFECT/COUNTED
- **Cytus II**：右上角 TP（100/70/30 权重口径）实时显示
- app.manifest 声明 PerMonitorV2 高 DPI 感知（高分辨率屏下清晰不裁切）
- **Phigros 无轨编辑**：编辑器为无轨自由场——白色判定线随 moveX/moveY/rotate 事件移动旋转，音符沿判定线自由放置（Col=-1 按 X 定位），游玩端按按键列对应区间命中；判定线本体随 rotate 事件旋转
- **编辑器网格在轨道上**：细分/小节网格只画在轨道区域内，不再贯穿侧栏
- **选歌右侧卡片一次性显示完毕**：内容超高时自动压缩曲绘高度，不再出滚动条；编辑器场地高度钳制到可见区
- 编辑器：全部 9 种可用模式可编辑；编辑布局由游玩界面决定（场/双线/判定线），无轨模式一律保持无轨场编辑
- **无轨玩法位置判定（不做定轨简化，v8）**：触屏类模式（Phigros/Cytus/Arcaea）以**鼠标点击音符所在位置**判定，与原版触屏一致——点判定线上/下落中/圆盘上的音符即命中；长条=按住鼠标跟随、松开或到尾完成；键盘列键仅作辅助（`NoteScreenPos` 与各 DrawXxx 完全同几何，`TapAt` 窗口内按距离取最近未判定音符）。人类模拟对触屏模式改用鼠标点击路径验证：Arcaea A94.7% / Cytus A93.8% / Phigros B88.9%
- **Cytus 扫描线 = 时钟（v9 玩法还原）**：音符判定时刻由其 Y 位置经扫描线页折算（偶数页向下、奇数页向上，speed 事件=页拍数）；只有扫描线到达音符附近（页速×判定窗+半径）才可命中——**提前击打无效**（实机玩法，不再等价于时间窗口下落）；人类模拟 A 93.75% 验证
- **Phigros DRAG 触碰判定线即可（v9）**：DRAG 音符任意键可命中（键盘）、点击判定线任意位置可命中（鼠标，点到线段距离判定），无需精确位置——实机玩法
- **Arcaea 3D 渲染（v11）**：修复 Cam3D 焦距 bug（焦距改为像素当量 2400，透视不再塌缩——原公式 f≈1.73 使 z=460 时 persp→0.002 全场缩成一点）；Arcaea 改用真实 3D 相机：场地为俯仰 18° 的倾斜平面（上远下近），音符/arc/判定线全部经透视投影（命中点/鼠标点击与渲染同映射，A 94.74% 验证）；mania 3D 同步修复
- **Phigros 判定线颜色一致 + 无上限（v11）**：所有判定线纯白（同实机，取消彩虹配色）；线数量上限取消（性能内 64）；编辑器线列表同步
- **右侧窗口规则（v11）**：仅 Mania / IIDX 保留右侧窗口（实机 DJ 界面），其余玩法实际游玩面积铺满程序窗口
- **引擎化（v11）**：① 游戏循环改为 Application.Idle 驱动——帧率不再受 WinForms 定时器 15.6ms 粒度限制（实测 299~300 FPS / 3.3ms，此前锁 64 FPS）；② 渲染后端探测（DXGI 枚举 GPU）——FPS HUD 与日志显示 "Direct2D1 · GPU: NVIDIA GeForce RTX 5070 Ti"；③ 多进程渲染管线为后续演进项（当前 D2D1 由 GPU 硬件加速）
- **图像自检工具（v11）**：`tools\img-probe.ps1`——截图区域/线段/主色检测 + ASCII 色图（开发自检无需人眼）

## 有轨（下落/列式）玩法
| 玩法 | 核心玩法 | 关键视觉 |
|---|---|---|
| osu!mania / Malody | 轨道下落，按键判定，斜轨透视 | 轨道竖线、轨道色音符、判定线闪光 |
| IIDX | 7 白键 + 1 转盘 | 红色转盘轨与 7 白键轨，判定线细闪 |
| osu!taiko | 太鼓红蓝鼓点 | 左侧鼓面、红 don 蓝 kat 横滚 |
| osu!catch | 左右移动接盘接水果 | 底部角色接盘、水果下落 |
| ADOFAI | 单键踩节拍，路径旋转（Twirl 自动） | 旋转折线路径、菱形 tile、霓虹描边 |

## 无轨（触控/位置）玩法
| 玩法 | 核心玩法 | 关键视觉 |
|---|---|---|
| Cytus | 扫描线往返，点击/长按/拖动圆点 | 上下扫描线带光点、音符圆点脉冲 |
| osu!standard | 鼠标瞄准：圆圈点击、滑条跟随、转盘旋转 | 接近圈收缩、滑条曲线+跟随球、转盘螺旋 |
| Phigros | 动态判定线：TAP/FLICK/DRAG 沿线落下 | 白色判定线（多线同色）、线上刻度 |
| Arcaea | 天地双轨：地面/天空音符 + Arc 弧 | 俯仰 3D 场地、胶囊音符、回忆率竖条 |

## 已实现的判定标准（按公开资料修正）
| 玩法 | 判定窗口 | 权重 | 来源 |
|---|---|---|---|
| osu!mania | lazer DifficultyRange 公式（按文件 OD） | 1/1/2-3/1-3/1-6 | osu-master 源码 |
| osu!taiko | 良=50-3OD / 可=120-8OD | 1 / 0.5 | osu!stable |
| osu!standard | 300=79.5-6OD / 100=139.5-8OD / 50=199.5-10OD | 1/1-3/1-6 | osu!stable / lazer OsuHitWindows |
| Malody 段位 | C 判 36/76/110ms | 100/80/60 | Malody 4.3.7 |
| Phigros | Perfect 80 / Good 160 / Bad 180ms；Drag 只计连击不计判定 | 1/0.65/0 | [Phigros判定2.0](https://www.bilibili.com/opus/587050569698837785) |
| Arcaea | PURE+ 25 / PURE 50 / FAR 100ms | 1/1/0.5 | [Arcaea中文维基](https://arcwiki.mcd.blue) |
| Cytus | C.PERFECT 70 / PERFECT 140 / GOOD 200ms | 1/0.7/0.3（TP 口径） | [Cytus II 指南](https://game.zol.com.cn/1159/11593436.html) |
| IIDX | PGREAT 16.67 / GREAT 33.33 / GOOD 116.67 / BAD 250（BAD 断连） | EX-SCORE 口径 1/0.5/0/0 | [iidx.org](https://iidx.org/compendium/gauges_and_timing) |
| ADOFAI | 角度判定 PURE 30° / PERFECT 45° / COUNTED 60°（随 BPM 换算，有效窗口=min(角度窗口, 20/30/65ms 上限)） | 1/0.75/0.4 | [B站判定系统文档](https://www.bilibili.com/opus/1187804902628261926) |

段位：Malody 纯 ACC（Regular/Dan 95、Extra 96）；osu 按 HP 存活（lazer ManiaHealthProcessor 模型）。
