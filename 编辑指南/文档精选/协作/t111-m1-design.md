# t111：v3 M1 设计细目（签名级：ms_platform + 默认实现策略 + 生命周期归属 + parity 管线 + lazy-update 边界）

> eng-design-vis · 引用：t110-m1-platform-prep.md（eng-coder-vis 平台草案——本细目=其「细目齐备」兑现；对齐判定见 §1）；t112-review.md（M0 评审：R1 修复前置）。
> §0 M0 评审结论摘要：**有条件通过（R1 必改=R0 前置：生命周期序 Fix；R2/R3 建议随 M1）**——详见 t112-review.md。

---

## 1. 与 t110 草案对齐判定

| t110 草案项 | 判定 | 说明 |
|---|---|---|
| IWindow（12 签名：Title/Width/Height/ClientWidth/ClientHeight/Dpi/Handle/Closed/Show/PumpOne/Present/OnResize·OnKey·OnMouse/Close） | ✅ 采纳 | 本细目 §2.1 为签名级展开（含 CreateWin32Window 工厂） |
| IRenderContext（9 原语） | ⚠ **整改 1** | 改名 **IRenderer**（v1 延续/主流命名）；语义 9 原语+CreateSoftwareRenderer 工厂采纳；Present 移除（呈现归 IWindow::Present——见整改 2） |
| IWindow::Present(const uint32_t*) | ✅ 采纳（v1 语义：窗口 StretchDIBits 帧呈现） | 相应 IRenderer 不 hold Present（渲染→帧缓冲；窗口拉取 Frame()） |
| Input(实例接口+OnKey/OnMouse/GetKey/GetAxis/GetButton) | ⚠ **整改 2** | 采纳实例接口形态（DI）；**补** GetKeyDown/GetKeyUp（边沿——测试/主流 GetKeyDown 语义）+EndFrame（清边沿）；工厂 **CreateWin32Input**（绑 IWindow 回调） |
| IAudioBackend（Open/Play/Pause/Seek/PositionMs/SetTimescale） | ⚠ **整改 3** | 采纳；**补** TimeScale() const get（v1 口径：后端不支持变速=1.0）+IsOpen()；工厂 CreateWinMMBackend() |
| CMake ms_platform 拆分（ms_core←ms_platform←user32/gdi32/winmm） | ✅ 采纳 | 系统库=零第三方红线内 |
| 依赖方向（Core←Platform←Domain←Editor←Tools） | ✅ 采纳 | §4  |
| 优化点记录①注册表直调②lazy-update③软渲 0.42ms | ✅ 采纳 | ②见 §4；①③=实现纪律（M1 组件循环走注册表；软渲 FastBlend/SafeBlend 双路径端口） |

**整改清单（1 条硬性+2 软）**：整改 1 命名（IRenderContext→IRenderer）、整改 2 Input 边沿补齐、整改 3 TimeScale get 补齐——均不改变草案架构，仅签名命名/补齐。

---

## 2. 签名级（ms_platform 每个接口方法 + 默认实现策略 + 生命周期归属）

### 2.1 IWindow（Win32Window 默认实现——win32_window.cpp）

~~~cpp
namespace Milestone::Platform {
struct WindowDesc { std::string title; int width=1280, height=720; };
class IWindow {
public:
  virtual ~IWindow() = default;
  // 生命周期归属：由宿主（m1_demo/Tools）Create 创建、Close/Destroy 归属宿主；OnClose 通知宿主退出循环
  virtual bool  Create(const WindowDesc&) = 0;                       // Win32: RegisterClassW+CreateWindowExW；失败=false+日志
  virtual void  Show() = 0;                                          // ShowWindow(SW_SHOW)
  virtual bool  PumpOne() = 0;                                       // PeekMessageW→Translate/Dispatch；防 0x0 尺寸
  virtual void  Present(const uint32_t* frame) = 0;                  // StretchDIBits+letterbox 黑边（ViewportPolicy）
  virtual bool  Closed() const = 0;                                  // WM_CLOSE 置位
  virtual int   ClientWidth() const = 0;  virtual int ClientHeight() const = 0;   // WM_SIZE 同步
  virtual uint32_t Dpi() const = 0;                                  // GetDpiForWindow（96 默认）
  virtual void* Handle() const = 0;                                  // HWND 裸指针
  std::function<void(int,int)> OnResize;                             // WM_SIZE→(w,h)
  std::function<void(int,bool)> OnKey;                               // WM_KEYDOWN/UP→(vk,down)
  std::function<void(double,double,bool)> OnMouse;                   // client 坐标（DPI 感知）
  std::function<void()> OnClose;
};
std::unique_ptr<IWindow> CreateWin32Window();                        // 工厂（默认实现；GLFW=二期接口）
}
~~~
**默认实现策略**：类注册单例类名（"MilestoneEngineV3Wnd"）；消息泵=非阻塞 PumpOne（宿主循环调用）；Present=帧缓冲→DIB→StretchDIBits（含 letterbox）；DPI=动态（初始 96；WM_DPICHANGED 预留登记）。

### 2.2 IRenderer（SoftwareRenderer 默认实现——software_renderer.cpp）

~~~cpp
class IRenderer {
public:
  virtual ~IRenderer() = default;
  virtual int  Width() const = 0;  virtual int  Height() const = 0;
  virtual void Resize(int w, int h) = 0;                             // 帧缓冲重建（WM_SIZE 触发）
  virtual const uint32_t* Frame() const = 0;                         // 像素（parity/GoldenHash/窗口 Present 取用）
  virtual void Clear(Rgba) = 0;  virtual void ClearRect(double,double,double,double,Rgba) = 0;
  virtual void FillRect(double,double,double,double,Rgba) = 0;
  virtual void FillRoundedRect(double,double,double,double,double,Rgba) = 0;
  virtual void DrawLine(double,double,double,double,Rgba,double=1) = 0;
  virtual void FillCircle(double,double,double,Rgba) = 0;
  virtual void DrawCircle(double,double,double,Rgba,double=1) = 0;
  virtual void FillTriangle(double,double,double,double,double,double,Rgba) = 0;
  virtual void FillQuad(double,double,double,double,double,double,double,double,Rgba) = 0;
};
std::unique_ptr<IRenderer> CreateSoftwareRenderer(int w, int h);
~~~
**默认实现策略**：32bpp RGBA 帧缓冲（uint32 小端=Rgba 打包）；光栅= FastBlend（不透明原语直写）+SafeBlend（alpha>0 用预乘近似——v1 语义）；热路径纪律=0B 分配（无 std::vector 中间物；坐标操作=栈上）。**生命周期归属**：宿主创建；Resize 由窗口 OnResize 回调（宿主绑定）驱动。

### 2.3 Input（Win32Input 默认实现——input.cpp）

~~~cpp
class Input {
public:
  virtual ~Input() = default;
  virtual void OnKey(int vk, bool down) = 0;                        // 窗口回调桥（Win32Input 绑定）
  virtual void OnMouse(double x, double y, bool down) = 0;
  virtual bool GetKeyDown(KeyCode) const = 0;                       // 本帧按下边沿（EndFrame 清）
  virtual bool GetKeyUp(KeyCode) const = 0;
  virtual bool GetKey(KeyCode) const = 0;                            // 持续
  virtual double GetAxis(const std::string& name) const = 0;         // Horizontal/Vertical（WASD+方向键）
  virtual bool GetButton(const std::string& action) const = 0;       // 默认映射：Jump=Space 等
  virtual void EndFrame() = 0;                                       // 帧尾清边沿（宿主驱动）
};
std::unique_ptr<Input> CreateWin32Input(IWindow& win);               // 绑回调
~~~
**默认实现策略**：Windows VK 表→KeyCode 映射（switch）；ActionMap 默认表（Horizontal=WASD+←→ 等——数据驱动表可配（M2 配置））；GetKeyDown 语义=上一帧未按+本帧按（EndFrame 清）；**生命周期归属**=宿主（demo/Tools）创建/EndFrame 由主循环每帧调用；Input 不拥有窗口。

### 2.4 IAudioBackend + AudioClock（WinMM 默认实现——winmm_audio.cpp）

~~~cpp
class IAudioBackend {
public:
  virtual ~IAudioBackend() = default;
  virtual bool Open(const std::string& path) = 0;                    // mciSendString open；失败=false+日志
  virtual void Play() = 0;  virtual void Pause() = 0;  virtual void Seek(double ms) = 0;
  virtual double PositionMs() const = 0;                             // status position
  virtual double TimeScale() const = 0;                               // 不支持=1.0（v1 口径）
  virtual bool IsOpen() const = 0;
};
std::unique_ptr<IAudioBackend> CreateWinMMBackend();
class AudioClock {                                                    // 采样时钟（v1 语义）
public:
  void Bind(IAudioBackend*);                                          // 无后端→EngineTime 回退
  double SongTimeMs() const;  void Tick(double realDt);
  void SetTimeScale(double);  double TimeScale() const;
};
~~~
**生命周期归属**：AudioClock 宿主拥有（Bind 后端）；Open 失败=回退 EngineTime（不崩溃——t65 口径）。

### 2.5 ViewportPolicy（viewport.cpp）

~~~cpp
enum class FitMode { Stretch, Letterbox, Fill };
struct Viewport { double scale; double ox, oy; };
class ViewportPolicy {
public:
  static Viewport Compute(int cw, int ch, int dw, int dh, FitMode);
  static void ToVirtual(const Viewport&, double x, double y, double& vx, double& vy);
  static void FromVirtual(const Viewport&, double vx, double vy, double& x, double& y);
};
~~~
**默认实现策略**：Compute=min(scale)+居中（letterbox）；ToVirtual/FromVirtual 互逆（round-trip 断言）；**归属**=纯静态（无状态）。

---

## 3. parity 抽帧管线（确定性渲染黄金帧）

1. **确定性**：软渲原语=确定性（无 JIT/无随机）→ 同序列同帧缓冲。
2. **管线**：固定场景（m1_parity_scene：Clear+FillRect×N+DrawLine+FillCircle——50 原语序列）→ 渲染 1 帧 → **FNV-1a 64** 对 Frame() 像素 → 输出哈希（--renderhash 模式返回哈希并 exit）。
3. **断言**：GoldenHash 常量（首跑生成→写入 test_parity.cpp；此后比对=防回归）——回归劣化=FAIL。
4. **v1 跨语言对照**：人工（试玩视觉：v3 输出 vs v1 存量截图同场景布局）+自动跨语言=M4 后（像素级跨语言代价高——标注）。
5. **边界**：--renderhash 与 demo 同源场景（防漂移）；哈希输入=Frame() 全缓冲（含 letterbox 黑边=确定性）。

---

## 4. lazy-update 边界（t110 优化点②）

- **触发**：SetPosition/SetRotation/SetScale/SetParent（含 keepWorld 逆运算）→ **标记本节点+子树 dirty**（递归线性标记——子树平均小；空间索引 P4）。
- **计算**：WorldMatrix() 若 dirty→先父后己重算（父 dirty 先清）；**查询不重算**（缓存命中直接返回）。
- **边界**：①LocalMatrix 恒即时（无需缓存）②Parent 变更 keepWorld=true=先算世界位姿再重挂（不破坏表现）③Dirty 传播范围=以本节点为根子树（父先于子；深度优先）。
- **断言**（test_tree_transform 扩展）：①改 1 次+1000 次查询→重算=1（注入计数）②父改→子 WorldMatrix 变（一次传播）③SetParent(keepWorld) 后 WorldMatrix 不变④Local/World 互逆。

---

## 5. 生命周期归属总表

| 对象 | 创建/销毁 | 驱动 | 备注 |
|---|---|---|---|
| IWindow | 宿主（m1_demo/Tools） | PumpOne（宿主循环） | OnClose→宿主退出 |
| IRenderer | 宿主 | Resize（窗口 OnResize） | Frame() 供窗口 Present/parity |
| Input | 宿主（工厂绑 IWindow） | EndFrame（宿主帧尾） | GetKeyDown 边沿=EndFrame 清 |
| IAudioBackend/AudioClock | 宿主 | AudioClock.Tick（宿主帧） | Open 失败=静音回退 |
| ViewportPolicy | 静态 | — | 无状态 |
| LifecycleDriver（Core） | Core 场景 | 宿主循环调 Tick（M1.1 起 demo 接线） | R1 修复序前置 |

---

## 6. 验收（M1 全绿——评审用）

| # | 项 | 判据 |
|---|---|---|
| 1 | 窗口 | Create+PumpOne+Resize/DPI 回调；Present 非黑（试玩视觉或像素抽查） |
| 2 | 软渲 | 9 原语像素级抽查+Resize 缓冲正确 |
| 3 | 视口 | letterbox Compute/ToVirtual/FromVirtual round-trip+DPI 场景断言 |
| 4 | 输入 | GetKeyDown（边沿）/GetAxis 默认映射/EndFrame 清边沿+VK 映射断言 |
| 5 | 音频 | WinMM Open（wav）/Play/Pause/Seek/PositionMs；AudioClock=采样位置（无后端=回退断言） |
| 6 | lazy-update | 重算次数断言+树变换回归绿 |
| 7 | parity | --renderhash 两次一致+GoldenHash 绿+试玩人工对照 v1 |
| 8 | 零第三方/命名 | platform 零第三方；Milestone::Platform |
| 9 | R1 前置 | M0 生命周期 R1 修复已完成（分支前置） |

---

*t111 完成*（签名级 ms_platform/默认实现/生命周期归属/parity 管线/lazy-update 边界/验收 9 项；与 t110 对齐=3 整改（命名/边沿/TimeScale get）；M0 评审→t112-review.md）

> **M1 验收补记（2026-xx）：第 5 项音频=已升级为通过**——captain 官方验证发现并修复引擎 bug（L"open "+kQ 指针算术 UB→mci 命令损坏——真实缺陷非环境项！）；修复后 WinMM 测试 PASS（ms_test_platform 20/20 无 SKIP/0 警告/exit=0；辅助证=PowerShell mci 探针+微型 g++ exe 同命令成功）。M1 验收 9 项=**全部通过（无环境项）**。
