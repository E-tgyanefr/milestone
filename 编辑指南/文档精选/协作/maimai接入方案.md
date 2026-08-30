# maimai 接入方案(游戏内容 AI 主导 · 2026-08-21)

## 1. 目标与现状

将 maimai 恢复为可游玩的完整玩法(判定/渲染/输入/谱面解析),与实机体验对齐。
本地生态盘点结论:**官方 .ma2 为二进制加密格式,无本地样本;社区自制谱标准是 simai 文本格式(maidata.txt),参考源码已入库** `research_tmp\MaiLib\MaiLib-master\`(含官方语法规范 `Enums\SimaiFormalSpec.txt` 与完整 C# 解析器 Simaiparser)。

## 2. 各侧就绪度

| 侧 | 组件 | 状态 |
|---|---|---|
| 引擎侧(引擎 AI 领地,已就绪) | RingField(圆心/半径/8 分区/起始角/顺逆时针)、RingNote(StartAngle/EndAngle/SlideProgress/SlideDirection)、TouchInput(多触点) | ✅ 无需改动 |
| 引擎侧 | JudgementProfile.Maimai()(PERFECT 31.25ms/300 分、GREAT 62.5/150、GOOD 125/0、MISS 125) | ✅ |
| 主项目 | ModeSystem maimai 条目 | ✅ 本轮已恢复(Removed 移除) |
| 主项目 | 谱面解析 ParseMaimai(simai 子集) | ⏳ 本轮实施 |
| 主项目 | GamePanel.DrawMaimai(环形轨道+8 分区+中心 touch)+ 输入映射 | ⏳ 本轮实施 |
| 主项目 | 测试谱(自造 simai)+ humantest 验证 | ⏳ 本轮实施 |

## 3. 谱面格式:simai 要点(源自 MaiLib 规范)

- 文件:maidata.txt;多难度行 `&inote_1=`~`&inote_7=`,`&first=`,`&lv_*=`,`&bpm=`,`&inote_*` 之后是逗号分隔的 token 流,每行一个 measure?——**非也**:token 按逗号分隔,`(BPM)` 与 `{measure}` 是控制 token,音符 token 无显式 measure 边界,时间由 token 顺序推进(每逗号前进 1/measure 拍)。
- 音符 token 语法(详见 SimaiFormalSpec.txt):
  - `1`~`8`:外圈 8 键 Tap;`1h[2:1]`:Hold(时长 `[小节:拍]` 或 `[#秒]`);`5b`:Break;`6x`:EX;`1f`:焰火(仅 touch)。
  - Slide:`1-2[1:1]`(直线)、`1>2`/`1<2`/`1^2`/`1v2`(方向弧)、`1p2`/`1q2`/`1pp2`/`1qq2`(曲线)、`1V2`、`1w2`、`1s2`/`1z2`(折线),可连写 `1-2-3-4`(connecting slide),可带 `[wait##slide]` 时长。
  - 中央 touch:`A`~`F`(6 个中央区),可 `Ah[2:1]` 长条 touch。
  - 起始修饰 `$`(star slide 起点)、`!`、`@`(tap 起点);连符 `%`(前后音符 tick 紧邻)。
- 键位→方位:**官方与 MaiLib 一致:编号顺时针,8=正上 0°、1=右上 45°、2=右、3=右下、4=下、5=左下、6=左、7=左上**(常见误解"1 在左上"是错的)。中央 touch:A~E 五组 34 区(A1~A8 环带/B1~B8 中环/C1~C2 中心/D1~D8 对角/E1~E8 内环),`C`/`C1`/`C2` 等价;`F` 是 MaiLib 扩展。主项目用粗列 9~14 表示 A~F。
- 时长语义(已按官方修正):`[n:m]`=m 个 n 分音符(tick=(384/n)×m),非"小节:拍";`[#s]`=绝对秒;`[w##l]`=等待 w 秒+滑行 l 秒;`[BPM#n:m]`/`[BPM#s]`=指定 BPM。slide 默认等待当前 BPM 一拍(四分音符),链式后续段 wait=0。换算:`tick→ms = tick×60000/bpm/96`。
- 计时:BPM token `(bpm)` 改当前 BPM;measure token `{n}` 改小节拍数(quaver);每 token 前进 1/quaver 小节。tick 精度 384/小节。

## 4. 主项目映射(Note 模型)

- `Col` = 1~8 外圈分区;中央 touch 用 `Col` = 9~14(9=A…14=F),或独立 `Type="touch"` + `X/Y` 中心坐标。
- `Type`:"tap"|"hold"|"slide"|"break"(break 兼 tap/hold/slide)|"touch"。
- `Kind`:1=EX(x),2=焰火(f)。
- hold/slide:`Time`=头、`End`=尾;slide 中间点存 `Curve`(角度序列),`EndCol` 存终点键。
- 判定:沿用引擎 `_eng.Judge`,滑动进度用 RingNote 语义自行实现(主项目 Note 是轻量模型,渲染时按角度差算进度)。

## 5. 渲染设计 DrawMaimai(本轮骨架)

- 环形轨道:以 play 区域中心为圆心,大半径外圈轨道(8 分区刻度+按钮圈),中央 touch 区(6 点小圆)。
- 音符表现:Tap/hold/break 从圆心向外扩散到外圈(实机是向内?实机:音符从中心向外飞向外圈按钮,判定时刻到达外圈)。采用**从圆心到外圈的扩散动画**,进度 = (now-接近窗)/时长。
- Slide:起始于某外圈键,沿环/弧线移动到终点,拖动跟随。
- 输入:鼠标位置→极坐标(距圆心/角度)→分区;外圈命中=1~8,中央= A~F;左键按下判定,按住维持 hold/slide,松开结束。键盘可加 1~8 数字键映射(可选)。
- HUD/连击/ACC 复用现有通道。

## 6. 实施顺序与验证

1. ✅ ModeSystem 恢复(已做)。
2. ✅ ParseMaimai(ChartParserExtra 追加;参考 MaiLib Simaiparser;支持 tap/hold/slide 直线+基本弧/break/ex/touch/中央 touch;纯数字串逐键拆分;官方时长语义 [n:m]=m 个 n 分音符、slide 默认等待一拍、[#秒]/[w##l]/[BPM#n:m])。
3. ✅ GamePanel:渲染 switch 加 `case GameMode.Maimai: DrawMaimai(...)`;IsTouchMode 加 Maimai(鼠标 TapAt 通道自动生效);NoteScreenPos/HitPointFor 加 maimai 按钮位置;ResetState 跳过 maimai 的 Col 钳制(中央 touch 用 9~14)。
4. ✅ JudgeSettings.ApplyForChart 加 maimai 判定窗口(PERFECT ±31.25 / GREAT ±62.5 / GOOD ±125 / MISS 125)。
5. ✅ 自造 simai 测试谱 `Chart\测试格式\maimai_test.txt`(8 键 tap + 2 hold + 2 break + 2 slide + 6 touch + 1 touch hold + 8 tap,共 29 音符),humantest 实测 **ACC 89.66% = 26/29 与 8% 漏键模型精确吻合**,403 FPS 无崩溃,渲染截图确认环形轨道+按钮+音符扩散正常(见 屏幕采集\maimai_验证_环形渲染.png)。
6. 留言消息板汇报;待引擎 AI 侧补充(如需 RingField 直接驱动渲染可后续对接)。

## 7. 风险与后续

- simai 全部 slide 曲线类型(pp/qq/V/w/s/z)首版可先降级为直线/基本弧,后续按需补。
- 实机方位/视觉细节(按钮大小、touch 颜色、爆字)待实机截图校准(策划 C2)。
- 官方 .ma2 二进制解析暂不接(无样本+加密),如后续获得 MajdataEdit 生态资料再评估。

## 8. 参考资源(子代理精读 MaiLib 报告,存档于会话)

- **键位自检**:MaiLib `Flip(Clockwise90)`:1→3,2→4,3→5,4→6,5→7,6→8,7→1,8→2(顺时针 90°,8 项全吻合)。
- **slide 方向规则**:起点键 ∈{1,2,7,8}(0-based {0,1,6,7})时 `<`→SCL(逆键号方向)、`>`→SCR,否则互换;`^` 取两方向较短者(两方向都 ≥4 键则抛错);`V` 型拐点必须是起点 ±2。
- **触摸 34 区近似半径**(相对按钮圆半径 R):A_k 同角 0.85R、B_k 同角 0.60R、E_k 同角 0.40R、D_k 角+45° 0.75R、C 中心 0。
- **时长公式核对用例**(单元测试建议):`(120){4}` → 每逗号 0.5s;`5h[2:1]`@120 → 1s;`1-4[8:3]`@120 → 等待 0.5s + 滑行 1.5s。
- **风险清单**:R1 % 连符 tick 推进、R2 hold 时长单位混用、R3 slide 等待一拍、R4 BPM 变化分段积分、R5 {n} 变拍位置语义、**R6 同位多音符拆分(✅ 已修复 2026-08-21:扫描式切分——"123"→3 tap、"1h[2:1]2h[2:1]"→2 hold 共享时长、"1b2b"→2 break)**、R7 w 扇形 3 终点、R8 [BPM#秒] 等待 BPM 分歧、R9 链式滑条时长均分、R10 键位方向、R11 触摸编号(C/C1/C2 等价)、R12 滑条缺时长抛错、R13 解析错误定位、R14 E/空 token、R15 时长公式。详见子代理报告(会话内)。
