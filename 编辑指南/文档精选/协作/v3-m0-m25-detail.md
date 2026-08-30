# v3 M0+M2.5 派单细目（M0 蓝本 + 绑定层 ms_bind）——eng-coder-vis 实施蓝本

> 起草：eng-design-vis（引擎策划）· V3-2..10 拍板（V3-B=C ABI+P/Invoke 适配层；域库=C++ 本体+C# 代理）后产出。
> 说明：M0 全量细节见 编辑指南/文档精选/协作/v3-m0-detail.md（布局/命名空间/API/ms_test/验收——本文件 §A 为摘要引用）；§B=**M2.5 绑定层细目**（本文件新增主体）。只做设计（规格/签名级）。

---

# §A M0 细目（摘要——以 v3-m0-detail.md 为准）

- **布局**：引擎源码/engine-v3/（CMake 顶层+include/milestone/core/*+src/core/*+tests/{ms_test,core}/*）。
- **命名空间**：Milestone::Core（全文件，无 C#/ChartPlayer 残留）。
- **Core API**：Vec3/Quat/Mat4·InstanceIds·Transform（层级+SetParent keepWorld+世界矩阵）·Component 八回调·GameObject（AddComponent/GetComponent/SetActive/Destroy 延迟）·Scene·LifecycleDriver（Unity 顺序分派+SequenceLog）·TimeSingleton·EventBus。
- **ms_test**：TEST_CASE/CHECK_*/REQUIRE+Runner（退出码=fails）。
- **验收 7 项**：结构/双链/生命周期顺序/树变换/时间事件/Runner/零第三方。

---

# §B M2.5 绑定层细目（C ABI + C# P/Invoke 适配）

## B1 绑定总览（双层数据流）

~~~
每帧（C++→C# 回调）：
  C++ LifecycleDriver ──Tick(dt)──→ 委托表（C# delegate 注册）────────────► C# 组件
      Awake/OnEnable/Start/Update/FixedUpdate/LateUpdate/OnDisable/OnDestroy    （ComponentBase 八回调）
  C++ EventBus Flush ──▶ C# 事件订阅者（EventBridge）
运行时（C#→C++ 命令）：
  C# GameEngine/LoadScene/AddComponent/Transform.get-set/Property→ ms_bind（P/Invoke）──▶ C++ Core
~~~

**约定**：①句柄=IntPtr（C++ 指针稳定）②InstanceId=C# long ↔ C++ uint64 ③字符串=UTF-8（MarshalAs）④事件回调=C# delegate→函数指针（维护 GCHandle 防 GC）⑤边界无托管 UI/依赖（C# 组装=纯托管壳）。

## B2 ms_bind.h 函数签名表（C ABI 稳定表）

~~~c
/* ms_bind.h —— M2.5 定稿（与 C# wrapper 一一对应） */
/* ---- 引擎生命周期 ---- */
void*   ms_engine_create(const char* title, int w, int h, void** outAssetStore);
int     ms_engine_tick(void* h, double dt, double* outFrameMs);      /* 触发生命周期回调表+事件 flush */
int     ms_engine_render(void* h, void* rctx);                       /* 渲染委托（C# 可挂 OnRender）*/
void    ms_engine_pump_input(void* h, int keyCode, int down, double mx, double my, int mdown);
void    ms_engine_destroy(void* h);
int     ms_engine_last_fps(void* h, double* outMs);                  /* 观测（PerfGate）*/

/* ---- 组件注册（C# 脚本类 → C++ 运行时）---- */
typedef void (*ms_cb)(void* ctx, double dt);                          /* Update 系列回调（带 dt）*/
typedef void (*ms_cb_void)(void* ctx);                                /* Awake/OnEnable/Start/OnDisable/OnDestroy */
typedef struct ms_component_spec {
  const char* scriptType;      /* C# 类型注册 key */
  ms_cb_void onAwake, onEnable, onStart, onDisable, onDestroy;
  ms_cb      onUpdate, onFixedUpdate, onLateUpdate;
  void*      userData;         /* C# 实例 GCHandle */
} ms_component_spec;
int  ms_component_register(ms_component_spec* spec);
int  ms_object_add_component(void* h, long instanceId, const char* scriptType);
int  ms_object_remove_component(void* h, long instanceId, const char* scriptType);

/* ---- Transform 访问 ---- */
int  ms_transform_get(void* h, long instanceId, const char* field, double* out3);   /* pos/rot(deg)/scale */
int  ms_transform_set(void* h, long instanceId, const char* field, const double* v3);
long ms_transform_parent(void* h, long instanceId);

/* ---- 属性（Inspector/脚本字段——注册表元数据）---- */
int ms_property_get(void* h, long id, const char* type, const char* prop, double* out, int* isString, char* outStr, int cap);
int ms_property_set(void* h, long id, const char* type, const char* prop, double v);
int ms_property_set_string(void* h, long id, const char* type, const char* prop, const char* s);

/* ---- 资产/场景 ---- */
int  ms_scene_load(void* h, const char* assetPath);                  /* JSON SceneAsset */
int  ms_scene_save(void* h, const char* assetPath);
long ms_asset_load_texture(void* h, const char* path, int* w, int* hgt);
void* ms_asset_load_audio(void* h, const char* path);               /* 后端句柄 */

/* ---- 事件桥 ---- */
int ms_event_emit(void* h, int eventId, const void* payload, int payloadBytes);
int ms_event_subscribe(void* h, int eventId, ms_cb_void cb, void* userData);

/* ---- 域库（Rhythm.* 代理，V3-10：C++ 本体 + C# 代理）---- */
int ms_rhythm_preset_run(const char* presetId, double* outAcc);
int ms_rhythm_scene_build(void* h, const char* presetId);
~~~

**六大块**：①生命周期回调调度=ms_engine_tick+组件注册表 ②组件注册=ms_component_* ③Transform 获取=ms_transform_* ④渲染委托=ms_engine_render ⑤事件桥=ms_event_* ⑥资产=ms_asset_*/ms_scene_*。

## B3 C# wrapper 类骨架（C# 开发层实现骨架）

~~~csharp
// GameEngine.cs —— 公开入口（开发者视角；纯 BCL，零 WinForms/System.Drawing）
namespace Milestone.Engine;
public sealed class GameEngine : IDisposable
{
    [DllImport("milestone_v3", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ms_engine_create(string title, int w, int h, out IntPtr assets);
    [DllImport("milestone_v3")] private static extern int ms_engine_tick(IntPtr h, double dt, out double frameMs);
    [DllImport("milestone_v3")] private static extern int ms_engine_render(IntPtr h, IntPtr rctx);
    [DllImport("milestone_v3")] private static extern void ms_engine_destroy(IntPtr h);
    [DllImport("milestone_v3")] private static extern int ms_scene_load(IntPtr h, string path);
    public string Title { get; set; } = "Milestone";
    public int Width { get; set; } = 1280;  public int Height { get; set; } = 720;
    private IntPtr _handle; private IntPtr _assets;
    public event Action<double>? OnFrame;
    public GameEngine LoadScene(IScene scene) { return this; }        // 场景根注册（ms 侧）
    public int Run() { /* create→loop(tick/pump/render)→destroy */ }
    public void Dispose() { }
    public AssetStore Assets => default;
}

// Scene.cs —— 场景接口 + 根对象
namespace Milestone.Engine;
public interface IScene { void Build(GameEngine engine); void Update(double dt); }
public sealed class Scene : IScene
{
    public GameObject Root { get; } = new("Root");
    public void Build(GameEngine engine) { }
    public void Update(double dt) { }
    public GameObject Add(string name) => Root.AddChild(name);
}

// ComponentBase.cs —— C# 侧八回调（绑定 C++ LifecycleDriver；子类覆写即可）
namespace Milestone.Engine;
public abstract class ComponentBase : IDisposable
{
    public virtual void Awake() { } public virtual void OnEnable() { } public virtual void Start() { }
    public virtual void Update(double dt) { } public virtual void FixedUpdate(double dt) { }
    public virtual void LateUpdate(double dt) { } public virtual void OnDisable() { }
    public virtual void OnDestroy() { }
    public GameObject Owner { get; internal set; } = null!;
    public bool Enabled { get; set; } = true;
    public void Dispose() { OnDestroy(); }
}

// GameObject.cs —— C# 代理（句柄+组件生命周期）
namespace Milestone.Engine;
public sealed class GameObject
{
    private readonly long _id;
    public string Name { get; set; } = "GameObject";
    public Transform Transform { get; }
    public T AddComponent<T>() where T : ComponentBase, new() { return default!; }   // new+注册（函数指针委托表）
    public T GetComponent<T>() where T : ComponentBase { return default!; }
    public void SetActive(bool v) { }
    public void Destroy() { }
    internal GameObject AddChild(string name) { return default!; }
}

// Transform.cs —— C# 代理（ms_transform_get/set）
namespace Milestone.Engine;
public readonly struct Transform
{
    public Vector3 Position { get => default; set { } }
    public Vector3 Rotation { get => default; set { } }
    public Vector3 Scale { get => default; set { } }
    public GameObject? Parent => null;
}
public readonly struct Vector3 { public double X, Y, Z; }
~~~

**C# 侧注意**：①委托注册用 [UnmanagedCallersOnly] 静态方法（.NET 8 函数指针）+GCHandle 防 GC；②字符串 UTF-8；③零托管依赖（纯 BCL）；④域库代理（Milestone.Rhythm.*：ms_rhythm_preset_run/build 薄壳）。

## B4 分层测试（V3-B）

| 层 | 测试 | 断言 |
|---|---|---|
| C++（Core） | ms_test（§A） | 生命周期顺序/树/时间——零第三方 |
| C# 绑定集成 | 自研 CSTestRunner（控制台+断言宏同 ms_test 风格——零依赖） | ①GameEngine 创建/销毁（句柄非空/无泄漏）②组件注册→生命周期回调顺序==Unity 官方 ③Transform get/set round-trip ④场景保存/加载 round-trip ⑤事件桥订阅-发射 |
| 联合 | 冒烟（C# 侧脚本建场景→渲染 1 帧非空，退出 0）——双层同验收 | 冒烟通过 |

---

## C 实施顺序与验收

| 序 | 项 | 验收 | 依赖 |
|---|---|---|---|
| 1 | M0（§A） | 7 项清单 | —（t108 已派） |
| 2 | M2.5 绑定层 | ms_bind 头+六大块签名+C# wrapper 骨架+分层测试全绿（C# 生命周期顺序断言） | M0+（M2 资产可后补——运行时场景占位先行） |
| 3 | M3 编辑器 | 六区+C# 扩展脚本（[CustomInspector]/[MenuItem] 经 ms_bind 注册） | M2.5 |
| 4 | M4 域库 | Rhythm.* C++ 本体+ms_rhythm_* 代理+示例 | M2.5 |

**备注**：M2.5 与 M2 可并行（绑定层场景/资产=运行时占位→M2 补 JSON；生命周期不依赖资产——先行）。

---

*M0+M2.5 细目完毕*（M0=§A 摘要+引用；M2.5=ms_bind 表/六大块/C# wrapper 骨架/分层测试——发 eng-coder-vis 对齐 t108 与 M2.5 派单）
