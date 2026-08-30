# v3 M1 平台层技术准备（eng-coder-vis · M0 全绿待命——等 eng-design-vis 细目齐备后实现）

> 任务：M1=Platform 平台层（窗口+软件渲染器+输入+音频）+parity 抽帧+lazy-update——本文档=接口草案与 CMake 目标拆分（调研/草拟；实现待细目）
> 决策锚点：V3-1 C++20/V3-6 首阶段 Win 软渲（D3D11 后续）/零第三方/自绘 Editor=M3/ms_bind C ABI=M2.5

---

## 1. 参考蓝本（v1 已验证 C# 平台接口——签名 C++ 化）

| v1（C# legacy） | 语义 | 备注 |
|---|---|---|
| IEngineWindow（Title/Width/Height/ClientWidth/ClientHeight/Dpi/Fit/Handle/Closed/Show/PumpOne/Present/SizeChanged/Key/Mouse/ClosedEvent） | Win32 窗口+消息泵+帧呈现（P/Invoke StretchDIBits） | t6 多分辨率/letterbox 已验 |
| IEngineRenderer（9 原语：Clear/ClearRect/FillRect/FillRoundedRect/DrawLine/FillCircle/DrawCircle/FillTriangle/FillQuad+Present(frame)） | 软渲原语 | t9 0B 热路径已验；本 M1 软渲 C++ 端口 |
| IEngineAudioBackend（Open/Play/Pause/Seek/PositionMs/Poll/Close——WinMM/mci） | 音频后端接口 | t65 mci 已验；M1 WinMM 或 WASAPI |

## 2. C++20 平台接口草案（ms_platform/）

```cpp
namespace Milestone::Platform {

struct WindowDesc { std::string title; int width = 1280, height = 720; };
class IWindow {
public:
    virtual ~IWindow() = default;
    virtual bool Create(const WindowDesc&) = 0;
    virtual void Show() = 0;
    virtual bool PumpOne() = 0;
    virtual void Present(const uint32_t* frame) = 0;
    virtual bool Closed() const = 0;
    virtual int ClientWidth() const = 0;
    virtual int ClientHeight() const = 0;
    virtual uint32_t Dpi() const = 0;
    virtual void* Handle() const = 0;
    std::function<void(int,int)> OnResize;
    std::function<void(int,bool)> OnKey;
    std::function<void(double,double,bool)> OnMouse;
};
std::unique_ptr<IWindow> CreateWin32Window();

class IRenderContext {
public:
    virtual ~IRenderContext() = default;
    virtual void Clear(double r, double g, double b) = 0;
    virtual void FillRect(double x, double y, double w, double h, uint32_t rgba) = 0;
    virtual void FillRoundedRect(double x, double y, double w, double h, double radius, uint32_t rgba) = 0;
    virtual void DrawLine(double x1, double y1, double x2, double y2, uint32_t rgba, double thickness = 1) = 0;
    virtual void FillCircle(double cx, double cy, double r, uint32_t rgba) = 0;
    virtual void DrawCircle(double cx, double cy, double r, uint32_t rgba, double thickness = 1) = 0;
    virtual void FillTriangle(double x1, double y1, double x2, double y2, double x3, double y3, uint32_t rgba) = 0;
    virtual void FillQuad(double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4, uint32_t rgba) = 0;
    virtual void Present(std::vector<uint32_t>& front) = 0;
    virtual int Width() const = 0; virtual int Height() const = 0;
};
std::unique_ptr<IRenderContext> CreateSoftwareRenderer(int w, int h);

class Input {
public:
    virtual ~Input() = default;
    virtual void OnKey(int keyCode, bool down) = 0;
    virtual void OnMouse(double x, double y, bool down) = 0;
    virtual bool GetKey(int vk) const = 0;
    virtual double GetAxis(const std::string& name) const = 0;
    virtual bool GetButton(const std::string& action) const = 0;
};

class IAudioBackend {
public:
    virtual ~IAudioBackend() = default;
    virtual bool Open(const std::string& path) = 0;
    virtual void Play() = 0; virtual void Pause() = 0;
    virtual void Seek(double ms) = 0;
    virtual double PositionMs() const = 0;
    virtual void SetTimescale(double) = 0;
};
std::unique_ptr<IAudioBackend> CreateWinMMBackend();

} // namespace Milestone::Platform
```

## 3. CMake 目标拆分（M1 Platform 层）

```cmake
add_library(ms_platform STATIC
    src/platform/win32_window.cpp
    src/platform/software_renderer.cpp
    src/platform/winmm_audio.cpp
    src/platform/input.cpp
)
target_link_libraries(ms_platform PRIVATE ms_core)
target_link_libraries(ms_platform PRIVATE user32 gdi32 winmm)
target_compile_definitions(ms_platform PRIVATE MS_WINDOWS)
```

## 4. 依赖方向（五层连贯）

Core（零依赖）← Platform（window/renderer/input/audio——仅 Core/标准库+Win32 API）← Domain（Rhythm——M4）← Editor（自绘——M3）← Tools（CLI）

## 5. M1 范围（细目后定案）

| 项 | 内容 | 依赖 |
|---|---|---|
| 窗口 | Win32Window（Create/Show/PumpOne/Present——StretchDIBits 帧呈现） | v1 端口 |
| 软件渲染器 | SoftwareRenderer（9 原语+Present——0B 热路径纪律延续） | v1 端口 |
| 输入 | Input（KeyInput/TouchInput/HitZone 端口+Axis/ActionMap 简） | D5 |
| 音频 | WinMM（mci 端口）+AudioClock（采样位置驱动） | t65 |
| parity 抽帧 | v1 对照（软渲帧一致性） | 工具链 |
| lazy-update | Transform worldDirty 懒更新（M0 摘记——M1 落地） | t110 diff |

## 6. M0 期间优化点记录（随手）

1. 调用开销：v1 委托每帧多次虚调用——v3 直调用+组件注册表（M1 生命周期 Tick 用注册表遍历——虚调用仅八回调 8 次/组件）；
2. lazy-update：Transform 每查重算 WorldMatrix（M0 简化）——M1 引入 dirty 位+层级传播（父变更→子树标记）；
3. 软渲热路径：v1 已验 0.42ms/帧（1000 帧）——v3 端口维持（FastBlend+SafeBlend 双路径）。

## 7. 待办

- [ ] eng-design-vis M1 设计细目（窗口/软渲/输入/音频/parity 抽帧/lazy-update 签名级）；
- [ ] 细目齐备后按本草案落地（接口+CMake 拆分+测试）。