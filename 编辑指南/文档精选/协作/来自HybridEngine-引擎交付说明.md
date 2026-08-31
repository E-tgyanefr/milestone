# 来自 HybridEngine 引擎侧：EngineSDK v1 交付说明（供游戏编写/移植团队）

> 交接方：HybridEngine 引擎团队（引擎专属职责——引擎/编辑器/ABI/SDK）
> 接收方：milestone（ChartPlayer）游戏编写与移植团队（游戏内容/玩法/UI 业务层）
> 时间：2026-08-31 · 引擎版本戳：`e012994` · 引擎验收门：**188/188**（core 24 / platform 21 / bind 42 / editor 94 / render3d 7，dev+release-LTO 双链）· ABI：**82 导出**

> **v1.1 增补（2026-08-31）**：新增 `ms_rnd_clip_push_rotated`（并计数 81→82；矩阵 186→188；`ms_engine_render` 无命令黄金帧 `4634E387E024BE90` 不变；GUI 平面锚点因 t-text-native 文本光栅按视图缩放刷新为 **`855D0AC4CBCD78C2`**——C#/Python 同命令同值已验证）。

## 1. EngineSDK 位置与刷新（游戏团队的唯一入口）

```
D:\EngineSDK\
  include\hybridengine\bind\ms_bind.h      ← 唯一 C ABI 头（81 函数）
  lib\*.a + libhybridengine.dll.a          ← 静态库全链 + DLL 导入库（C++ 消费）
  bin\hybridengine.dll                     ← 稳定 C ABI 动态库（C#/Python 消费；81 导出）
  bin\libgcc_s_seh-1.dll / libstdc++-6.dll / libwinpthread-1.dll  ← MinGW 运行时（同 dir 部署）
  dotnet\HybridEngine.Bind\                ← C# 绑定源码（net8.0 纯 BCL，可 ProjectReference）
  python\hybridengine\ (+ zip)             ← Python 绑定（ctypes）
  template\                                ← 三语言最小消费模板（空壳：窗口+一帧绘制+--frames 120 自检）
  docs\绑定层.md（快照）· abi.txt（头 sha256 + 导出表 81）· VERSION.txt · README.md
```

**刷新协议**：引擎侧 `scripts\release.ps1`（幂等；矩阵 186/186 + 三语言模板自检 `021B54042A405A9A` 双门禁，失败即中止）→ 重新生成 D:\EngineSDK。**ABI 变更必须发新版**（VERSION.txt/abi.txt 为契约；游戏侧以导入/构建是否通过为准）。

## 2. 消费接线（模板即占位，逐语言替换即可）

- **C++**：`add_subdirectory`/静态链 `libhybridengine_{bind,app,core,platform,render3d}.a`（`--start-group` 全链 + `user32 gdi32 winmm`）——模板 `template\cpp\`
- **C#**：`ProjectReference → EngineSDK\dotnet\HybridEngine.Bind`；运行时把 `bin\hybridengine.dll + 3 个 mingw dll` 放 exe 旁（csproj 已自动拷贝）
- **Python**：`PYTHONPATH=EngineSDK\python`；`HYBRIDENGINE_DLL=EngineSDK\bin\hybridengine.dll`；`HYBRIDENGINE_MINGW=<mingw64\bin>`（镜像运行时路径）

## 3. 本版核心能力（游戏层直接用）

| 平面 | API 概况 |
|---|---|
| 引擎/场景 | ms_engine_create/tick/render/pump；Scene/GameObject/Component/Transform（世界坐标辅助）；八回调生命周期 |
| **图形** | `ms_rnd_*` 15：Clear/ClearRect/FillRect/FillRoundedRect/DrawLine/FillCircle/DrawCircle/FillTriangle/FillQuad/BlitRect/**BlitRectAlpha**/**ClipPush/ClipPop**（矩形栈）+ **ClipPushRotated**（旋转矩形——`(cx,cy,w,h,angleRad)`；v1.1 新增——**斜劈/分离位移精确切分**：沿任意角度线裁剪内容，做"对角劈开/扇形分离/位移错位"类视觉直接可用；angle=0 与矩形 push 逐位等价；同栈 ≤8 深）+ `ms_text_draw/measure`（**CJK 原生、掩码零拷贝**：HUD 文本基准 ~13x(dev)/~230x(LTO)——v1.1 起掩码按 `size×viewport.scale` 原生光栅（大窗/高 DPI 文本点对点清晰）；颜色=**0xAARRGGBB（A 生效）**——半透遮罩/渐变/辉光/MISS 红光直接可用 |
| **输入** | `ms_input_*` 13：键（vk 直通）/轴/动作 + **鼠标**（x,y/design-x,y 设计面/三键按住与边沿/滚轮帧累计） |
| **音频** | `ms_audio_*` 8：open/close/play/pause/seek/position/is_open/volume（WinMM；句柄≥10；单线程；时基 ms；多句柄并存）——**偏移校准/BPM 对音/判定音首前提** |
| **资产** | `ms_assets_*` 9：load/type/guid/unref/set_root/save/save_text/**list**（JSON 数组）/texture_pixels（零拷贝借出）；**PNG 即用**（8bit 非隔行 RGB/RGBA，自实现 inflate+CRC/adler32 严格校验；坏文件拒绝）；BMP 不变；.mscene/.msprefab 场景与预制件 |

**框架约定**：设计平面=**1280×800**（parity）；窗口面=Letterbox 按视口映射；命令帧序=**帧首 clear_list → OnFrame 录制（原语+文字）→ ms_engine_render 双面回放**；无命令=黄金帧路径（`4634E387E024BE90`）；GUI 平面参考锚点：**`855D0AC4CBCD78C2`**（C#/Python 同命令同值——v1.1 t-text-native 后刷新）。单线程模型（跨线程=MS_ERR_THREAD）；句柄=引擎拥有、UTF-8 字符串、错误码 0=OK/<0=绑定层/>0=引擎（1..9 保留给错误，音频句柄从 10 起）。

## 4. 已知边界（v1 诚实清单）

- `ms_audio_volume`：v1 仅记录增益，无 DSP（真音量=播放前混音；P1）
- PNG：alpha 通道 v1 忽略（输出恒 0xFFRRGGBB；P1 半透明纹理）
- 文本：单行（多行/对齐/行距= P1）；字体=系统 GDI（GB2312/微软雅黑类；换机缺字体=缺字，内嵌/子集化= P1）
- 预制件/场景=骨架级（Prefab 变体/动画曲线= P2）；D3D11 后端= P2
- 游戏层 GUI 控件库/UI 组件=**游戏侧职责**（引擎只给原语+文本+输入；编辑器侧有 UiKit 可作参考实现）

## 5. 验收门（每次接手/改版跑三样）

1. 引擎自检：`D:\HybridEngine\scripts\build.ps1 -Check`（=186/186 全矩阵）— 引擎侧改动者负责
2. 模板自检：三语言 `--frames 120` 断言 drawHash=`021B54042A405A9A`、GOLDEN 零回归
3. 游戏侧自检：你们既有 `EngineChecks`/DemoRunner 语义的 HybridEngine 版（建议按模板挂守门；CI 化= 建议项）

**AI 协作条款**（沿用 milestone 既有约定并扩充）：见 `编辑指南\文档精选\协作\协作工作法.md` / `给另一个AI的说明.md` / 引擎侧 `D:\HybridEngine\docs\跨团队协作.md`（八节：SDK 刷新/引用/部署/验收门/DPI 三查/时间线警钟/单线程句柄语义/AI 纪律——**绝对路径、Temp worktree、ghost 吸收、禁止跨项目源码树拷贝**）。

## 6. 职责边界（本轮定案）

- **引擎团队**：仅 HybridEngine（引擎/编辑器/ABI/SDK/验收门)）
- **游戏团队**：milestone（ChartPlayer)业务层编写与移植——按本说明消费 EngineSDK

—— HybridEngine 引擎侧
