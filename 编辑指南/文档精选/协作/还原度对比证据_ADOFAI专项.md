# ADOFAI 还原度对比证据(第一轮专项)

> 工具:FrameCompare(ffmpeg + ResNet18 DirectML GPU,RTX 5070 Ti)
> 实机:adofai_real.mp4(B 站 BV1mzLY6UEXr《3-X 发条 重制版 实机演示》)
> 程序:Milestone --play adofai_real_test.adofai --adofai2 游玩中 F12 截屏
> 完整报告:research_tmp\fc_adofai2.csv(44 帧 × 29 截图)

## 对比结果

- 平均 0.650 · 最高 0.830 · 最低 0.503
- **程序 ADOFAI 游玩截图未成为任何实机帧的最佳匹配**(最高 0.516@114445)——画面结构与实机差异明显

## 证据图(见 docs\协作\对比证据\)

| 文件 | 内容 |
|---|---|
| ADOFAI实机_t10s/30s/50s.png | 实机游玩画面 3 帧(轨道+双球+砖块) |
| ADOFAI程序_游玩1/2.png | 本程序 ADOFAI 游玩画面 2 帧 |
| ADOFAI程序_编辑器.png | 本程序 ADOFAI 俯视路径编辑器 |

## 给策划的裁决请求

1. 逐对查看 ADOFAI实机_* vs ADOFAI程序_* 截图,列出**可见差异**(按影响排序):
   - 轨道形态/透视/旋转方向
   - 双球视觉(固定球/绕旋球大小颜色、运动轨迹)
   - 砖块(tile)形状/大小/间距/发光
   - 背景/UI/判定反馈
   - 编辑器:俯视路径 vs 实机编辑器的差异
2. 下发修正规格(可引用 策划\ADOFAI玩法规格.md 已有内容,重点在**视觉呈现**差异)。

## 循环说明

本轮起 FrameCompare 已接入闭环:改完视觉 → 重新截游玩画面 → 跑对比 → 差异清单 → 策划裁决 → 再改。目标:相似度 ≥0.9 或策划判定无可见差异。
