# t103 AOT 探路（D-1）：单文件 ReadyToRun/AOT 方案+实测（探路——不改发布链路）

> 编写：eng-coder-vis（引擎编写）· t103 · 目标：20-30MB/冷启动 ≤1.5s；约束：零 NuGet/零 WinForms/零 System.Drawing 红线不变

## 方案（三条路径实测）

| 路径 | 命令 | 预期 | 现状 |
|---|---|---|---|
| JIT 单文件（现状） | dotnet publish -p:PublishSingleFile=true | ~67MB/启动 ~2s | t95 验收链（编辑/工具窗） |
| ReadyToRun（首选） | publish -p:PublishReadyToRun=true | 30-45MB/启动 ~0.8-1.2s（R2R 免 JIT 热点） | 待实测（captain 构建记录） |
| NativeAOT（目标） | publish -p:PublishAot=true（需组件化裁剪：Windows API 走 #if WINDOWS P/Invoke 兼容） | 20-30MB/启动 ≤0.5s | 需 AOT 兼容改造（反射组件/Editor 反射工作量大——Editor 可 AOT 例外） |

## 关键点

1. AOT 反射限制：Editor Inspector 反射（GetFields/SetValue）+AssetManager 类型查找在 NativeAOT 需源生成——故 AOT=引擎核心库（GameEngine/Lifecycle/渲染/域库），Editor=JIT 例外（设计 D-1 声明）。
2. ReadyToRun 折中：零改造（反射兼容——JIT 运行时后备），R2R 预编译热点——当前推荐（20-30MB 不达但 30-45MB + 启动 ≤1.2s 达标）；AOT 目标=20-30MB 需 Editor 降级例外。
3. SizeMap：publish 后大件审计（Assembly 内嵌谱/模板——pack-template.exe 67MB 是内容非引擎——引擎本体单文件 20-45MB 内）。

## 实测值（captain 构建后记录）

| 路径 | Size | 启动 | 备注 |
|---|---|---|---|
| JIT 单文件 | ~67MB | ~2s | 现状基线 |
| R2R | 待测 | 待测 | 推荐 |
| AOT 核心 | 待测 | 待测 | Editor 例外 |

## 结论

- 推荐路径：ReadyToRun（零改造折中）；AOT 核心库列入批C P2（反射源生成器接入后）。
- 目标 20-30MB=AOT 核心库（Editor JIT 例外——发布双形态：engine-aot 核心 + editor-JIT）。
- 不改发布链路（探路+方案——captain 既定发布流不动）。