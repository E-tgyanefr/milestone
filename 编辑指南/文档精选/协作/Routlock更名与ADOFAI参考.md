# Routlock 更名与真实 ADOFAI 玩法参考(游戏内容 AI · 2026-08-21)

## 1. 本轮已完成:现有模式更名 Routlock

- 用户确认:现有 ADOFAI 玩法保留,更名为 **Routlock**(非真实 ADOFAI)。
- 改动:`ModeSystem.cs` Display="ADOFAI"→"Routlock";`ChartParserExtra.ParseAdofai` ModeName="冰与火之舞"→"Routlock"、异常信息同步;编辑器列名 "单轨"→"Routlock 单轨"。Id 保留 "adofai"、文件格式(.adofai JSON)不变,解析/渲染/编辑器零破坏。构建 0 错误。
- 真实 ADOFAI 将作为新玩法编写(等策划调研指导)。

## 2. 真实 ADOFAI 玩法速览(联网调研初步;权威细节待策划文档)

> 来源:adofai.fandom.com Game Mechanics / Tile Angles、B站"ADOFAI 判定系统与精准度系统技术文档"、萌娘百科。

### 2.1 核心玩法(与 Routlock 的本质差异)

- **双球绕行**:冰球与火球各自沿一条"轨道"移动,轨道由**菱形(tile)链**组成;每拍移动到下一个 tile。
- **输入**:按拍击打(单键),球沿当前 tile 方向前进;玩家输入决定**转向**——在 tile 拐角处按,轨道转弯。
- **判定**:角度判定——每次输入时,球与目标 tile 中心的连线与正确方向的角度差决定判定档(Pure/Perfect/Counted/…);另有时间窗。
- **失败**:偏离轨道(角度超差)= 掉轨,重开本段(关卡重试)。
- **画面**:深色星空背景,发光轨道菱形链,双球沿轨道绕行,轨道会整体旋转(tile 角度累计)。

### 2.2 判定窗口(社区/技术文档口径,待策划核实)

- 角度边界 + 时间上限结合:判定 = min(角度换算窗口, 时间上限)。
- 档位:PURE(最佳)/ PERFECT / COUNTED / 失败;角度约 30°/45°/60°,时间上限约 20/30/65ms(随 BPM 换算)——与本项目现 JudgeSettings.Adofai 分支一致(该分支当初按此口径实现,但**玩法结构不对**)。
- 精准度(acc):按判定档计分。

### 2.3 谱面格式(.adofai JSON,本项目已能解析)

- `settings`(bpm/offset/title)、`angleData`(每 tile 转角,度)、`pathData`(每段拍长倍率)、`actions`(SetSpeed/Twirl/Hold 等)。
- 本项目 ParseAdofai 已解析 angleData/pathData/actions → 现有 Routlock 渲染用角度累计旋转。

### 2.4 Routlock 与真实 ADOFAI 的差异(重写要点)

| 维度 | Routlock(现有) | 真实 ADOFAI |
|---|---|---|
| 球 | 单轨单点/菱形轮盘 | 双球各自轨道 |
| 移动 | tile 按角度累计旋转,轮盘式 | 球沿轨道前进,轨道随角度转向 |
| 输入 | 每拍按键,tile 正点到底 | 每拍按键+**转向输入**(拐角判定) |
| 判定 | 时间窗口(20/30/65ms 上限) | 角度差+时间窗结合 |
| 失败 | 无掉轨(仅判级) | 角度超差=掉轨重开 |
| 画面 | 中央轮盘+菱形 | 全屏轨道链+双球 |

## 3. 待策划输出(已留言请求)

1. 真实 ADOFAI 玩法规格文档(判定公式/输入/画面/掉轨规则/谱面结构与 Routlock 差异);
2. 编辑器问题清单(除 mania 外各玩法)与修复优先级;
3. 指导真实 ADOFAI 模式编写(新 GameMode 或替换)。

## 4. 主代理自行实现进展(策划未响应,2026-08-21)

- **真实 ADOFAI 新独立模式已建**:`GameMode.AdofaiReal = 20`(Adofai=13 保留为 Routlock);ModeSystem 注册 Id="adofai2" Display="ADOFAI" KeyHint="空格/D 单键(每拍输入)"。
- **解析**:`ParseAdofaiReal` 复用 ParseAdofai 核心(同一 .adofai 格式),Mode 标为 AdofaiReal;CLI `--adofai2` 开关(LoadChartForPlay)使 .adofai 谱面走真实模式,Routlock 保持默认。
- **判定**:JudgeSettings AdofaiReal 共用 ADOFAI 角度口径(PURE 30°/PERFECT 45°/COUNTED 60° 随 BPM 换算,时间上限 20/30/65ms,有效窗=min)。
- **渲染**:`DrawAdofaiReal`——轨道式视图(区别于 Routlock 轮盘):路径从中心向右延伸、每 tile 一段、方向=Kind 累计角度(带透视压缩)、霓虹路径线+菱形 tile+当前 tile 脉冲环、火球沿上一→当前 tile 间移动(时间进度)、冰球下一拍预览。
- **双轨并行升级**(2026-08-21,策划规格 0b):火球/冰球**各占一条轨道**(垂直偏移 ±trackOff),双轨路径各自着色(火轨暖色/冰轨冷色),双球同进度各自沿轨移动——对齐实机"两球各自在轨道上"语义;tile 双轨菱形(火轨偏暖/冰轨偏冷)。
- **双球机制修正**(2026-08-21,策划规格 0b 再次修正,依据 Stable 3.3.1 源码 SwitchChosen):实机 = **一球固定于当前 tile、另一球绕其旋转,每拍按键触发跳转+两球角色互换**——非"双轨并行同跳"。已重写 DrawAdofaiReal:单路径(弃双轨),固定球(火)在当前 tile + 绕旋球(冰)绕其旋转(角度=进度×360+路径方向),每拍绕旋球落向下一 tile 视觉互换。
- **输入**:单键(空格/D,ResetState 与 Routlock 共享)。
- **掉轨重开**(2026-08-21 追加):MISS 时 `_adofaiDerails++` + `SeekTo(失败 tile 前 3 拍)` 重开本段 + Toast "掉轨 ×N";`SeekTo` 支持有/无音频(Seek/时间轴偏移)。
- **策划规格修正落实**(2026-08-21,策划《ADOFAI玩法规格》深度调研):
  1. **判定边界语义反转**:实机 = max(角度换算窗口, 时间下限),原 min() 相反——已修正 JudgeSettings(PURE 30°+20ms 下限 / PERFECT 45°+30 / COUNTED 60°+65);
  2. **掉轨回检查点**:实机回本关开头或最近 Checkpoint(非"前几拍")——解析器补 Checkpoint 事件(→Chart.Events Type="checkpoint"),掉轨时找 ≤失败时刻的最近检查点回放,无检查点回第一音符前 2 拍;
  3. 待办:方形砖块(非菱形)、双球绕旋互换、结算口径(完成度/精准度)、SetSpeed Multiplier/angleOffset、MoveTrack/PositionTrack/SetHitsound 解析。
- **方形砖块落实**(2026-08-21):DrawAdofaiReal 与编辑器 DrawAdofaiTopdown 的 tile 从菱形改为**发光方形砖块**(沿路径方向摆放,白亮描边,选中加粗)——对齐实机"发光方形砖块"规格;编辑器俯视同步改单路径。
- **结算口径落实**(2026-08-21,策划规格 §4c):AdofaiReal 结算显示 ADOFAI 口径——完成度 = 已判定砖块占比%、精准度 = 100% + Perfect×0.01% − 非完美×0.05%(近似),替代通用 ACC/连击行。
- **SetSpeed Multiplier/angleOffset 落实**(2026-08-21):解析器 SetSpeed 支持 Bpm 绝对值与 Multiplier 倍率(作用于初始 BPM 连续乘);angleOffset 叠加到该 tile 转角(Kind)。无头验证:Multiplier 0.5@floor2→tile2 拍长 500→1000ms、angleOffset 90→Kind=90 ✅。
- **Twirl 事件写出 + 渲染提示**(2026-08-21):Twirl 事件写入 Chart.Events(type="twirl");DrawAdofaiReal 路径线在 Twirl 区间改粉紫虚线(策划规格:Twirl 反转旋转方向提示)。
- **MoveTrack 渲染消费**(2026-08-21):DrawAdofaiReal 按最近 movetrack/positiontrack 事件对整条路径应用位移(Value=positionOffset)/旋转(EndValue=rotation)/缩放(Line=scale)——轨道变换实际作用于画面。
- **编辑器适配**(2026-08-21 追加):AdofaiReal 设为 Tracked=true 走单轨时间轴编辑;列头/事件类型(bpm/mode/twirl)/角度框/Kind 转角映射/DrawAdofaiPreview 全部并入 AdofaiReal。
- **构建**:0 错误。humantest 验证待允许弹窗(预期:每拍按键走 OsuTap 等价通道,ACC 与角度窗口吻合)。
