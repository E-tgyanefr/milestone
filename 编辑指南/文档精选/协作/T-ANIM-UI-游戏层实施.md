# T-ANIM-UI 游戏层实施（coder-vis——用户定稿『斜劈光束』非游玩界面切换动画）

## 已完成（框架——X2 原语就绪前可编译/测试/验证）
1. **Ui/BeamTransition.cs**（新）：
   - BeamTimeline 纯数学——原版 UiTransition.BeamTransition 曲线 1:1 端口（只读参考）：SlashP=Smooth(t/0.22)/RotP=(0.22-0.50)/SplitP=(0.50-1.0)/NewAlpha=Smooth((t-0.22)/0.78)/OldBright=1-0.88·Smooth(t)/BeamAlpha=t>0.5 淡出/Gap=SplitP·diag·0.62/BeamLen；总时长 **0.8s**（设计建议）；
   - ThetaFor(fromId,toId)：页面 Id 对→**固定斜角**（35°+0..30°·镜像随机——原版 θ0 口径；哈希确定性——selftest 断言）；
   - IBeamDraw 绘制抽象：DrawToPage(alpha)/DrawFromPage(bright)/BeginSplit·DrawFromSplit(side,off)·EndSplit（X2：Blend quad 层——原版 PushQuadLayer 等效）/DrawBeam(厚线辉光×3 与裂后边缘线×2)；
   - BeamDrawStub（X2 前=页面 render 直调——无绘制期=黑屏过渡预期；X2 后 BeamDrawX2 绑定 ms_rnd_*）。
2. **App/PageStack.cs 换页钩子**：TransitionKind.Beam（默认）；Push/Pop/PopTo 统一 Hook→StartFx（旧/新页委托+ThetaFor+视口 1280×800）；Update 驱动 _fxT 0→1；Render 按原版序（新页渐入→旧页变暗/裂开两侧→光束）——**游玩页（PlayPage）跳过**（用户设计：不触游玩界面）。
3. **SelfTest 6 断言**：t0 锚（劈未始/零长）/t=0.22（劈完旋转未始）/t=0.50（旋转完裂未始）/t=0.80 裂中 t=1.0 完+全显/旧页渐暗曲线（0.88→t=1 剩 0.12）/页面对固定斜角确定性。

## 待接（依赖）
- **X2 ms_rnd_* 就绪**→BeamDrawX2（FillRect+Line 厚线辉光+Blend 渐层——原语编码；算法/几何/缓动已全就位=零数学改动仅绘制绑定）；
- **eng-design 规格**（时间线/几何坐标/性能预算——本侧参数=原版端口；规格到后按表核对差异（如起止角规则/时长/t 分段）——若差异仅参数=一行常数；若结构=回调表对齐）；
- **性能预算**（软渲全屏合成 ≤3ms——X2 后 benchmark 验证；当前数学=O(1)/帧零分配：BeamFrame record 值语义）。

## 验证
- build 0 错 0 警；--selftest **PASS 34/34**（+beam 6 断言）；--abi-check 22/22（回归无损）；两树同步 MD5 一致（+BeamTransition.cs）。

## 注记
- 仅非游玩界面切换（用户设计）；PlayPage=跳过（游玩=域场景组件化，不走 PageStack 转场）；
- 原版差异：D2D PushQuadLayer（GPU）→引擎=X2 Blend quad 层（软渲——性能预算 3ms 待 X2 验证）；帧不分配（record struct 值传递）。