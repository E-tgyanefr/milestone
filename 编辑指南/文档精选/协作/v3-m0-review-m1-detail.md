# v3 M0 正式评审 + M1 设计细目（review-m1）——eng-design-vis

> 依据：M0 全绿（7 cases/7 pass/0 fail/0 警告/0 错误，WinLibs GCC 20 链）+ 源码逐文件核验（engine-v3 26 文件：CMake 三层/ms_test.hpp/TEST_CASE 宏/生命周期测试/lifecycle_driver/game_object/transform/math 等）。
> 输出：① M0 评审结论（通过+整改清单）② M1 设计细目（Platform 窗口+软渲+输入+音频+parity 抽帧+lazy-update）。

---

## 一、M0 评审结论：**有条件通过**（1 项必改 + 2 项建议）

### 1.1 评审清单逐项

| # | 项 | 核查 | 结论 |
|---|---|---|---|
| 1 | CMake 双链 | GCC（WinLibs GCC 20）已全绿；MSVC/clang++ 链=**未验**（SDK 环境缺） | ⚠ **R2（建议）**——README 结论须精确：『双链=目标（项目模板已备）；**当前已验证=GCC 链**；MSVC/Clang 待 SDK 环境补验』（不夸大「双链已验证」） |
| 2 | 八回调顺序断言 | test_lifecycle 断言每帧序 = [Start, Update, **FixedUpdate**, LateUpdate] | ❌ **R1（必改）**——与 Unity 官方 ExecutionOrder **不符**：官方更新序 = Start → **FixedUpdate → Update** → LateUpdate（FixedUpdate 在 Update 前；且 FixedUpdate 按固定步长可每帧 0..N 次）。改法=driver Tick 分派序改为 **Start→FixedUpdate→Update→LateUpdate**；测试预期同步（[Start,FixedUpdate,Update,LateUpdate]）；Destroy 用例预期同步（6 项序=Start,FE,U,LU,OnDisable,OnDestroy）；FixedUpdate 固定步长累加器=文档化（M0 单步=默认 60Hz 一格；M1 支持累积步长+每帧 0..N 次）——**这正是「原汁原味」的语义正确性问题，必改** |
| 3 | SequenceLog=driver 分派轨迹 | 组件自行记录、driver 仅为轨迹载体——设计取舍 | ✅ 可接受（断言=引擎分派语义的轨迹比对；注释已注明「组件自行记录」——语义清晰；可读性 P2 增强=记录含组件名） |
| 4 | ms_test 质量 | 两级粘贴 __LINE__（__COUNTER__ 贴 ## 不展开=正确规避——MS_CAT_ 层展开=标准做法）；退出码=fails；各例 try/catch | ✅ 合格（Catch2 风格最小集达标；P2 增强=失败时打印表达式值/浮点 nan 告警——不阻塞） |
| 5 | 命名空间/零第三方 | 全文件 namespace Milestone::Core；include 仅标准库（vector/string/cstdio/cmath/functional） | ✅ 符合（零第三方红线保持；无 ChartPlayer/C# 残留——抽查核心头通过） |
| 6 | GameObject::Transform() 与类型同名遮蔽 | go->Transform() 返回 ::Milestone::Core::Transform*——成员名与类型名同名（合法但可读性低；与 Unity GetComponent<Transform>() 更疏远） | ⚠ **R3（建议，不阻塞）**：**值得重构**——方案=成员改名 **GetTransform()**（主流感最大化）+ 保留 Transform() 为 **deprecated inline 别名**（一 release 窗口后删）；或属性式（C++ 无属性——用方法）。建议 M1 顺手实施（M0 评审通过后小改，重跑 7 用例即可） |
| 7 | 零警告/构建 | 0 警告 0 错误（GCC）；CMake 三层结构 | ✅ |

### 1.2 整改清单

| 优先级 | 项 | 说明 | 处理 |
|---|---|---|---|
| **P0（必改）** | R1 生命周期顺序 | driver Tick 分派序=Start→FixedUpdate→Update→LateUpdate；三处测试预期同步；FixedUpdate 累加器语义文档化 | eng-coder-vis 修改+重跑（动作约 40 行+测试 10 行）——M0 验收后补证 |
| P1（建议） | R3 GetTransform | GameObject::GetTransform() 新签名+Transform() 别名（deprecated） | M1 顺手 |
| P2（轻） | R2 README 措辞 | 双链验证状态精确化（已验证=GCC；MSVC/Clang 待验证） | 文档补一行 |

**结论**：M0 语义骨架+测试+ms_test+命名空间+零第三方=**优秀**；R1 为唯一语义偏差（Unity 序）——**修 R1（P0）后即可宣布 M0 达成**（其余 P1/P2 不阻塞，M1 期消化）。

---

## 二、M1 设计细目（Platform 窗口 + 软件渲染器 + 输入 + 音频 + parity 抽帧 + lazy-update）

> 参考主流引擎抽象：GLFW（窗口/输入回调式）、Unity Graphics/Input/IAudio 式后端、Godot DisplayServer/AudioServer（双后端）。v3 实现=接口+默认 Win32 后端（零第三方红线）。

### 2.1 Platform 层布局（新增 include/milestone/platform/* + src/platform/*）

~~~
engine-v3/include/milestone/platform/
├─ platform.hpp        # umbrella；Platform 初始化/版本
├─ window.hpp          # IWindow（接口）+ WindowDesc
├─ renderer.hpp        # IRenderer（9 原语平面接口——v1 语义保留）
├─ viewport.hpp        # ViewportPolicy（letterbox/DPI——v1 端口：Compute/ToVirtual/FromVirtual）
├─ input.hpp           # InputState（Key/Btn 枚举+ActionMap GetAxis/GetButton/GetKeyDown）
├─ audio_backend.hpp   # IAudioBackend（Open/Play/Pause/Seek/PositionMs/TimeScale）
└─ audio_clock.hpp     # AudioClock（采样时钟；无后端回退 EngineTime）
src/platform/
├─ win32_window.cpp    # 窗口（Create/PumpOne/Resize/DPI/消息→InputState 桥）
├─ software_renderer.cpp# 软渲（uint32 帧缓冲+9 原语+Resize+Present）
├─ win32_input.cpp     # Win32 键鼠→ InputState/ActionMap
├─ winmm_audio.cpp     # mci 端口（v1 WinAudioBackend 语义）
└─ viewport.cpp
~~~

### 2.2 接口签名（M1 蓝本）

~~~cpp
namespace Milestone::Platform {

// —— 窗口（GLFW 式接口）——
struct WindowDesc { std::string title; int width=1280, height=720; };
class IWindow {
public:
  virtual ~IWindow() = default;
  virtual bool Create(const WindowDesc&) = 0;
  virtual bool PumpOne() = 0;                 // 泵一条消息（键盘/鼠标/尺寸→回调）
  virtual void Show() = 0;  virtual void Close() = 0;
  virtual void Present(const uint32_t* frame) = 0;   // Win32 帧呈现（StretchDIBits——v1 语义；IRenderer.Present 移除）
  virtual int  ClientWidth() const = 0;  virtual int ClientHeight() const = 0;
  virtual uint32_t Dpi() const = 0;
  virtual void* Handle() const = 0;
  virtual std::function<void(int key, bool down)> OnKey = nullptr;    // WM_KEYDOWN/UP
  virtual std::function<void(double x, double y, bool down)> OnMouse = nullptr;
  virtual std::function<void(int w, int h)> OnResize = nullptr;
  virtual std::function<void()> OnClose = nullptr;
};
IWindow* CreateWindow(const WindowDesc&);     // 默认 Win32 实现（GLFW 后端=二期接口）

// —— 渲染（9 原语——v1 语义/命名保留）——
struct Rgba { uint8_t r,g,b,a; };
class IRenderer {
public:
  virtual ~IRenderer() = default;
  virtual int Width() const = 0;  virtual int Height() const = 0;
  virtual void Clear(Rgba) = 0;  virtual void ClearRect(double x,double y,double w,double h,Rgba) = 0;
  virtual void FillRect(double x,double y,double w,double h,Rgba) = 0;
  virtual void FillRoundedRect(double x,double y,double w,double h,double radius,Rgba) = 0;
  virtual void DrawLine(double x1,double y1,double x2,double y2,Rgba,double thickness=1) = 0;
  virtual void FillCircle(double cx,double cy,double r,Rgba) = 0;
  virtual void DrawCircle(double cx,double cy,double r,Rgba,double thickness=1) = 0;
  virtual void FillTriangle(double x1,double y1,double x2,double y2,double x3,double y3,Rgba) = 0;
  virtual void FillQuad(double x1,double y1,double x2,double y2,double x3,double y3,double x4,double y4,Rgba) = 0;
  // （Present 移除——呈现归 IWindow::Present(frame)；本接口经 Frame() 暴露像素）
  virtual void Resize(int w, int h) = 0;
  virtual const uint32_t* Frame() const = 0;  // 像素（0xFFRRGGBB——v1 ToArgb 口径；软渲共享/parity hash 用）
};
IRenderer* CreateSoftwareRenderer(int w, int h);
// （D3D11 后端=二期接口——V3-4 声明；接口已就绪=可插拔）

// —— 视口（letterbox/DPI——v1 ViewportPolicy 语义）——
enum class FitMode { Stretch, Letterbox, Fill };
struct Viewport { double scale; double ox, oy; };
class ViewportPolicy {
public:
  static Viewport Compute(int cw, int ch, int dw, int dh, FitMode mode);
  static void ToVirtual(const Viewport&, double x, double y, double& vx, double& vy);
  static void FromVirtual(const Viewport&, double vx, double vy, double& x, double& y);
};

// —— 输入（Unity/Godot 语义：公开面=ActionMap+轮询；整合 t110 草案=实例接口+DI）——
enum class KeyCode { Space=0x20, D=0x44, F=0x46, J=0x4A, K=0x4B, W=0x57, A=0x41, S=0x53,
                     Left=0x25, Right=0x27, Up=0x26, Down=0x28, Escape=0x1B, R=0x52, P=0x50 };
class Input {                                  // 实例接口（Win32Input 实现；Core 传引用）
public:
  virtual ~Input() = default;
  virtual void OnKey(int vk, bool down) = 0;  virtual void OnMouse(double x, double y, bool down) = 0;
  virtual bool GetKeyDown(KeyCode) const = 0;  virtual bool GetKeyUp(KeyCode) const = 0;
  virtual bool GetKey(KeyCode) const = 0;
  virtual double GetAxis(const std::string& name) const = 0;   // Horizontal/Vertical=WASD+方向键
  virtual bool GetButton(const std::string& action) const = 0;
  virtual void EndFrame() = 0;                 // 清 GetKeyDown 边沿
};
std::unique_ptr<Input> CreateWin32Input();     // 绑 IWindow 回调（内部桥）

// —— 音频（采样时钟驱动）——
class IAudioBackend {
public:
  virtual ~IAudioBackend() = default;
  virtual bool Open(const char* path) = 0;  virtual void Play() = 0;  virtual void Pause() = 0;
  virtual void Seek(double ms) = 0;  virtual double PositionMs() = 0;
  virtual double TimeScale() const = 0;          // 后端不支持变速=1.0（v1 口径）
  virtual bool IsOpen() const = 0;
};
IAudioBackend* CreateWinMmAuidBackend();       // mci 端口（v1 语义）
class AudioClock {                              // 采样时钟（v1 语义）
public:
  void Bind(IAudioBackend*);  double SongTimeMs() const;
  void Tick(double realDt);   void SetTimeScale(double);  double TimeScale() const;
};
}
~~~

### 2.3 lazy-update（Transform 世界矩阵 M1 优化）

- **方案**：Transform dirty 传播——SetPosition/SetRotation/SetScale/SetParent 标脏（本节点+子树）；WorldMatrix() 查时若脏→重算（先父后己）；**查询不重算**（重复读=缓存）。
- 断言（test_tree_transform 扩展）：①改一次+1000 次查询→重算次数=1（计数注入）②父改→子 WorldMatrix 随之变③SetParent(keepWorld=true) 后 WorldMatrix 不变。

### 2.4 parity 抽帧（确定性渲染黄金帧）

- **机制**：软渲 9 原语=确定性 → 相同绘制序列每帧像素一致。
- **实现**：①IRenderer::Frame() 公开像素②**--renderhash**（固定场景绘制一帧→FNV-1a 64 哈希打印——tools 先导）③测试=GoldenHash 断言（首跑生成 golden 常量，后续比对=防回归——同 v1 基线思想）。
- **跨语言 parity**（v1↔v3）：v1 存量截图作人工参照（试玩视觉对照）；自动跨语言=后续（M1 交付=同场景人工对照+GoldenHash 自动）。
- 验收：--renderhash 两次运行一致；GoldenHash 断言绿；试玩抽帧视觉对照（非黑屏/布局合理）。

### 2.5 M1 验收清单（评审用）

| # | 项 | 判据 |
|---|---|---|
| 1 | 窗口 | IWindow 创建+Resize/DPI 回调；PumpOne 收 Key/Mouse 日志 |
| 2 | 软渲 | 9 原语单测（像素级抽查：FillRect/DrawLine/FillCircle 对应像素）+Resize 缓冲正确 |
| 3 | 视口 | letterbox Compute/ToVirtual/FromVirtual round-trip 断言（DPI 缩放场景） |
| 4 | 输入 | GetKeyDown/GetAxis（默认映射）断言；EndFrame 清边沿；窗口按键→Input 桥日志 |
| 5 | 音频 | WinMM Open（wav 样本）/Play/Pause/Seek/PositionMs；AudioClock SongTime=采样位置（无音频=EngineTime 回退——断言） |
| 6 | lazy-update | 重算次数断言（1 次传播/查询不重算）+树变换回归绿 |
| 7 | parity | --renderhash 稳定+GoldenHash 断言；试玩抽帧人工对照 v1 参考 |
| 8 | 零第三方/命名空间 | platform 层零第三方；namespace Milestone::Platform |
| 9 | 双链 | README 更新：GCC 已验；MSVC/Clang=SDK 补验（R2 落实） |

---

## 三、交付与排期

- M1 拆三档（给 eng-coder-vis）：**M1.1** 窗口+软渲+视口+Input（含 GetTransform R3 顺手）→ **M1.2** 音频+AudioClock → **M1.3** lazy-update+parity GoldenHash+README（R2）+验收全绿。
- 每档验收=对应断言组+构建 0 警告；M1 全绿=captain 构建+试玩抽帧视觉。

---

*M0 评审（有条件通过——R1 必改+ R2/R3 建议）+ M1 细目（接口签名/黄金帧/验收 9 项/三档拆分）*
