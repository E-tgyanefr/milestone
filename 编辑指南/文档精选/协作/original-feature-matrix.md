# 原版功能矩阵（Milestone.exe v1 冻结基准——M4 功能对标盘）

> 来源：游戏源码/源码/**/*.cs（70 文件）——功能抽点（v1 权限=冻结基准，仅作对标）

| 域 | 文件 | 功能点 |
|---|---|---|
| 导航 | MainForm/EngineMainShell/SecondaryPages | 引擎壳主菜单/二级页（曲库管理/我的数据/段位/键位/设置） |
| 曲库 | FolderPanel/ModeSystem/ChartParser(+Extra)/ZipImport | 多目录扫描/多模式（Cytus·Phigros·Mania·Arcaea·osu）/导入（.osu+.mcy+zip）/谱面模型 |
| 游玩 | GamePanel.Play/GamePanel.Hud/GamePanel.Editor | 判定线/音符滚动/HUD/判定区渲染/暂停/跳过/布局自定义 |
| 判定 | JudgementEngine/JudgeSettings | 四档判定（PERFECT·GOOD·BAD·MISS 族）/判定配置 |
| 结果 | GamePanel.Result | 结算（准度/连击/评级/成绩记录） |
| 音频 | AudioPlayer/SoundFx | 音乐引擎（WinMM 类）/音效 |
| 视觉 | D2DRenderer/GraphicsQuality/FxParticles/UiTransition | D2D 渲染/质量档/粒子/转场 |
| 编辑器 | ChartEditorPanel/ChartAiAssistant/ChartMentor | 谱面编辑（事件/属性/判定线/音符/预览/AI 面板——editor-trilab 规范）/AI 检查/教练 |
| AI | AiEngine/AiDevice/AiTrainer/AiChartReview/AiSetupForm | AI 引擎/设备检测/AI 训练/谱面审查/AI 设置 |
| 数据 | PlayerData/ReplaySystem/AppConfig/DarkMode | 玩家数据/回放/配置/暗色模式 |
| 联机 | MpManager/MpLobbyForm/MpEmbedPanel/MpLeaderboard | 联机大厅/嵌入面板/排行榜 |
| 工具 | PackExporter/ParityCli/StressTestCli/GameStressCli/EngineStressCli | 打包导出/parity 对拍/压力测试 CLI |
| 优化 | FpsGovernor/GpuGuard | 帧率治理/GPU 保护 |
| 其他 | LoopComposer/SkinSettings/CalibrationForm | 循环合成/皮肤设置/校准 |
```
