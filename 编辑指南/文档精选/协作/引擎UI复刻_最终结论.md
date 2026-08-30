# 引擎驱动 UI 复刻 · 最终结论（视觉级一致）

- 日期：2026-08-26 · 评审：vision（deepseek-v4-flash-vision-exp 像素级）· 构建：0 警告 0 错误

## 判定
**P0=0：可认定「多页截图级一致」**（主菜单 / 选歌页 / 设置页，引擎 1280×800 离屏 GDI vs 真 legacy 全窗口 1707×1067）。

## 证据链（sha256 前缀）
- 引擎：shell-menu 9B423045 · shell-songs 3D768828 · shell-settings 41979FF6（构建产物/shots/engine-final/）
- legacy：A6668DA2 · E79DBB81 · 05C2182A（构建产物/shots/legacy-final/，真 legacy UI 全窗口，非引擎窗/非弹窗）
- 报告：其他/docs/协作/视觉终验3.md（全项）、视觉终验3b_t58定点复验.md、视觉终验报告2.md；证据裁剪 构建产物/obj/vision-crops4/C_*.png

## 对齐内容
1. 主菜单：18 格(9 行×2 列) legacy AddCell 全顺序、18/18 真实 emoji 图标（渲染层 emoji run-split）、10 模式片（Routlock/ADOFAI/osu!std 改名映射，含回环作曲）、玩家卡（玩家名标题+圆形头像+单行统计 10F）、▶ 开始游戏、黄色快捷键行。
2. 选歌页：♪选歌 + 搜索框 + 全部模式循环 + ▶ 游玩当前/📂 曲库/🏠 主菜单 工具栏；空态右下右对齐两行白字；无按钮重叠。
3. 设置页：UiTabBar 全宽页签 9 分区；游戏分区 legacy 表单（▲▼ 微调×3 / 🎯 自动调整延迟 / 键位 4K 行内绿 / 判定基准(默认音符下端) / □ 自动复选框）；行高 42 不切头；主题卡已删。

## 功能/运行结论
- 单窗口：默认启动（引擎 UI）可见顶层窗口=1；游玩/编辑同窗承载（t50/t51 P1 Hide() 修复保留）。
- parity：279/279 一致 · 差异 0 · 已知偏差 1（Arcaea 得分口径：宿主 10M vs 引擎 300 基制，判定/ACC/档位一致——产品级演进项）。
- --selfcheck EXIT=0；EngineChecks 基线维持。

## 遗留记录项（不阻塞，样式/演进级）
- 头像为圆形+玩家名首字（无位图图元，宿主层 AvatarImage 由承载提供时升级）。
- 自动/自动游玩标签：引擎与 legacy SettingsPanel 同文（自动游玩），记录 vision 读法差异。
- 判定基准/全部模式为循环按钮（无下拉/▾ 图元，行为等价 8 项循环）。
- 页签样式：引擎圆角芯片 vs legacy 全宽方块（可后续 UiTabBar 样式细化）。
- 迷你 mania 演示窗 = 2 个顶部窗口（独立演示窗体，legacy 同构，记录）。
- 搜索框无 IME（仅 ASCII 键盘输入过滤）。

## 本轮修复摘要（从真 legacy 截图揭示后的全部工作）
- 参数循环 bug（--legacyui 单项被忽略）→ 真 legacy 参照才首次拍到；新增 --legacysettings。
- 引擎三页重构（t55 engcore）：18 格/10 片/玩家卡/工具栏/表单控件。
- 渲染层 emoji run-split（t56 aihelper，Segoe UI Emoji 回退，GDI/D2D 双通道）。
- 视觉级收尾（captain）：空态挂 bg 层右下、统计行 9F、▲▼ 双三角、流速 0.00、延迟标签加宽、JudgeBase 无 cfg 路径默认=音符下端。
- 运行修复（verifier t51）：游玩/编辑成功后误 Hide() 删除（同窗承载）；4 处编译修复；windrive 单窗口工具 + parity 基线固化。

## 补充终验（2026-08-26 晚）

### 1. 窗口放大/定位（用户反馈“放大后无法正确定位”）
- **结论：无 P0，引擎窗两档均居中/无裁切/完整页面**（vision 逐像素，DPI 感知实屏裁剪）：自然 1280×800 scale=1.0（按钮 x282..998 cx=640 精确居中；黄色提示 y772..794 距底 6px）；最大化 2560×1494 scale=1.868（画布 2391×1494 水平居中、左右 letterbox 各 ≈84.5px；九行网格 + 提示行全部可见）。
- 证据：构建产物/obj/dpi-natural.png（9BE6AC6D）、dpi-maximized.png（C363E1CA）。
- **重要纠偏**：此前“1:1+偏右+裁剪”的判定来自 DPI-unaware 外部进程的虚拟化截图坐标（只截到窗口左上角）—— 已撤销；引擎窗 OnPaint letterbox（物理客户区 GetClientRect + min(w/1280,h/800) 等比居中）验证正确。
- 修复项：OnPaint/HandleCreated 统一用物理客户区（PhysClient，t59）；GPU 保护 WinForms 弹窗移除（其阻断/叠加曾中断引擎窗创建，是“窗口打不开/错位”的另一元凶）。

### 2. GPU 调用策略（用户决策：常态也调用 GPU；避让仅限 AI 演示/陪玩）
- 常态：GameSettings.ForceWarp=false —— 硬件 GPU 照常（日志：渲染后端 Direct2D1 · GPU: NVIDIA GeForce RTX 5070 Ti Laptop GPU）。
- AI 演示/AI 陪玩会话：进入时 GpuGuard.BeginAiSession()（检测 llama-server 等 >1.5GB 时本会话临时 WARP，GPU 让给本地 AI 服务防冻结）；离开/结束时 EndAiSession() 恢复硬件 + D2DRenderer.MarkRecreate() 重建渲染目标（GamePanel.RestartRenderer）。
- 启动不再弹 GPU 保护对话框（仅日志）；llama-server -ngl 99 10GB 运行下实测：窗口正常打开、渲染正常、无冻结。
- 接线：MainForm（AiDemoFromPath/MpManager.OnStart 进入；ExitToMenu/ExitToLibrary/SongEnded 退出）+ GameSettings + GpuGuard + D2DRenderer + GamePanel。

### 3. 关闭窗口不回退旧界面（用户反馈 t60）
- 修复：引擎窗 FormClosed 不再 this.Show()+ShowMainMenu（旧 WinForms 主界面），改为 Application.Exit() 直接退出应用；已实测 WM_CLOSE 后进程退出（无旧界面闪现）。
- 主题切换动作从 Act 改为引擎内执行（不再因 Act 显示旧主窗体）。
- 遗留记录：联机/AI 演示/回环作曲/迷你 mania/引擎 UI 演示 仍走 Act（旧窗承载或旧对话框）——后续可逐项引擎化（联机含 MP 大厅整体迁移，工作量大，另立任务）。

### 4. 打包导出完整程序（新功能）
- 入口：`Milestone.exe --pack "<谱面>" [--out "<目录>"]` → 产出 **单文件 exe**（<曲名>.exe，~66MB 自包含，无 .NET 依赖）：双击即运行播放器窗口自动演示该谱面（4K 下落）、按键 D F J K 可玩、ESC 关闭、播完自动关闭。
- 原理：引擎库 MilestoneEngine（零依赖）+ engine\App Win32 壳 + SoftwareRenderer 播放器模板（MilestonePlayer.exe）；谱面解析（全 10 模式/多部件）→ 归一化 4K 时间列 → 二进制内嵌 exe 尾部 `[payload][len][MILSTPK1 magic]`。
- 新增文件：引擎\engine\PublishPlayer\（PublishPlayer.csproj + Program.cs）、源码\CoreUtil\PackExporter.cs、Program.cs --pack 调度、MilestoneEngine.csproj `<Compile Remove="PublishPlayer\**"/>`。
- 实测：Dense Stress 4K（276 音符）打包 → 运行 12.3s 自动关闭 exit 0；iidx（12）arcaea（62）同样成功。
- 注意：模板发布命令 `dotnet publish 引擎\engine\PublishPlayer -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o 构建产物\pack-template`，产物复制为 bin\Release\...\ pack-template.exe。

### 6. 主菜单现代 UI（定稿 sha 47DDC981）
- 用户指令：主菜单按网上现代 UI 设计重构（2026 启动器风格：暗色渐变+光晕星点+玻璃拟态卡片+蓝色 CTA+宽松留白）。
- 历程：t58 初版（vision 判定方向优秀但 P0×3 重叠）→ t60 几何修复 → t61 二次修复（统计两行卡/药丸 5×2/迷你卡字号）→ **vision 终判 P0=0 通过**；t61b 再修 P1（迷你 mania 卡宽 286→317）后 sha=47DDC981。
- 18 入口全保留（CTA+3 快捷+14 功能卡+顶栏退出）；动作/文案=UiText 原文；布局=顶栏(Logo/玩家胶囊/设置/退出)+左主区(CTA/快捷/两行统计/5×2 药丸)+右功能区(2 列玻璃卡)+底部提示。

### 5. GPU/CPU 调度引擎内直接调整
- 引擎设置页「界面」分区新增两行（即改即生效）：
  1. **渲染后端**：硬件 GPU / 软件渲染（WARP）——切换后 GameSettings.ForceWarp + GamePanel.RestartRenderer()（渲染目标重建）。
  2. **CPU 并行度**：自动（=Environment.ProcessorCount）/ 1 / 2 / 4 / 8 / 16——直接设置 EngineJobs.Degree（引擎级并行门，全部 EngineJobs 并行 API 生效）。
- UiText 新增常量：SettingsUiRenderBackend/RenderHardware/RenderWarp/CpuDegree/CpuAuto。
