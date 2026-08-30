# t13 策划复查：t5 编辑器/功能实现 vs editor-trilab.md 设计合规性（editor-review）

> 复查人：designer-pro（策划）· 任务 t13 · 方法：静态代码核对（对照 editor-trilab.md §1-§4 / D1-D8 / 验收要点；未构建——构建与运行态复验由 captain/t7 负责）
> 复查对象：t5 实现（coder-max，7 文件，全部 源码/ 侧；报告=其他/docs/协作/t5-实现报告.md）+ t12 本地 AI 禁用联动
> 结论速览：**总体合规（约 90% 规格项落地）**。三栏/折叠/自适应/持久化、自动游玩/AI 游玩主链路、返回路径、t1 P0 宿主修复全部按设计落地；复查轮发现 **P0=0 / P1=3 / P2=5**——其中 **P1-1/P1-2 已由 t14 修复（§2.1 静态复核通过）**；**P1-3 与 P2-2/P2-3 经 captain 拍板放行并已恢复（t14.1，最终静态复核通过，见 §2.2）**；其余 P2 四条（P2-1/P2-4/P2-5/P2-6）留档；创新玩法 A/B 未列入本批（标注待后续，B 依赖 t6 EN-1/EN-2）。

---

## 1. 逐项核对表（✅ 合规 / ⚠️ 偏差 / ⏳ 待后续）

### 1.1 三栏 200/780/300 + 折叠持久化 + 自适应（editor-trilab §1，D1/D5/D6）

| 规格项 | 实现证据（源码\Charting\ChartEditorPanel.cs） | 结论 |
|---|---|---|
| 左 200 / 中 780 / 右 300（1280×800 基线） | LeftWidthOf=clamp(round(W×200/1280),160,240)（:2461）、RightWidthOf=clamp(round(W×300/1280),240,360)（:2463）、_leftPanel Dock=Left、_canvasHost Dock=Fill、_animPanel Dock=Right（:2416-2446） | ✅ 公式与 D6 逐字一致 |
| 左栏内容（曲目/谱面+分区+音符模板+最近谱面） | BuildLeftPanel（:2416-2439）+ gRecent「最近谱面」组（:1572-1573）、AddRecentChart 去重置顶限 8（:2566-2572）、打开/保存钩子（:2811/:3072）、失效文件剔除（:2614） | ✅ 含新增最近谱面列表（近 8 条，AppConfig 持久化） |
| 右栏 Tab 化 | BuildRightPanelShell（:2440-2459）：7 Tab=属性/事件/判定线/音符/Arcaea/预览/AI | ✅ 规格 §1.1 表格列 6 Tab、迁移说明=现有 4 Tab+新增 3=7；按「不删功能」落定为 7 Tab（Arcaea 保留），歧义以保留为准，合规 |
| 顶部工具栏保留 | 组⑥视图+保存/试玩/主菜单/「≡ 面板」+状态标签留工具栏（:1599-1605）；旧 gEvt/gAi/gAudio 零残留（grep 仅命中 DragEvt 等无关） | ✅ 组①-⑤迁栏不删功能（事件→事件 Tab 顶、AI 三按钮→AI Tab、运输+自动/AI 游玩→预览 Tab） |
| 折叠四档 + 8px 把手 | _leftMode 0/1/2（开/48 轨/收）、右 0/2、把手 Width=8（_foldLeft/_foldRight :2431/:2453）、ToggleLeftPanel 0→1→2→0（:2504）、ToggleFocusPanels F9 双收+恢复（:2516）、双收态「≡ 面板」RestorePanelsFromFocus（:2523） | ✅ 48px 图标轨含「◀」+竖排提示（:2423-2430） |
| 快捷键 | F5/F6/F8/F9/F10 全部接线（:8275-8279） | ✅ |
| 自适应最小画布 640 自动折叠 | ApplyPanelModes：W−lw−rw<640 → 先左收轨再右收（:2471-2477）+ OnResize（:2493） | ✅ |
| 折叠档持久化 | AppConfig.PanelLeftMode/PanelRightMode + 变更才落盘（:2546-2563）+ AppConfig.Capture 防覆盖（t5 报告） | ✅ 重启保持（待运行态验证） |

### 1.2 自动游玩（§2，D2/D7/D8）

| 规格项 | 实现证据 | 结论 |
|---|---|---|
| 复用 StartAutoplay 同窗承载 | PlayAutoplay（:2642-2653）→ TestAutoplay 事件（:217）→ MainForm 接线 StartAutoplay + HostContent（MainForm.cs:1161-1167） | ✅ 所见即所玩，零新渲染器 |
| 入口三处 | 预览 Tab「🅰 自动游玩」蓝钮（:753）、F5（:8275）、工具栏保留「试玩」旁——注：工具栏未单独再放 🅰 按钮，但 F5+预览 Tab 双入口已满足 | ✅（第三入口合并进预览 Tab） |
| 无音符/无音频拦截 | 「谱面还没有音符」/「请先加载音频（🎵 加载音频…）」（:2644-2645） | ✅ 文案与 t1 验证过的一致 |
| 状态显示 | 编辑器 _status.Text「🅰 自动游玩预览中… ESC 返回编辑器」（:2652）；GamePanel AUTO 徽章/进度条/暂停覆盖层全复用 | ✅ |
| 与播放条关系 | PauseForPreview 冻结编辑器播放（:2680-2690，D8 防双声源）；**t14 已修复**：进入带 startMs=_time（:2649/:2664），退出 SyncTimeFromPreview 回写（:2682）；GamePanel 增 StartAutoplayAt/SeekToPublic/CurrentMs；MainForm 三条返回路径均同步 | ✅ t14（见 §2.1） |
| 返回路径 | _testPlayReturn 三分支：ExitToMenu（MainForm:131-137）/ExitToLibrary（:152-160）/SongEnded IsAiDemo（:170-179）均 Unhost 游戏→Host 编辑器 | ✅ ESC 与自然结束均回编辑器 |
| 大谱 toast >2000 | **t14.1 已恢复（captain 放行）**：MainForm 三条预览接线（自动游玩 :1175 / AI 游玩 :1203 / AI 演示 :1211）开局后 ShowToast「⚠ 大谱面演示可能降帧，建议关闭 3D（V）」（GamePanel.ShowToast 转 public :1098，开局后提示防 ResetState 清除） | ✅ t14.1（见 §2.2） |
| 变速预览 1.0x | 预览承载=整体替换编辑器（编辑器不可见），EN-3 未定稿前无影响；t3 v1.2 口径一致 | ✅（口径闭环，见 P2-4） |

### 1.3 AI 游玩三档（§3，D3 + 用户决策不调用本地 AI）

| 规格项 | 实现证据 | 结论 |
|---|---|---|
| 三档接线 | PlayAi（:2654-2665）→ TestAiPlay 事件（:219）→ MainForm：companions==0 → StartAiDemo（纯演示）；1~3 → LoadAndPlay+AddCompanionAi×N（MainForm:1184-1197） | ✅ 演示/陪玩两档+对练（陪玩面板） |
| 「vs 我」差值行（档位③） | **t14 已修复**：陪玩面板首行「vs 我 ±N 分」（我−AI 差值，领先绿 127,208,160 / 落后红 255,122,122），面板高 +22 | ✅ t14（见 §2.1） |
| AI 等级下拉 + 陪玩数 0~3 | _aiLevelBox（AiEngine.Levels 全量，:757-767）+ _aiCompanionBox 0~3（:770-772） | ✅ **t14.1 已恢复（captain 放行）**：默认等级=1st Dan（defaultIdx 遍历 items 取「1st Dan」，:768-771，与 §3.2 口径一致） |
| 规则驱动不接本地 AI | 文案「AI 游玩 = 引擎规则驱动（AiEngine），不调用本地 AI 服务（t12）」（:774-777）；t12 已删全部 HTTP/Ollama 代码；AI Tab 头「本地 AI 未启用，规则检查仍可用」（:784） | ✅ 用户决策完全遵守 |
| 状态显示 | _status.Text「🤖 AI 游玩（等级 · 陪玩 N）预览中… ESC 返回」（:2663）；DemoAi/陪玩面板徽章全复用 | ✅ |
| AI 计划后台生成（>1500 音符/陪玩≥2） | 未实现（MakePlayer 同步） | ⚠️ **P2-5**（性能建议项） |
| 与自动游玩互斥 | 走 StartAiDemo/LoadAndPlay 路径，Autoplay=false（GamePanel 既有语义） | ✅ |

### 1.4 创新玩法（§4，D4）

| 提案 | 状态 | 结论 |
|---|---|---|
| A 回环变奏（FlattenToChart+演绎入口） | LoopComposer.cs 无 FlattenToChart（grep 0 命中） | ⏳ **待后续**（未列入本批；零引擎依赖，建议下批 ~130 行游戏层实现） |
| B 模式接力（relay） | 源码 grep relay/接力 0 命中 | ⏳ **待后续·引擎前提已解锁**（t6 已落地 EN-1 SetProfile 热换 + EN-2 WindowMultiplier，引擎侧已验证（0/0 + EngineChecks EXIT=0，仅剩 captain 官方打包）；**captain 拍板：排期下一轮（当前需求交付+终验完成后）建任务给 coder-max**，按 §4.2 由 t5 段调度器实施） |
| C 判定演出 | — | ⏳ 远期（依赖 EN-4，t3 v1.2 P1 批） |

### 1.5 附带给修项（t5 自认范围外，静态确认）

| 项 | 证据 | 结论 |
|---|---|---|
| P0-4 启动对齐屏幕 | StartupClientSize()=主屏工作区（≥1024×640 才采用，否则 1280×800 兜底）（EngineMainShell.cs:1192-1201） | ✅ 与 t3 ViewportPolicy 方案兼容（t6 落地后无缝替换） |
| P0-1 设置页签 | HostTabBar 宿主兜底：LastPointer 虚拟坐标→InvokeClick 均分算 idx→Select（:1501-1525、:695-701） | ✅ 兜底可用；引擎根治交 t6（职责边界=EN-8 一致） |
| P0-2 ▲▼ 命中错位 | ArrowCard 按父框实际宽与 DrawSelf 同公式（:853-861、:1525 起） | ✅ 修 590px 错位 |
| P0-3 曲库行重叠 | SecondaryPages 行距重排（t5 报告 §曲库/我的数据/段位坐标） | ✅（运行态待 t7 截图复核） |

---

## 2. 偏差清单（P0=0；P1×3 全部闭环：P1-1/P1-2=t14、P1-3/P2-2/P2-3=t14.1 放行已恢复；其余 P2 四条留档）

| # | 级别 | 项 | 规格依据 | 现状 | 修复建议（供 t5 补丁轮） |
|---|---|---|---|---|---|
| P1-1 | **P1**（✅ t14 已修复，见 §2.1） | 预览播放头同步缺失 | §2.4 表：进入=从当前播放头 Seek 开始；退出=_time=预览 now（D7 只读预览但需同步播放头） | 预览恒从头开始；退出不回写播放头（长谱反复预览需手动拖回） | ①GamePanel 加 StartAutoplayAt(chart,dir,startMs)（规格已给签名，~15 行）；②MainForm 三分支回编辑器前调 _editor.SetPreviewReturnTime()（GamePanel 暴露 CurrentMs 属性；~20 行）。或降级处理：在 editor-trilab.md 登记「首版从头预览」并更新验收口径（需 captain 拍板） |
| P1-2 | **P1**（✅ t14 已修复，见 §2.1） | 「vs 我」差值行缺失 | §3.1 档位③：陪玩面板顶部差值行（我 Score − AI Score，正绿负红） | 对练=陪玩同台，无差值对比 | GamePanel 陪玩面板绘制段（_ais.Count>0 分支）顶部加一行 vs 差值（~10 行，数据全在 _score/_ais[i].Score） |
| P1-3 | **P1**（✅ t14.1 已恢复（captain 放行），见 §2.2） | 属性 Tab 为只读速览 | §1.1 右栏「属性」=选中音符/事件/判定线属性编辑归整 | 属性 Tab=工程/选中态速览（_propLbl）+提示「属性在音符/事件/判定线页编辑」；编辑仍散在三个 Tab | 可接受简化（编辑功能零丢失），但建议：属性 Tab 顶部加 3 个跳转按钮（「音符属性→」「事件属性→」「判定线→」直接切 Tab 并选中当前对象），消除用户"属性在哪编"的困惑（~20 行） |

---

## 2.1 t14 补丁复核（coder-max 修复 P1-1/P1-2，designer-pro 静态复核通过）

| 项 | 修复实现（已核验） | 复核结论 |
|---|---|---|
| P1-1 进入从当前播放头 Seek | ChartEditorPanel.PlayAutoplay/PlayAi 捕获 startMs=_time 传入事件（:2649/:2664）；MainForm 接线 StartAutoplayAt(chart,dir,startMs)（GamePanel 新增，含越界 try/catch）；AI 演示路径同样带 startMs | ✅ 与 §2.4「从当前播放头位置 Seek 开始」一致 |
| P1-1 退出回写播放头 | GamePanel 新增 SeekToPublic/CurrentMs；MainForm 三条 _testPlayReturn 返回路径（ExitToMenu :140/:142、ExitToLibrary :163/:165、SongEnded :188/:190）统一调 _editor.SyncTimeFromPreview(nowMs)；SyncTimeFromPreview（:2682）含 IsFinite/≥0 守卫 + 画布重绘 + UpdateStatus | ✅ D7「退出同步播放头、不写回编辑内容」完全落地 |
| P1-2 「vs 我」差值行 | GamePanel 陪玩面板首行 i==0 绘制「vs 我 ±N 分」（vs=(int)_score−(int)a.Score，正=+N 绿 127,208,160，负=−N 红 255,122,122）；面板 FillRect 高 +22（多一行槽） | ✅ §3.1 档位③ 差值行口径（正绿负红）逐字一致 |
| 顺带：SelectedAiLevel 口径 | levels==null/levels.Length==0 守卫、idx 用 Length 钳制（:2669-2678） | ✅ 适配引擎侧 AiEngine.Levels 数组类型 |

> t14 报告称构建 0 警告 0 错误、12s 回归探针 PASS（avg 2046 FPS）——运行态最终以 t7 终验为准（§5 第 6 条）。

---

## 2.2 t14.1 恢复复核（captain 拍板放行 · 已恢复，designer-pro 最终静态复核通过）

> 时间线：coder-max 实现 → designer-pro 复核通过 → captain「记录不补」回退 7 处 → captain 拍板放行 → coder-max 恢复 7 处（构建 0 错误、SG-10/11/12 重跑全 PASS，editorai 断言默认=1st Dan，stress-game-report.md 已标注）。
>
> 构建状态补充（coder-max）：SG 断言=GameStressCli.cs:913（editorai 默认=1st Dan）；恢复构建 0 错误（5 条 MSB3026 为并发进程文件锁复制重试告警，进程退出后自愈）；**整树 0 警告 0 错误暂被 t6 进行中引擎改动阻塞**（UiTheme.cs:150 Math 未 using、UiComponents.cs:714 ViewportPolicy/FitMode 未定义——eng-coder-max 多分辨率实现中），t6 完成后恢复；游戏层代码本身编译干净。

| 项 | 恢复后实现（已核验，行号=当前源码） | 复核结论 |
|---|---|---|
| P1-3 跳转按钮 | 属性页「跳转编辑页」三钮：🎵音符→_tabNote、📅事件→_tabEvt、📏判定线→TabPages 不含 _tabLine 时先 SyncAnimPanel 再跳（:803-816，注释「captain 放行」） | ✅ 与 t13 建议一致；Phigros 专属 Tab 边界正确 |
| P2-2 默认 1st Dan | defaultIdx 遍历 items 取 StartsWith(「1st Dan」) 条目（:768-771） | ✅ §3.2 口径落地 |
| P2-3 大谱 toast | MainForm 三入口：:1175（自动游玩）/ :1203（AI 游玩）/ :1211（AI 演示）chart.Notes.Count>2000 → ShowToast「⚠ 大谱面演示可能降帧，建议关闭 3D（V）」；GamePanel.ShowToast 转 public（:1098） | ✅ §2.5#4 逐字一致；开局后提示防 ResetState 清除 |
| 回归确认 | t14 项不受影响（GamePanel StartAutoplayAt/SeekToPublic/CurrentMs、「vs 我」差值行仍在） | ✅ |

> **最终验收口径（t7 按此核对）**：P0=0；P1×3 全部闭环（P1-1/P1-2=t14、P1-3=t14.1）；P2-2/P2-3 已修；P2 剩四条（P2-1 Tab 宽微调 / P2-4 EN-3 变速口径 / P2-5 计划后台生成 / P2-6 状态栏承载）留档。运行态以 t7 终验为准（§5）。

---

## 3. 改进建议（P2 / 布局微调 / 交互细节）

| # | 建议 | 依据 | 量级 |
|---|---|---|---|
| P2-1 | 右栏 7 Tab 在 300 宽下 ItemSize 42×24 共 294px 刚好占满：建议把「Arcaea」并入「音符」页（Arcaea 页本为 3D 视图类控制），或 ItemSize 微调至 40 避免 9.5F 字号拥挤（运行态截图后决定） | §1.2 右栏 300 | 视觉微调 |
| P2-2 | ✅ t14.1 已修（默认=1st Dan，:768-771）；后续有星级映射时按 spec §3.2 接入 | §3.2 默认口径 | 已修 |
| P2-3 | ✅ t14.1 已修（MainForm :1175/:1203/:1211 三入口开局后 ShowToast，见 §2.2） | §2.5 #4 | 已修 |
| P2-4 | EN-3 定稿后：若引擎支持变速，把预览速率接入 TestAutoplay/TestAiPlay（传入 PlayRate）；若 1.0x 限制，则按 t3 v1.2 口径在预览入口弹一次「当前不支持变速预览」提示 | §2.4+t3 v1.2 | ~10 行 |
| P2-5 | AI 计划后台生成：陪玩≥2 或音符>1500 时 Task.Run 生成计划并显示「生成 AI 计划…」 | §3.5 #1 | ~30 行 |
| P2-6 | 状态栏提示落在工具栏 _status 标签（spec §1.2 底部 strip 为二选一预留）：可接受；若后续做独立底部 strip，把预览状态/音符数/判定线数统一迁入 | §1.2 | 可选 |

---

## 4. 待后续事项（本批未列入，标注）

1. **A 回环变奏**（§4.1，D4 P0 建议）：LoopComposer.FlattenToChart + 「演绎为谱面」入口——零引擎依赖，游戏层 ~130 行，建议下批（可与 R2 回环作曲 M2 保存 .mil 合并做）。
2. **B 模式接力**（§4.2，D4 P0 建议）：**引擎前提已解锁**——t6 已落地 EN-1 档位热换 + EN-2 窗口乘数（t3 v1.2 P0 批，含 ×1.0 零差异硬断言；引擎侧已验证（0/0 + EngineChecks EXIT=0，仅剩 captain 官方打包））；**captain 拍板：排期下一轮（交付+终验完成后）建任务**，届时按 §4.2 实施（GamePanel 段调度器 ~250 行 + 编辑器多场面板「接力/同屏」切换 ~40 行）。
3. **C 判定演出**（§4.3，D4 P1）：依赖 EN-4（Storyboard 宿主样板+hook 计数，t3 v1.2 P1 批）。
4. A-B 循环预览（§2.4 P1 项）：建议在 P1-1 播放头同步补丁轮一并做（选区复用现有机制）。

---

## 5. 构建后运行态验证要点（供 t7 终验对照，与 editor-trilab §7 一致）

1. 三栏 1280×800 下实测左 200/中 780/右 300；拉窗 1100/900/700 自适应与自动折叠不裁切；折叠把手/F8/F9/F10/「≡ 面板」；重启保持折叠态与最近谱面。
2. 自动游玩：F5/按钮进入→AUTO 徽章+进度条→ESC 回编辑器；无音频弹「请先加载音频」；多场谱逐场命中。
3. AI 游玩：F6 三档（0/1/2/3 陪玩）；ESC 与自然结束均回编辑器；断网/停 Ollama 服务下功能完全一致（t12）。
4. t1 P0 四项（页签点击/▲▼ 命中/曲库行距/启动贴合工作区）截图复核。
5. 回归：画布编辑/Undo/事件/判定线/AI 检查/自动校准/试玩/导出；--selfcheck/--modetest exit 0。
6. 复查项 P1-1/P1-2（t14 已修复，静态复核通过）：运行态验证——预览退出后播放头=预览终点（含自然结束≈谱面末）；陪玩面板首行「vs 我 ±N 分」正绿负红且面板高度 +22 无遮挡。

---

## 附：核对证据索引

- ChartEditorPanel.cs：三栏装配 :228-243；折叠/自适应 :2461-2523；持久化 :2526-2563；预览 Tab :736-777；AI Tab :783-789；属性 Tab :791-800；PlayAutoplay/PlayAi/PauseForPreview :2642-2690；快捷键 :8275-8279；最近谱面 :1572-1573/:2566-2614/:2811/:3072
- MainForm.cs：_testPlayReturn 三分支 :130-179；TestAutoplay 接线 :1161-1167；TestAiPlay 接线 :1184-1197
- EngineMainShell.cs：StartupClientSize :1192-1201；HostTabBar :695-701/:1501-1525；ArrowCard :853-861
- GamePanel.cs：t14 新增 StartAutoplayAt(chart,dir,startMs)/SeekToPublic(ms)/CurrentMs=>RawMs()；陪玩面板首行「vs 我 ±N 分」绿红差值行（L3 陪玩绘制段）
- MainForm.cs：三条 _testPlayReturn 返回路径均调 SyncTimeFromPreview(nowMs)（:140/:142/:163/:165/:188/:190）
- ChartEditorPanel.cs：PlayAutoplay/PlayAi startMs=_time（:2649/:2664）、SyncTimeFromPreview（:2682，Finite 守卫+重绘）、SelectedAiLevel 改 Length 口径（:2669-2678）
- LoopComposer.cs：FlattenToChart 无（A 案待后续依据）
- t12：no-local-ai-report.md（本地 AI 服务全部禁用，AiChartReview 保留签名返回规则文本）
