# Milestone 引擎架构（迭代计划）

> 目标：把玩法逻辑从 UI/渲染中解耦，形成可维护的引擎层。逐步迭代，每步保留回归测试。

## 分层
```
UI（MainForm/SongCardView/SettingsPanel）     —— 界面与导航
 ├─ ChartEditorPanel（谱面编辑器）            —— 编辑专用
 └─ GamePanel（游戏画面 + D2D 渲染 + 输入）    —— 渲染/输入粘合层
     └─ Engine 层（新增，逐步抽取）
         ├─ ModeSystem      —— 模式注册表（显示名/键数/有轨无轨/默认键位/mode字符串）
         ├─ JudgementEngine —— 判定/计分/ACC/HP/连击（按模式判定预设）
         ├─ ChartStore      —— 谱面数据（解析/序列化/parts 部件选择）
         └─ Replay/AI       —— 回放与 AI（现状保留，后续迁入）
```

## 迭代步骤（每步编译 + 28 谱面回归 + 段位训练回归）
1. **ModeSystem.cs**：模式注册表（单一事实来源）。迁移消费方：SongCardView.ModeInfo、ChartEditorPanel.ModeKeyCount/ModeDisplayName/ModeKey、ChartParser.ModeDisplayName、GamePanel 键位映射。
2. **JudgementEngine**：把 GamePanel.Hit()/hold/漏判/HP 抽为纯逻辑类（无渲染依赖），GamePanel 委托调用；用固定种子对拍回归（对比抽取前后模拟结果一致）。
3. **ChartStore**：谱面载入/parts 选择/元数据统一入口（MainForm/GamePanel 改用）。
4. 渲染保持 GamePanel（D2D 粘合层），引擎层不依赖 SharpDX。

## 判定标准备忘（JudgeSettings 现状）
- Malody 段位：C 判 36/76/110ms，权重 100/80/60，纯 ACC 过段
- osu!mania：lazer 窗口公式（OD），lazer HP（DR 校准、MISS 掉血），HP 存活
- 各模式判定见 JudgeSettings.ApplyForChart
