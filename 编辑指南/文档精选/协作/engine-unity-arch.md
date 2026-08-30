# Milestone 引擎 Unity 式架构契约（design spec）

> 目的：把现有 `MilestoneEngine`（纯 C# 音游引擎库）升级为 Unity 式运行时——新增
> `GameObject / Component / Scene / SceneManager / GameLoop` 场景运行时、可组合搭建四大玩法族的
> `Ruleset`、引擎驱动的场景 UI（重做 UI）、引擎侧时序/缓动 + 宿主合成器渲染的 `SceneTransition`，
> 并把新抽象接到 `D2DRenderer`（`D2DDrawAdapter`）与 `SceneCompositor`，通过构建与自检。
>
> 本文档是**可执行设计契约**：每个类都给"精确签名 + 生命周期语义 + 自检断言"，工程师照抄即能落地。
>
> - 目标工程布局（UTF-8 / LF）：引擎库 = `引擎\engine\`，宿主 = `源码\`，文档 = `其他\docs\协作\`。
> - 命名空间：引擎库一律 `ChartPlayer`（与现有引擎一致），宿主沿用 `ChartPlayer`。

---

## 0. 设计原则与硬约束

1. **引擎库零包依赖**：`引擎\engine\MilestoneEngine.csproj` 只允许 `net8.0-windows` + 纯 C#，
   **禁止** `using System.Drawing;`、`using System.Windows.Forms;`、`using SharpDX.*;`、任何 `PackageReference`。
   《当前 `MilestoneEngine.csproj` 零包引用、`<Compile Remove>` 掉 Samples/Tests/HelloEngine，必须保持》.
2. **颜色一律 `RgbaColor`**（引擎自有，见 `Particles.cs`），引擎内出现 `System.Drawing.Color` 即违规。
3. **引擎只算"逻辑/时序/几何/判定/布局/缓动"，渲染由宿主读参数画**。引擎可无头（headless）推进与自检，不依赖窗口/GPU。
4. **向量/矩阵复用现有**：`Vec2`、`Vec3`、`Mat4`、`Easing`、`Bezier`（`Engine3D.cs`），`Transform`、`Tween`（现有）。
5. **场景对象树复用现有 `Transform` 父子层级**：`GameObject` 持有一个 `Transform`，用其 `Parent/Children/SetParent/AddChild/RemoveChild` 组织层级与依赖。
6. 新增抽象全部提供**可无头自检的断言入口**（沿用 `DemoRunner.Assert(bool, string)` 风格：失败抛 `InvalidOperationException`）。

---

## 1. 运行时 API：GameObject / Component / Scene / SceneManager / GameLoop

> 这一层是"Unity 式场景运行时"的地基。引擎库新增文件 `引擎\engine\GameObject.cs`、
> `引擎\engine\Scene.cs`、`引擎\engine\GameLoop.cs`（都 `namespace ChartPlayer`，零包依赖）。

### 1.1 生命周期方法语义与调用顺序（完整契约表）

| 方法 | 触发时机 | 是否受 `Enabled`/`ActiveInHierarchy` 影响 | 次数 |
|---|---|---|---|
| `Awake()` | 组件被加入 GameObject / 对象被实例化（场景加载）时 | 无关——**即使 disabled 也调用** | 一次 |
| `OnEnable()` | 对象被激活且组件 `Enabled==true` 时 | 需要 `Enabled && ActiveInHierarchy` | 每次 `false→true` |
| `Start()` | 首次 `Update` **之前**的那一帧，且组件此时有效 | 需要 enabled | 一次 |
| `Update()` | 每帧，组件有效（enabled 且在激活层级） | 需要 enabled | 每帧 |
| `LateUpdate()` | 每帧在所有对象的 `Update` 之后，组件有效 | 需要 enabled | 每帧 |
| `OnDisable()` | 对象失活 / 组件 `Enabled=false`（含销毁前） | 无关 | 每次 `true→false` |
| `OnDestroy()` | 对象/组件被 `Destroy()` 或场景卸载 | 无关 | 一次 |

**引擎内确定性遍历顺序（Unity 不保证顺序，本引擎给出确定性顺序以便自检）**：

- 树遍历采用**前序（父先于子）**：`Awake → OnEnable`，`Start`，每帧 `Update → LateUpdate`。
- 同帧全局序：`Awake(全部) → OnEnable(全部) → Start(全部，仅首帧) → Update(全部) → LateUpdate(全部)`。
- 失活/销毁序：`OnDisable(全部) → OnDestroy(全部)`。

> 引擎对组件调用生命周期的规则（严格一致）：
> 1. `Awake` 在 `GameObject.AddComponent` / 场景加载实例化时**立即**调用（无关 enabled）。
> 2. `OnEnable` 在对象 `ActiveInHierarchy=true` **且** `Component.Enabled=true` 时调用。
> 3. `Start` 只在**首次进入 Update 前**，若此刻组件已启用才调用，且只一次；
>    若对象一开始失活，则**延迟到激活后的首个 Update 前**调用。
> 4. `Update/LateUpdate` 每帧、仅当组件已启用。
> 5. `OnDisable` 在 `ActiveInHierarchy` 变 false 或 `Enabled` 变 false 时调用。
> 6. `OnDestroy` 在 `Destroy()` 时调用（此时不论 enabled）。

### 1.2 `Component` 精确签名（`GameObject.cs`）

```csharp
namespace ChartPlayer
{
    /// <summary>Unity 式组件基类：挂在 GameObject 上，接收生命周期回调。
    /// 引擎只驱动回调；子类实现业务逻辑。零包依赖。</summary>
    public abstract class Component
    {
        /// <summary>所属 GameObject（由引擎设置为 non-null）。</summary>
        public GameObject GameObject { get; internal set; }

        /// <summary>快捷访问持有者的 Transform（场景树的层级/位置）。</summary>
        public Transform Transform => GameObject?.Transform;

        bool _enabled = true;

        /// <summary>是否启用。false 时不再接收 Start/Update/LateUpdate，但 Awake 仍会；关闭时触发 OnDisable。</summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                RefreshEnable();   // 由 IsActiveAndEnabled 判断是否需要触发 OnEnable/OnDisable
            }
        }

        /// <summary>是否"生效"：自身 enabled 且所在对象在层级中激活。</summary>
        public bool IsActiveAndEnabled => Enabled && GameObject != null && GameObject.ActiveInHierarchy;

        // ---------- 生命周期回调（子类 override） ----------
        protected virtual void Awake() { }
        protected virtual void OnEnable() { }
        protected virtual void Start() { }
        protected virtual void Update() { }
        protected virtual void LateUpdate() { }
        protected virtual void OnDisable() { }
        protected virtual void OnDestroy() { }

        // ---------- 引擎内部驱动状态（子类不碰） ----------
        internal bool _awakened, _started;
        bool _effEnabled;   // 上一次已触发 OnEnable 的有效状态（true ⇔ 当前处于启用态）

        // 过渡驱动：只有"已启用 ↔ 未启用"真实翻转时才触发 OnEnable/OnDisable，避免重复触发。
        internal void RefreshEnable()
        {
            bool eff = IsActiveAndEnabled;
            if (eff && !_effEnabled) { _effEnabled = true; OnEnable(); }
            else if (!eff && _effEnabled) { _effEnabled = false; OnDisable(); }
        }

        internal void InternalAwake() { if (_awakened) return; _awakened = true; Awake(); }
        internal void InternalStart() { if (_started) return; _started = true; Start(); }
        internal void InternalUpdate() { if (IsActiveAndEnabled) Update(); }
        internal void InternalLateUpdate() { if (IsActiveAndEnabled) LateUpdate(); }
        internal void InternalOnDestroy() { if (_awakened) { RefreshEnable(); OnDestroy(); } }

        // ---------- 便捷查询 ----------
        public T GetComponent<T>() where T : Component => GameObject?.GetComponent<T>();
        public bool TryGetComponent<T>(out T component) where T : Component
        {
            component = GameObject != null ? GameObject.GetComponent<T>() : null;
            return component != null;
        }
    }
}
```

### 1.3 `GameObject` 精确签名（`GameObject.cs`）

```csharp
namespace ChartPlayer
{
    /// <summary>Unity 式场景对象：持有 Transform + 一组 Component + 子对象。
    /// 通过 SceneManager/GameLoop 驱动生命周期。零包依赖。</summary>
    public sealed class GameObject
    {
        readonly List<Component> _components = new List<Component>();
        readonly List<GameObject> _children = new List<GameObject>();
        GameObject _parent;
        bool _active = true;
        bool _awakened, _started, _destroyed;

        public GameObject() : this("GameObject") { }
        public GameObject(string name) { Name = name; }

        /// <summary>显示名（编辑器/调试用）。</summary>
        public string Name { get; set; }

        /// <summary>场景树变换（父/子 World/Local 位置、旋转、缩放）。</summary>
        public Transform Transform { get; } = new Transform();

        // ---------- 激活 ----------
        /// <summary>自身是否激活。false 时整棵激活子树失活（触发 OnDisable）。</summary>
        public bool Active { get => _active; set => SetActive(value); }

        /// <summary>是否在层级中激活（= 自身激活 && 祖先全部激活）。</summary>
        public bool ActiveInHierarchy => _active && (_parent == null || _parent.ActiveInHierarchy);

        public GameObject Parent => _parent;
        public IReadOnlyList<GameObject> Children => _children;

        /// <summary>设置激活；切换状态会级联触发子树组件的 OnEnable/OnDisable。</summary>
        public void SetActive(bool active)
        {
            if (_active == active) return;
            _active = active;
            if (active) { InternalAwake(); InternalEnable(); if (_started) InternalStart(); }
            else InternalDisable();
        }

        // ---------- 组件管理 ----------
        /// <summary>添加组件并立即调用其 Awake（若对象已激活则随后 OnEnable）。</summary>
        public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));

        public Component AddComponent(Type componentType)
        {
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                throw new ArgumentException($"{componentType?.Name} 不是 Component");
            var c = (Component)Activator.CreateInstance(componentType);
            c.GameObject = this;
            _components.Add(c);
            c.InternalAwake();
            c.RefreshEnable();   // 对象激活则触发 OnEnable；未激活只记录状态
            return c;
        }

        public T GetComponent<T>() where T : Component
        {
            foreach (var c in _components) if (c is T t) return t;
            return null;
        }

        public Component[] GetComponents() => _components.ToArray();

        /// <summary>在自身及后代中查找首个指定类型组件。</summary>
        public T GetComponentInChildren<T>() where T : Component
        {
            var c = GetComponent<T>();
            if (c != null) return c;
            foreach (var ch in _children) { c = ch.GetComponentInChildren<T>(); if (c != null) return c; }
            return null;
        }

        // ---------- 层级 ----------
        public void SetParent(GameObject parent, bool keepWorld = false)
        {
            _parent?._children.Remove(this);
            _parent = parent;
            if (parent != null && !parent._children.Contains(this)) parent._children.Add(this);
            if (keepWorld) Transform.SetParent(parent?.Transform, true);
            else Transform.SetParent(parent?.Transform, false);
        }

        public void AddChild(GameObject child) => child?.SetParent(this, true);
        public void RemoveChild(GameObject child)
        {
            if (child != null && child._parent == this) child.SetParent(null, true);
        }

        // ---------- 销毁 ----------
        /// <summary>销毁对象：级联 OnDisable + OnDestroy 并移除根引用（由场景持有）。</summary>
        public void Destroy() => InternalDestroy();

        internal void InternalDestroy()
        {
            if (_destroyed) return;
            _destroyed = true;
            InternalDisable();
            foreach (var c in _components.ToArray()) c.InternalOnDestroy();
            foreach (var ch in _children.ToArray()) ch.InternalDestroy();
            _components.Clear();
            _children.Clear();
            _parent?._children.Remove(this);
            if (SceneRef != null) SceneRef.NotifyRootDestroyed(this);
        }

        // ---------- 引擎内部生命周期驱动（Scene 调用；子类/宿主不调用） ----------
        internal void InternalAwake()
        {
            if (_awakened) return;
            _awakened = true;
            foreach (var c in _components) c.InternalAwake();
            foreach (var ch in _children) ch.InternalAwake();
        }

        internal void InternalEnable()
        {
            foreach (var c in _components) c.RefreshEnable();
            foreach (var ch in _children) ch.InternalEnable();
        }

        internal void InternalStart()
        {
            if (_started) return;
            _started = true;
            foreach (var c in _components) if (c.IsActiveAndEnabled) c.InternalStart();
            foreach (var ch in _children) ch.InternalStart();
        }

        internal void InternalUpdate()
        {
            if (!_active) return;
            foreach (var c in _components) c.InternalUpdate();
            foreach (var ch in _children) ch.InternalUpdate();
        }

        internal void InternalLateUpdate()
        {
            if (!_active) return;
            foreach (var c in _components) c.InternalLateUpdate();
            foreach (var ch in _children) ch.InternalLateUpdate();
        }

        internal void InternalDisable()
        {
            foreach (var c in _components) c.RefreshEnable();
            foreach (var ch in _children) ch.InternalDisable();
        }

        /// <summary>场景引用（Scene 设置），用于根对象销毁通知。</summary>
        public Scene SceneRef { get; internal set; }

        public override string ToString() => $"{Name} ({_components.Count} components)";
    }
}
```

> 说明：引擎用"过渡驱动"（`Component.RefreshEnable`）触发 `OnEnable/OnDisable`——只有
> "已启用 ↔ 未启用"（受 `Enabled` 与 `ActiveInHierarchy` 共同决定）真实翻转时才回调，且 `Awake` 只一次、
> `Start` 只在首次激活后调用一次，"失活→激活"只补 `OnEnable`，避免重复触发。`Component.Enabled` 赋值与
> 对象 `SetActive` 都会经由各组件 `RefreshEnable` 精确触发对应回调。

### 1.4 `Scene` 精确签名（`Scene.cs`）

```csharp
namespace ChartPlayer
{
    /// <summary>场景：一组根对象（各自可带子树）。负责驱动生命周期与逐帧 Update/LateUpdate。
    /// 可被 SceneManager 加载/卸载；也可独立无头 Step（自检）。</summary>
    public sealed class Scene
    {
        readonly List<GameObject> _roots = new List<GameObject>();
        bool _awakened, _started;

        public Scene(string name) { Name = name; }
        public string Name { get; }

        public IReadOnlyList<GameObject> Roots => _roots;

        /// <summary>创建并挂入场景根的空白对象。</summary>
        public GameObject CreateRoot(string name = "GameObject")
        {
            var go = new GameObject(name) { SceneRef = this };
            _roots.Add(go);
            return go;
        }

        /// <summary>把已有对象挂为根（若在别的场景会先脱离）。</summary>
        public void AddRoot(GameObject go)
        {
            if (go == null) return;
            go.SceneRef = this;
            _roots.Add(go);
        }

        public void RemoveRoot(GameObject go) { _roots.Remove(go); }

        /// <summary>卸载场景：级联销毁全部根对象。</summary>
        public void Unload()
        {
            foreach (var r in _roots.ToArray()) r.InternalDestroy();
            _roots.Clear();
            _awakened = _started = false;
        }

        // ---------- 生命周期驱动（SceneManager / GameLoop 调用） ----------
        internal void Load()
        {
            if (_awakened) return;
            foreach (var r in _roots) r.InternalAwake();
            foreach (var r in _roots) r.InternalEnable();
            _awakened = true;
        }

        internal void StartOnce()
        {
            if (_started) return;
            foreach (var r in _roots) r.InternalStart();
            _started = true;
        }

        /// <summary>推进一帧：先 StartOnce（首帧），再 Update，再 LateUpdate。</summary>
        internal void Step(double nowMs, double deltaSeconds)
        {
            StartOnce();
            foreach (var r in _roots) r.InternalUpdate();
            foreach (var r in _roots) r.InternalLateUpdate();
        }

        internal void NotifyRootDestroyed(GameObject go) => _roots.Remove(go);
    }
}
```

### 1.5 `SceneManager` 精确签名（`Scene.cs`）

```csharp
namespace ChartPlayer
{
    /// <summary>场景管理器：持有当前场景、加载/卸载、全局生命周期回调。静态工具类（Unity 式）。</summary>
    public static class SceneManager
    {
        static Scene _current;

        /// <summary>当前加载的场景（无则 null）。</summary>
        public static Scene Current => _current;

        /// <summary>新建一个（未加载的）场景，需 LoadScene 才进入生命周期。</summary>
        public static Scene CreateScene(string name) => new Scene(name);

        /// <summary>加载场景：卸载旧的（可选）、对新的调用 Load()（Awake+OnEnable）。</summary>
        public static bool LoadScene(Scene scene, bool unloadCurrent = true)
        {
            if (scene == null) return false;
            if (unloadCurrent && _current != null && _current != scene) _current.Unload();
            _current = scene;
            _current.Load();
            SceneLoaded?.Invoke(scene);
            return true;
        }

        /// <summary>卸载当前场景并置空。</summary>
        public static void UnloadCurrent() { _current?.Unload(); _current = null; }

        /// <summary>由 GameLoop 每帧调用：推进当前场景一帧（先 StartOnce 再 Update/LateUpdate）。</summary>
        public static void Tick(double nowMs, double deltaSeconds) => _current?.Step(nowMs, deltaSeconds);

        public static event Action<Scene> SceneLoaded;
        public static event Action<Scene> SceneUnloaded;
    }
}
```

### 1.6 `GameLoop` 精确签名（`GameLoop.cs`）

```csharp
namespace ChartPlayer
{
    /// <summary>无头游戏循环：持有时钟 + 当前场景，宿主每帧调用 Tick(realDeltaSeconds)。
    /// 可被 WinForms 定时器、消息循环、或纯测试循环驱动（自检即用纯循环）。</summary>
    public sealed class GameLoop
    {
        public EngineTime Clock { get; } = new EngineTime();
        public Scene CurrentScene => SceneManager.Current;

        /// <summary>每次 Tick 后触发（传 realDeltaSeconds 秒），宿主可用于同步渲染。</summary>
        public event Action<double> OnFrame;

        /// <summary>游戏时间（毫秒，供判定类组件）。</summary>
        public double GameTimeMs => Clock.GameTime * 1000.0;

        /// <summary>推进一帧：推进时钟 → StartOnce/Update/LateUpdate → 触发 OnFrame。</summary>
        public void Tick(double realDeltaSeconds)
        {
            Clock.Tick(Math.Max(0, realDeltaSeconds));
            SceneManager.Tick(GameTimeMs, Clock.GameDelta);
            OnFrame?.Invoke(Math.Max(0, realDeltaSeconds));
        }

        public void LoadScene(Scene scene) => SceneManager.LoadScene(scene);

        public void Reset() { Clock.Reset(); SceneManager.UnloadCurrent(); }
    }
}
```

> 为什么 `GameLoop` 放引擎：它不碰任何渲染/窗口依赖，只是"时钟 + 场景推进"的驱动；宿主拿它接
> WinForms 定时器即可跑真实游戏，也可在纯 `for` 循环中跑自检。

### 1.7 运行时自检断言（`引擎\engine\Tests\EngineChecks\` 或 `Samples\DemoRunner.cs` 追加）

```csharp
// 断言顺序：Awake→OnEnable→Start→Update→LateUpdate；失活触发 OnDisable；销毁触发 OnDestroy。
sealed class OrderProbe : Component
{
    public readonly List<string> Log = new List<string>();
    protected override void Awake() => Log.Add("Awake");
    protected override void OnEnable() => Log.Add("OnEnable");
    protected override void Start() => Log.Add("Start");
    protected override void Update() => Log.Add("Update");
    protected override void LateUpdate() => Log.Add("LateUpdate");
    protected override void OnDisable() => Log.Add("OnDisable");
    protected override void OnDestroy() => Log.Add("OnDestroy");
}

static string GameLoopCheck()
{
    var scene = SceneManager.CreateScene("t");
    var go = scene.CreateRoot("root");
    var probe = go.AddComponent<OrderProbe>();
    SceneManager.LoadScene(scene);                 // Awake + OnEnable
    Assert(string.Join(",", probe.Log) == "Awake,OnEnable", "首帧前顺序 Awake,OnEnable");

    var loop = new GameLoop();
    loop.LoadScene(scene);
    loop.Tick(1.0 / 60.0);                          // 首帧：Start + Update + LateUpdate
    Assert(string.Join(",", probe.Log) == "Awake,OnEnable,Start,Update,LateUpdate", "首帧顺序含 Start 在前");
    loop.Tick(1.0 / 60.0);
    Assert(string.Join(",", probe.Log) == "Awake,OnEnable,Start,Update,LateUpdate,Update,LateUpdate", "后续帧 Update,LateUpdate");

    go.SetActive(false);                            // 触发 OnDisable
    Assert(probe.Log[probe.Log.Count - 1] == "OnDisable", "失活触发 OnDisable");
    go.SetActive(true);                             // 再补 OnEnable（不重复 Awake/Start）
    Assert(probe.Log[probe.Log.Count - 1] == "OnEnable", "重新激活补 OnEnable");

    go.Destroy();                                   // OnDestroy
    Assert(probe.Log[probe.Log.Count - 1] == "OnDestroy", "销毁触发 OnDestroy");
    return "[GameLoop 生命周期] Awake→OnEnable→Start→Update→LateUpdate→OnDisable→OnEnable→OnDestroy 通过\n";
}
```

---

## 2. Ruleset 组合模型（可组合搭建四大玩法族）

> 目标：用引擎既有模块**无头组合**出四个玩法族（固定下落 / 自由线场 / 环形触摸 / 路径轨道），
> 每个规则集给出"组合清单 + 可无头自检的判定闭环断言"。
> 新增文件 `引擎\engine\Ruleset.cs`（`abstract Ruleset` + `ChartContext` + 4 个子类，零包依赖）。

### 2.1 `ChartContext` 精确签名

```csharp
namespace ChartPlayer
{
    /// <summary>一局游玩的共享上下文：谱面 + 判定配置 + 结算板 + 输入源 + 时钟 + 判定事件。
    /// 由宿主一次性构造并传给 Ruleset。</summary>
    public sealed class ChartContext
    {
        public ChartData Chart;
        public JudgementProfile Profile;
        public ScoreBoard Board;
        public EngineTime Clock;
        public KeyInput Keys = new KeyInput();          // 下落式用
        public TouchInput Touch = new TouchInput();     // 触摸式用

        public void Prepare() { Chart?.SortNotes(); }

        public event Action<Judgement> Judged;
        public event Action<RhythmNote, Judgement> Hit;
        public event Action<RhythmNote> Miss;

        internal void Raise(Judgement j, RhythmNote n)
        {
            Judged?.Invoke(j);
            if (j.IsHit) Hit?.Invoke(n, j); else Miss?.Invoke(n);
        }
    }
}
```

### 2.2 `abstract Ruleset` 精确签名

```csharp
namespace ChartPlayer
{
    /// <summary>玩法族规则集抽象：组合"场 + 输入映射 + 判定追踪 + 结算"成一个可运行游玩闭环。
    /// 子类只需实现场/输入如何映射到音符判定。引擎侧无头推进，宿主可持渲染命令。</summary>
    public abstract class Ruleset
    {
        public ChartContext Context { get; protected set; }
        public JudgementTracker Tracker { get; protected set; }

        public JudgementProfile Profile => Context.Profile;
        public ChartData Chart => Context.Chart;
        public ScoreBoard Board => Context.Board;

        /// <summary>用给定上下文配料后加载谱面：建 Tracker、Prepared、Reset。</summary>
        public virtual void Start(ChartContext ctx)
        {
            Context = ctx;
            Tracker = new JudgementTracker(ctx.Chart, ctx.Profile, ctx.Board);
        }

        /// <summary>无头推进：调用 Tracker.Update(nowMs) 处理漏判（过 MISS 窗口自动 MISS）。</summary>
        public virtual void Update(double nowMs) => Tracker?.Update(nowMs);

        /// <summary>喂入一个输入事件（宿主触控/按键，或自检脚本）；返回本事件产生的判定列表。</summary>
        public abstract IReadOnlyList<Judgement> Feed(double nowMs, InputEvent evt);

        /// <summary>获取当前需要绘制的音符（宿主把 RenderNote 投影到屏幕/世界坐标绘制）。</summary>
        public abstract IEnumerable<RenderNote> Present(double nowMs);

        /// <summary>重新游玩同一谱面。</summary>
        public virtual void Reset() { Tracker?.Reset(); }

        public int JudgedCount => Tracker?.JudgedCount ?? 0;
        public int Remaining => Tracker?.Remaining ?? 0;
    }
}
```

### 2.3 判定闭环通用输入与渲染载体

```csharp
namespace ChartPlayer
{
    /// <summary>统一输入事件：一套按键或触点。键类用 KeyCode>0、触点类用 TouchId>=0。</summary>
    public readonly struct InputEvent
    {
        public readonly int KeyCode;
        public readonly int TouchId;
        public readonly Vec2 TouchPos;
        public readonly bool Pressed;     // true=刚按下
        public InputEvent(int keyCode, int touchId, Vec2 pos, bool pressed)
        { KeyCode = keyCode; TouchId = touchId; TouchPos = pos; Pressed = pressed; }

        public static InputEvent Key(int keyCode, bool pressed) => new InputEvent(keyCode, -1, default, pressed);
        public static InputEvent Touch(int id, Vec2 pos, bool pressed) => new InputEvent(-1, id, pos, pressed);
    }

    /// <summary>渲染载体（引擎中立）：宿主读取后投影/绘制。</summary>
    public readonly struct RenderNote
    {
        public readonly double X, Y;
        public readonly double Size;
        public readonly RgbaColor Color;
        public readonly int Lane;             // -1 = 自由场
        public readonly double Progress;      // 0..1 滚动/滑星进度
        public readonly double Alpha;
        public readonly string Type;
        public RenderNote(double x, double y, double size, RgbaColor color,
                          int lane, double progress, double alpha, string type)
        { X=x; Y=y; Size=size; Color=color; Lane=lane; Progress=progress; Alpha=alpha; Type=type; }
    }
}
```

### 2.4 `LaneRuleset`（固定下落）——`LaneField + TimeDepthMapper + KeyInput + PressLane`

```csharp
namespace ChartPlayer
{
    /// <summary>固定下落式（mania/IIDX/taiko/catch/Arcaea 地面）：列 → x 映射 + 时间深度滚动 + 按键映射 + 追踪器。</summary>
    public sealed class LaneRuleset : Ruleset
    {
        public LaneField Field = new LaneField(4, 100);
        public TimeDepthMapper Mapper = TimeDepthMapper.FromSpeed(0.3, 1.0);
        public IReadOnlyDictionary<int, int> KeyMap;   // 键码 → 列；null 则按列号当键码

        public override IReadOnlyList<Judgement> Feed(double nowMs, InputEvent evt)
        {
            var list = new List<Judgement>();
            if (!evt.Pressed || evt.KeyCode < 0) return list;
            int lane = KeyMap != null && KeyMap.TryGetValue(evt.KeyCode, out int l) ? l
                      : (int)evt.KeyCode;
            var j = Tracker.PressLane(lane, nowMs);
            if (j.HasValue) list.Add(j.Value);
            return list;
        }

        public override IEnumerable<RenderNote> Present(double nowMs)
        {
            foreach (var n in Chart.Notes)
            {
                double remain = n.TimeMs - nowMs;
                double depth = Mapper.DepthOfRemain(remain);
                double x = Field.LaneCenter(n.Lane);
                yield return new RenderNote(x, depth * 1000, 48, RgbaColor.Cyan,
                     n.Lane < 0 ? -1 : (int)n.Lane, depth, depth > 0.02 ? 1 : 0, n.Type);
            }
        }
    }
}
```

**组合清单（无头判定闭环）**：

| 环节 | 引擎模块 | 作用 |
|---|---|---|
| 列→x | `LaneField`（`LaneCenter/XToLane/ClampLane`） | 轨道定位 |
| 时间深度 | `TimeDepthMapper.DepthOfRemain` | 滚动（近大远小） |
| 输入 | `KeyInput` + `InputMapper.KeyToLane` / `TouchToLane` | 键/触点 → 列 |
| 判定 | `JudgementTracker.PressLane` | 命中最早未判定音符 |
| 漏判 | `JudgementTracker.Update` | 过 MISS 窗口自动 MISS |
| 结算 | `ScoreBoard.Apply`（Tracker 自动写入） | 分数/连击/ACC/评级 |

**可无头自检断言（`LaneRulesetCheck`）**：

```csharp
static string LaneRulesetCheck()
{
    var chart = new ChartData();
    for (int i = 0; i < 4; i++) chart.Notes.Add(new RhythmNote(1000 + i * 500) { Lane = i });
    var ctx = new ChartContext { Chart = chart, Profile = JudgementProfile.OsuMania(8),
                                 Board = new ScoreBoard(300, 4) };
    ctx.Prepare();
    var rs = new LaneRuleset();
    rs.Start(ctx);
    for (int i = 0; i < 4; i++)
    {
        var j = rs.Feed(1000 + i * 500, InputEvent.Key(90 + i, true));
        Assert(j.Count == 1 && j[0].IsHit, $"LaneRuleset 列{i} 应命中");
    }
    Assert(ctx.Board.HitCount == 4 && ctx.Board.MissCount == 0, "LaneRuleset 4 命中 0 漏");
    return "[LaneRuleset] 列映射+按键判定闭环 通过\n";
}
```

### 2.5 `LineRuleset`（自由线场）——`LineField + PlayfieldLine + TouchInput + PressField`

```csharp
namespace ChartPlayer
{
    /// <summary>自由线场（Phigros/Cytus/Deemo/osu 自由场）：多条判定线 + 自由坐标音符 + 触摸半径判定。</summary>
    public sealed class LineRuleset : Ruleset
    {
        public LineField Field = new LineField(500, 500);
        public double HitRadius = 50;

        public override IReadOnlyList<Judgement> Feed(double nowMs, InputEvent evt)
        {
            var list = new List<Judgement>();
            if (evt.TouchId < 0 || !evt.Pressed) return list;
            var j = Tracker.PressField(evt.TouchPos, Field, HitRadius, nowMs);
            if (j.HasValue) list.Add(j.Value);
            return list;
        }

        public override IEnumerable<RenderNote> Present(double nowMs)
        {
            foreach (var n in Chart.Notes)
            {
                var p = Field.Denormalize(new Vec2(n.X, n.Y));
                yield return new RenderNote(p.X, p.Y, 44, RgbaColor.Yellow, -1, 1, 1, n.Type);
            }
        }
    }
}
```

**组合清单**：`LineField`（`Normalize/Denormalize/NearestLine/InHitBand`）+ `PlayfieldLine`
（`Transform`/`Normal`/`DistanceTo`/`Alpha`/`Visible`）+ `TouchInput` + `JudgementTracker.PressField`
（`TouchToLine`）。判定闭环 = 触摸坐标 → `PlayfieldLine.DistanceTo`（在命中带内）+ `PressField`。

**自检断言**：中心点命中、场外不中、已判定忽略（沿用 `DemoRunner` 自由场同款）。

### 2.6 `RingRuleset`（环形触摸）——`RingField + TouchInput + PressRing`

```csharp
namespace ChartPlayer
{
    /// <summary>环形触摸（maimai/Lanota/CHUNITHM/WACCA）：方位角定位 + 触摸命中最近扇形。</summary>
    public sealed class RingRuleset : Ruleset
    {
        public RingField Ring = new RingField(new Vec2(0, 0), 400, 8);

        public override IReadOnlyList<Judgement> Feed(double nowMs, InputEvent evt)
        {
            var list = new List<Judgement>();
            if (evt.TouchId < 0 || !evt.Pressed) return list;
            var j = Tracker.PressRing(evt.TouchPos, Ring, nowMs);
            if (j.HasValue) list.Add(j.Value);
            return list;
        }

        public override IEnumerable<RenderNote> Present(double nowMs)
        {
            foreach (var n in Chart.Notes)
            {
                if (n is RingNote rn)
                {
                    var p = rn.WorldPoint(rn.StartAngle, Ring);
                    double prog = rn.Slide ? rn.SlideProgress(rn.StartAngle, Ring) : 1;
                    yield return new RenderNote(p.X, p.Y, 40, RgbaColor.Red, -1, prog, 1, "ring");
                }
            }
        }
    }
}
```

**组合清单**：`RingField`（`SectorOf/AngleToSector/SectorPoint/ClampSector`）+ `TouchInput` +
`InputMapper.TouchToSector` + `JudgementTracker.PressRing` + `RingNote.SlideProgress`。

**自检断言**：方位 0/2/4 tap 命中、滑星进度、方位容差、漏判（`DemoRulesets.MaimaiDemo` 同款）。

### 2.7 `PathRuleset`（路径轨道）——`PathField + 每拍按键 + PressLane(轨道号)`

```csharp
namespace ChartPlayer
{
    /// <summary>路径轨道（ADOFAI 双球 / Groove Coaster / osu slider 轨道）：沿样条(s)滚动，
    /// 每拍按键；lane = 轨道号（多轨各走一条 PathField），走距由时间×速度映射。</summary>
    public sealed class PathRuleset : Ruleset
    {
        public PathField[] Tracks;          // 多轨，索引即 lane
        public double Speed = 0.2;          // 单位/ms（一拍一格等）
        public IReadOnlyDictionary<int, int> KeyMap;

        public override IReadOnlyList<Judgement> Feed(double nowMs, InputEvent evt)
        {
            var list = new List<Judgement>();
            if (!evt.Pressed || evt.KeyCode < 0) return list;
            int lane = KeyMap != null && KeyMap.TryGetValue(evt.KeyCode, out int l) ? l : (int)evt.KeyCode;
            var j = Tracker.PressLane(lane, nowMs);
            if (j.HasValue) list.Add(j.Value);
            return list;
        }

        public Vec3 TrackPointAt(int lane, double nowMs)
            => Tracks[lane].PositionAt(Tracks[lane].DistanceOf(nowMs, Speed));

        public override IEnumerable<RenderNote> Present(double nowMs)
        {
            foreach (var n in Chart.Notes)
            {
                int lane = (int)Math.Max(0, n.Lane);
                var p = Tracks[lane].PositionAt(Tracks[lane].DistanceOf(n.TimeMs - nowMs, Speed));
                yield return new RenderNote(p.X, p.Y, 40, RgbaColor.Green, lane, 1, 1, "tile");
            }
        }
    }
}
```

**组合清单**：`PathField`（`PositionAt/TangentAt/DistanceOf/NearestS/NormalizeS`，多轨）+ `TimeDepthMapper`
（滚动，可选）+ `KeyInput` + `JudgementTracker.PressLane(轨道号)` + `BeatMath`（BPM↔拍）。判定鉴 =
**球位应达砖块**（`TrackPointAt == 谱面砖块坐标`，如 `AdofaiDemo` 第 235 行）。

**自检断言（可照抄 `AdofaiDemo`）**：完美 6 命中、错键掉轨 MISS、检查点重开段。

### 2.8 `RulesetCheck` 入口

```csharp
static string RulesetCheck()
{
    var sb = new StringBuilder();
    sb.AppendLine(LaneRulesetCheck());
    sb.AppendLine(LineRulesetCheck());
    sb.AppendLine(RingRulesetCheck());
    sb.AppendLine(PathRulesetCheck());
    return sb.ToString();
}
```

---

## 3. SceneTransition 模型（引擎侧时序/缓动，宿主合成器渲染）

> 目标：引擎只算"进度/时序/缓动 + 通用曝光参数"，具体绘制由宿主 `SceneCompositor` 读参数完成。
> 新增文件 `引擎\engine\SceneTransition.cs`（零包依赖）。

### 3.1 `TransitionStyle` 与 `abstract SceneTransition`

```csharp
namespace ChartPlayer
{
    /// <summary>转场风格枚举。</summary>
    public enum TransitionStyle { Fade, Slide, Wipe, CircleReveal, Beam }

    /// <summary>
    /// 转场抽象基类：引擎侧只推进时序与缓动，并曝光一组"宿主可读"的合成参数。
    /// 宿主合成器（SceneCompositor）读取 OutAlpha/InAlpha/OffsetX 及风格特有参数，对 from/to 快照绘制。
    /// 不依赖任何渲染后端。
    /// </summary>
    public abstract class SceneTransition
    {
        double _elapsedMs;
        bool _completed;

        public TransitionStyle Style { get; protected set; }
        public double DurationMs { get; set; } = 500;
        public double RawProgress { get; protected set; }     // 线性 0..1
        public double Progress { get; protected set; }        // 缓动后 0..1
        public bool IsComplete => _completed;
        public double OutAlpha { get; protected set; } = 1;
        public double InAlpha { get; protected set; }
        public double OffsetX { get; protected set; }
        public event Action Completed;

        protected SceneTransition(TransitionStyle style) { Style = style; }

        public void Update(double deltaSeconds)
        {
            if (_completed) return;
            _elapsedMs += Math.Max(0, deltaSeconds) * 1000.0;
            RawProgress = DurationMs <= 1e-6 ? 1 : Math.Min(1, _elapsedMs / DurationMs);
            OnCompute(RawProgress);
            Progress = Easing.EaseInOutCubic(RawProgress);   // 缓动约定见 3.3
            if (_elapsedMs >= DurationMs) { _completed = true; Completed?.Invoke(); }
        }

        protected abstract void OnCompute(double raw);

        public virtual void Reset()
        {
            _elapsedMs = 0; RawProgress = 0; Progress = 0;
            OutAlpha = 1; InAlpha = 0; OffsetX = 0; _completed = false;
        }
    }
}
```

### 3.2 五种内置实现（每类曝光参数表）

| 风格 | 类 | 特有曝光参数 | OutAlpha | InAlpha | OffsetX |
|---|---|---|---|---|---|
| `Fade` | `FadeTransition` | — | `1 - e` | `e` | 0 |
| `Slide` | `SlideTransition` | `DirX`(±1), `SlideDistance` | `1 - e` | 1 | `e * SlideDistance * DirX` |
| `Wipe` | `WipeTransition` | `WipeProgress`(0..1), `FromLeft` | `1 - e` | `e` | 0 |
| `CircleReveal` | `CircleRevealTransition` | `CircleProgress`(0..1), `RadiusMax` | `1 - e` | `e` | 0 |
| `Beam` | `BeamTransition` | `BeamAngleDeg`, `BeamProgress`, `SplitGap` | `1 - e*0.88` | `e` | 0 |

```csharp
namespace ChartPlayer
{
    public sealed class FadeTransition : SceneTransition
    {
        public FadeTransition() : base(TransitionStyle.Fade) { }
        protected override void OnCompute(double raw)
        { double e = Easing.EaseInOutCubic(raw); OutAlpha = 1 - e; InAlpha = e; OffsetX = 0; }
    }

    public sealed class SlideTransition : SceneTransition
    {
        public double DirX = 1;
        public double SlideDistance = 800;
        public SlideTransition() : base(TransitionStyle.Slide) { }
        protected override void OnCompute(double raw)
        { double e = Easing.EaseOutCubic(raw); OutAlpha = 1 - e; InAlpha = 1; OffsetX = DirX * e * SlideDistance; }
    }

    public sealed class WipeTransition : SceneTransition
    {
        public bool FromLeft = true;
        public double WipeProgress => Progress;
        public WipeTransition() : base(TransitionStyle.Wipe) { }
        protected override void OnCompute(double raw)
        { double e = Easing.EaseInOutCubic(raw); OutAlpha = 1 - e; InAlpha = e; OffsetX = 0; }
    }

    public sealed class CircleRevealTransition : SceneTransition
    {
        public double RadiusMax = 1000;
        public double CircleProgress => Progress;
        public CircleRevealTransition() : base(TransitionStyle.CircleReveal) { }
        protected override void OnCompute(double raw)
        { double e = Easing.EaseInOutCubic(raw); OutAlpha = 1 - e; InAlpha = e; OffsetX = 0; }
    }

    /// <summary>光束劈裂：分 3 段（slash → rotate → split）。只算参数，绘制由合成器完成。</summary>
    public sealed class BeamTransition : SceneTransition
    {
        public double BeamAngleDeg;
        public double BeamProgress => Progress;
        public double SplitGap;
        public BeamTransition() : base(TransitionStyle.Beam) { }
        protected override void OnCompute(double raw)
        {
            double slashP = Easing.EaseInOutCubic(Math.Min(1, raw / 0.22));
            double rotP   = raw <= 0.22 ? 0 : raw >= 0.50 ? 1 : Easing.EaseInOutCubic((raw - 0.22) / 0.28);
            double splitP = raw <= 0.50 ? 0 : Easing.EaseInOutCubic((raw - 0.50) / 0.50);
            double e = Easing.EaseInOutCubic(raw);
            OutAlpha = 1 - e * 0.88;
            InAlpha  = Easing.Clamp01((raw - 0.22) / 0.78);
            OffsetX  = 0;
            SplitGap = splitP * 800;
            BeamAngleDeg += rotP * 360.0;     // 引擎只累加角度（宿主按绝对角画光束）
        }
    }
}
```

### 3.3 引擎侧时序/缓动约定

- 引擎提供 `Easing` 曲线后，`SceneTransition.Progress` 用**缓动后**；`RawProgress` 为线性。
- 仅用引擎自带 `Easing.Clamp01 / Lerp / EaseInOutCubic / EaseOutCubic / InverseLerp`（`Engine3D.cs`），不引入新曲线（`EaseInOutCubic`、`EaseOutCubic` 引擎已具备）。
- 引擎不改任何渲染状态；宿主合成器每帧读 `Progress/OutAlpha/InAlpha/OffsetX + 风格参数` 对 from/to 快照合成。

### 3.4 自检断言（`SceneTransitionCheck`）

```csharp
static string SceneTransitionCheck()
{
    var fade = new FadeTransition { DurationMs = 1000 };
    int completed = 0; fade.Completed += () => completed++;
    fade.Update(0.5);
    Assert(fade.OutAlpha < 1 && fade.InAlpha > 0, "Fade 中段 alpha 互换");
    Assert(!fade.IsComplete && completed == 0, "未完成");
    fade.Update(0.6);
    Assert(fade.IsComplete && completed == 1, "Fade 完成且 Completed 触发一次");

    var slide = new SlideTransition { DurationMs = 800 };
    slide.Update(0.4);
    Assert(Math.Abs(slide.OffsetX) > 0, "Slide 有位移");
    slide.Update(0.5); Assert(slide.IsComplete, "Slide 完成");

    var wipe = new WipeTransition { DurationMs = 600 };
    wipe.Update(0.3);
    Assert(wipe.WipeProgress > 0 && wipe.WipeProgress < 1, "Wipe 进度");
    return "[SceneTransition] Fade/Slide/Wipe 时序+缓动+完成事件 通过\n";
}
```

> 注意：`BeamTransition.OnCompute` 的角度累加是按帧叠加的；自检若断言角度，宜用**总时长固定步长**
> 模拟（`Update(0.3); Update(0.3);`）以保证确定性。宿主以读 `BeamAngleDeg`/`SplitGap` 为准。

---

## 4. 引擎 UI 模型（重做 UI：IUiDraw + Ui* 控件 + UiTheme）

> 目标：引擎侧定义最小绘图接口与 UI 控件（作为 `Component` 子类），全部用 `RgbaColor`；
> 宿主用 `D2DDrawAdapter` 实现 `IUiDraw` 落到 `D2DRenderer`。新增文件
> `引擎\engine\UiDraw.cs`、`引擎\engine\UiControl.cs`、`引擎\engine\UiTheme.cs`（零包依赖）。

### 4.1 `IUiDraw` 最小绘图接口（`UiDraw.cs`）

```csharp
namespace ChartPlayer
{
    /// <summary>引擎侧最小绘图接口：只依赖 RgbaColor 与 UiImage，宿主用适配器落到任意渲染后端。
    /// 坐标均为"逻辑/虚拟"坐标，宿主负责 DPI/缩放适配。</summary>
    public interface IUiDraw
    {
        void Rect(double x, double y, double w, double h, RgbaColor fill);
        void RoundedRect(double x, double y, double w, double h, double radius, RgbaColor fill);
        void Text(string s, double x, double y, double w, double h, RgbaColor color, double size, bool center = false);
        void Ellipse(double cx, double cy, double rx, double ry, RgbaColor fill);
        void Line(double x1, double y1, double x2, double y2, RgbaColor color, double thickness = 1.0);
        void Image(UiImage img, double dx, double dy, double dw, double dh, double opacity = 1.0);
        void PushClip(double x, double y, double w, double h);
        void PopClip();
        double MeasureText(string s, double size);
    }

    /// <summary>引擎中立位图句柄：宿主在 D2DDrawAdapter 内把 Handle 绑定到后端位图。</summary>
    public sealed class UiImage
    {
        public object Handle;
        public int Width, Height;
        public UiImage() { }
        public UiImage(object handle, int width, int height) { Handle = handle; Width = width; Height = height; }
    }
}
```

### 4.2 `UiRect` 与 `UiElement`（布局基类，`UiControl.cs`）

```csharp
namespace ChartPlayer
{
    public readonly struct UiRect
    {
        public readonly double X, Y, W, H;
        public UiRect(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
        public bool Contains(double px, double py) => px >= X && px <= X + W && py >= Y && py <= Y + H;
    }

    /// <summary>UI 元素基类：矩形布局 + 渲染回调。继承 Component，挂在 GameObject 上由场景驱动。</summary>
    public abstract class UiElement : Component
    {
        public double X, Y, Width, Height;
        public double Margin = 8;
        public bool Visible = true;
        public UiRect Rect => new UiRect(X, Y, Width, Height);

        /// <summary>宿主渲染 pass 注入的绘图器。</summary>
        public IUiDraw Draw { get; set; }

        public void SetRect(double x, double y, double w, double h) { X = x; Y = y; Width = w; Height = h; }

        public virtual void DrawSelf() { }
        public virtual bool HitTest(double px, double py) => Rect.Contains(px, py);
    }
}
```

### 4.3 控件签名（`UiControl.cs`）

```csharp
namespace ChartPlayer
{
    /// <summary>面板：纯背景容器（圆角/平角）。</summary>
    public class UiPanel : UiElement
    {
        public RgbaColor Background;
        public double CornerRadius;
        public UiPanel(RgbaColor bg = default, double corner = 0) { Background = bg; CornerRadius = corner; }
        public override void DrawSelf()
        {
            base.DrawSelf();
            if (Draw == null) return;
            if (CornerRadius <= 0) Draw.Rect(X, Y, Width, Height, Background);
            else Draw.RoundedRect(X, Y, Width, Height, CornerRadius, Background);
        }
    }

    /// <summary>按钮：背景 + 文本 + 悬停/按下状态 + Click 事件。</summary>
    public class UiButton : UiPanel
    {
        public string Text;
        public RgbaColor HoverColor, PressColor, TextColor;
        public double FontSize = 24;
        public event Action Clicked;
        bool _hover, _pressed;

        public override void DrawSelf()
        {
            var bg = _pressed ? PressColor : _hover ? HoverColor : Background;
            if (CornerRadius <= 0) Draw?.Rect(X, Y, Width, Height, bg);
            else Draw?.RoundedRect(X, Y, Width, Height, CornerRadius, bg);
            if (Text != null) Draw?.Text(Text, X, Y, Width, Height, TextColor, FontSize, center: true);
        }

        public void OnHover(bool hover) => _hover = hover;
        public void OnDown(bool down) => _pressed = down;
        public override bool HitTest(double px, double py) => Rect.Contains(px, py);
        public void InvokeClick() => Clicked?.Invoke();
    }

    /// <summary>标签：纯文本。</summary>
    public class UiLabel : UiElement
    {
        public string Text;
        public RgbaColor Color;
        public double FontSize = 26;
        public bool Center = true;
        public UiLabel() { }
        public UiLabel(string text, RgbaColor color, double size) { Text = text; Color = color; FontSize = size; }
        public override void DrawSelf() => Draw?.Text(Text, X, Y, Width, Height, Color, FontSize, Center);
    }

    /// <summary>卡片：带边框/悬停/选中态的选择容器。</summary>
    public class UiCard : UiPanel
    {
        public RgbaColor Border;
        public RgbaColor HoverBg, SelBg;
        public bool Selected;
        public double BorderThickness = 2;
        bool _hover;
        public override void DrawSelf()
        {
            base.DrawSelf();
            if (Draw == null) return;
            var bg = Selected ? SelBg : _hover ? HoverBg : Background;
            if (CornerRadius <= 0) Draw.Rect(X, Y, Width, Height, bg);
            else Draw.RoundedRect(X, Y, Width, Height, CornerRadius, bg);
            Draw.Line(X, Y, X + Width, Y, Border, BorderThickness);
            Draw.Line(X, Y + Height, X + Width, Y + Height, Border, BorderThickness);
        }
        public void OnHover(bool h) => _hover = h;
    }

    /// <summary>堆叠布局：按 Margin 依次排列子元素（UiElement 挂在同名 GameObject 下）。</summary>
    public class UiStackLayout : UiElement
    {
        public bool Vertical = true;
        public IReadOnlyList<UiElement> Children { get; } = new List<UiElement>();

        /// <summary>引擎侧布局计算（无头可断言）：把子元素按顺序排布到本容器内。</summary>
        public void Layout()
        {
            double cur = 0;
            foreach (var c in Children)
            {
                if (Vertical) { c.SetRect(X, Y + cur, Width, c.Height); cur += c.Height + Margin; }
                else { c.SetRect(X + cur, Y, c.Width, Height); cur += c.Width + Margin; }
            }
        }

        public override void DrawSelf()
        {
            base.DrawSelf();
            Layout();
            foreach (var c in Children) c.DrawSelf();
        }
    }
}
```

> 说明：为使 UI 控件能在**无游戏场景**中单独自检（不需 GameObject/场景），`UiElement` 也可被直接
> `new` 使用（只依赖布局/绘制），`Component.GameObject` 允许 null。控件不强制挂场景树即可自检。

### 4.4 `UiTheme` 新主题（重做 UI，`UiTheme.cs`）

> 用引擎 `RgbaColor` 定义整套调色板 + 字号/圆角令牌。语义对齐宿主现有 `UiColors`（深空蓝 / 极夜紫 /
> 晨光青），但**不引用 System.Drawing**，供引擎 UI 控件的默认样式使用。

```csharp
namespace ChartPlayer
{
    /// <summary>引擎 UI 主题令牌（重做 UI）：整组 RgbaColor + 圆角/字号。宿主可自建/切换。</summary>
    public sealed class UiTheme
    {
        public RgbaColor Bg = RgbaColor.FromHex("#0a0e16");
        public RgbaColor ScreenBg = RgbaColor.FromHex("#080b13");
        public RgbaColor Fg = RgbaColor.FromHex("#dfe6f0");
        public RgbaColor CardBg = RgbaColor.FromHex("#141d31");
        public RgbaColor CardHover = RgbaColor.FromHex("#22304d");
        public RgbaColor CardSel = RgbaColor.FromHex("#1c2f55");
        public RgbaColor Border = RgbaColor.FromHex("#22304d");
        public RgbaColor BorderLight = RgbaColor.FromHex("#2d3c5e");
        public RgbaColor InputBg = RgbaColor.FromHex("#101828");
        public RgbaColor InputBorder = RgbaColor.FromHex("#2a3a5a");
        public RgbaColor HeadBg = RgbaColor.FromHex("#0a0e16");
        public RgbaColor HeadTitle = RgbaColor.FromHex("#9db4e8");
        public RgbaColor SubText = RgbaColor.FromHex("#7f93bb");
        public RgbaColor BodyText = RgbaColor.FromHex("#b8c6dd");
        public RgbaColor Muted = RgbaColor.FromHex("#9fb2d0");
        public RgbaColor Dim = RgbaColor.FromHex("#6b7ea0");
        public RgbaColor Green = RgbaColor.FromHex("#7fd0a0");
        public RgbaColor Blue = RgbaColor.FromHex("#3d7bff");
        public RgbaColor BlueBtn = RgbaColor.FromHex("#2d6cff");
        public RgbaColor BtnBg = RgbaColor.FromHex("#1c2740");
        public RgbaColor BtnHover = RgbaColor.FromHex("#27355a");
        public RgbaColor TabBg = RgbaColor.FromHex("#16203a");
        public RgbaColor TabBorder = RgbaColor.FromHex("#243247");
        public RgbaColor Gold = RgbaColor.FromHex("#ffd23f");
        public RgbaColor Accent = RgbaColor.FromHex("#3d7bff");

        public double CardRadius = 10;
        public double BaseFontSize = 26;
        public double LabelFontSize = 26;
        public double ButtonFontSize = 24;

        public void Apply(UiTheme src) { /* 逐字段拷贝 */ }

        public static UiTheme CreateDefault() => new UiTheme();
        public static UiTheme DeepSpace() => new UiTheme();
        public static UiTheme Violet() => new UiTheme();
        public static UiTheme Cyan() => new UiTheme();

        public RgbaColor Color(string semantic) => semantic switch
        {
            "bg" => Bg, "fg" => Fg, "card" => CardBg, "border" => Border,
            "accent" => Accent, "green" => Green, "gold" => Gold, _ => Fg
        };
    }
}
```

> `switch` 表达式需 C# 8+，本工程 `LangVersion=latest` 已满足；若要兼容更早期编译器可改写为 `if/else`。

### 4.5 UI 布局自检断言（`UiCheck`）

```csharp
static string UiCheck()
{
    var panel = new UiPanel(RgbaColor.FromHex("#141d31"), 10) { X = 0, Y = 0, Width = 300, Height = 200 };
    var b1 = new UiButton { Text = "OK", Width = 100, Height = 40 };
    var b2 = new UiButton { Text = "Cancel", Width = 100, Height = 40 };
    b1.SetRect(10, 10, 100, 40);
    b2.SetRect(10, 58, 100, 40);
    Assert(b1.Rect.Contains(20, 20) && !b2.Rect.Contains(20, 20), "UiRect 命中检测");
    Assert(panel.Rect.W == 300 && panel.Rect.H == 200, "UiPanel 矩形");
    Assert(b1.Height == 40 && b2.Y > b1.Y, "纵向堆叠 b2 在 b1 之下");

    RgbaColor c = UiTheme.DeepSpace().Accent;
    Assert(c.A == 255 && c.R == 61 && c.B == 255, "UiTheme 用 RgbaColor（零 System.Drawing）");
    return "[EngineUi] UiRect/UiPanel/UiButton/UiTheme 布局+颜色 通过\n";
}
```

---

## 5. 文件清单与责任边界

### 5.1 引擎库新增文件（`namespace ChartPlayer`，零包依赖）

| 文件 | 内容 | 依赖的现有引擎模块 |
|---|---|---|
| `引擎\engine\GameObject.cs` | `Component`、`GameObject` | `Transform`、`Vec3` |
| `引擎\engine\Scene.cs` | `Scene`、`SceneManager` | `GameObject`/`Component` |
| `引擎\engine\GameLoop.cs` | `GameLoop` | `SceneManager`、`EngineTime` |
| `引擎\engine\Ruleset.cs` | `ChartContext`、`abstract Ruleset`、`InputEvent`、`RenderNote`、`LaneRuleset`、`LineRuleset`、`RingRuleset`、`PathRuleset` | `LaneField`/`LineField`/`RingField`/`PathField`、`TimeDepthMapper`、`KeyInput`/`TouchInput`/`InputMapper`、`JudgementProfile`/`JudgementTracker`/`ScoreBoard`、`ChartData`、`RgbaColor` |
| `引擎\engine\SceneTransition.cs` | `TransitionStyle`、`abstract SceneTransition`、5 内置样式 | `Easing` |
| `引擎\engine\UiDraw.cs` | `IUiDraw`、`UiImage` | `RgbaColor` |
| `引擎\engine\UiControl.cs` | `UiRect`、`UiElement`、`UiPanel`、`UiButton`、`UiLabel`、`UiCard`、`UiStackLayout` | `Component`、`RgbaColor`、`IUiDraw` |
| `引擎\engine\UiTheme.cs` | `UiTheme` | `RgbaColor` |
| `引擎\engine\MathExtra.cs`（通常不需要） | 仅当新增缓动/数学工具时用；`EaseInOutCubic`/`EaseOutCubic` 引擎已具备 | `Easing` |

> 归属确认：上述文件都进 `MilestoneEngine.csproj` 默认编译（`<Compile Remove>` 只排除 Samples/Tests/HelloEngine），
> 故 `csproj` 无需改动即自动纳入。

### 5.2 宿主新增文件（`源码\Play\EngineUi\*`、`源码\Play\EngineTransitions\*`）

| 文件 | 内容 | 依赖（宿主可用 System.Drawing/SharpDX） |
|---|---|---|
| `源码\Play\EngineUi\D2DDrawAdapter.cs` | `sealed class D2DDrawAdapter : IUiDraw`——映射到 `D2DRenderer` | `D2DRenderer`、`RgbaColor`→`Color`、`UiImage`→`D2DBitmap` |
| `源码\Play\EngineUi\UiRenderHost.cs`（可选） | 宿主在 UI pass 内创建适配器、遍历场景中的 `UiElement` 注入 `Draw` 并调用 `DrawSelf` | `D2DRenderer`、`GameObject/Component` |
| `源码\Play\EngineTransitions\SceneCompositor.cs` | 读 `SceneTransition` 的 `Progress/OutAlpha/InAlpha/OffsetX/风格参数`，对 from/to 快照合成 | `IRenderer`、`D2DRenderer`、`SceneTransition` |
| `源码\Play\EngineTransitions\EngineTransitionHost.cs` | 宿主把新旧两帧画面截成位图交给 `SceneCompositor` | WinForms timer、`Bitmap` |
| `源码\Play\EngineTransitions\TransitionSurface.cs`（可选） | 过渡期间承载合成的自绘窗口/控件 | WinForms |

### 5.3 D2D 适配映射表（`D2DDrawAdapter`）

| `IUiDraw` | `D2DRenderer` | 颜色/参数转换 |
|---|---|---|
| `Rect(x,y,w,h,fill)` | `FillRect(x,y,w,h, color)` | `RgbaColor` → `Color.FromArgb(A,R,G,B)` |
| `RoundedRect(...,rad,fill)` | `FillRoundedRect(x,y,w,h,rad, color)` | 同上 |
| `Text(s,x,y,w,h,color,size,center)` | `Text(s,x,y,w,h,color,size,center)` | 颜色转换 + 字号用宿主字号（DPI 已处理） |
| `Ellipse(cx,cy,rx,ry,fill)` | `FillEllipse(cx,cy,rx,ry,color)` | 同上 |
| `Line(x1,y1,x2,y2,color,thickness)` | `DrawLine(x1,y1,x2,y2,color,thickness)` | 同上 |
| `Image(img,dx,dy,dw,dh,opacity)` | `DrawImage((D2DBitmap)img.Handle, dx,dy,dw,dh,opacity)` | `UiImage.Handle` 强转宿主位图 |
| `PushClip(x,y,w,h)` | `PushQuadLayer`（矩形四边形）/宿主矩形裁剪封装 | 宿主定义矩形裁剪 |
| `PopClip()` | `PopLayer()` | — |
| `MeasureText(s,size)` | `MeasureText(s,size)` | 同字号 |

> `D2DRenderer` 现有 `PushQuadLayer/PopLayer` 是**四边形图层**而非严格矩形裁剪；若需任意矩形裁剪，
> 宿主在 `D2DDrawAdapter` 内补一个矩形图层封装（基于 `PushQuadLayer` 传四角矩形）。`D2DDrawAdapter`
> 属宿主，可自由用 `System.Drawing` 做桥接。

### 5.4 `SceneCompositor` 渲染转场（读引擎参数画）

```csharp
namespace ChartPlayer
{
    /// <summary>宿主转场合成器：只读引擎 SceneTransition 的曝光参数，用 IRenderer 对两帧快照合成。</summary>
    public sealed class SceneCompositor
    {
        public SceneTransition Transition;
        public IRenderer Renderer;
        bool _drawing;

        public void Compose(double deltaSeconds)
        {
            if (Transition == null || Renderer == null) return;
            Transition.Update(deltaSeconds);                 // 引擎只推进时序/缓动
            if (!Renderer.Begin()) return;
            Renderer.Clear(Color.FromArgb((int)(255 * Transition.OutAlpha), 0, 0, 0));
            // 宿主在此按 Style 读取参数画 from/to，例如：
            //  - Fade: DrawImage(to, alpha=InAlpha)
            //  - Slide: DrawImage(to, x=OffsetX)
            //  - Wipe: 按 WipeProgress 裁剪
            //  - CircleReveal: 按 CircleProgress 半径画圆窗
            //  - Beam: 按 BeamAngleDeg/SplitGap 画光束劈裂
            Renderer.End();
            if (Transition.IsComplete) _drawing = false;
        }

        public bool IsDrawing => _drawing;
        public void Start(SceneTransition t, IRenderer r)
        { Transition = t ?? new FadeTransition { DurationMs = 500 }; Renderer = r; Transition.Reset(); _drawing = true; }
    }
}
```

> 过渡期间宿主应暂停/冻结旧场景渲染（或抓快照），只让 `SceneCompositor` 逐帧合成。
> 依赖方向：`SceneCompositor`（宿主）→ `IRenderer`/`SceneTransition`（引擎）。

### 5.5 工程接入（csproj 调整）

- **引擎库 `引擎\engine\MilestoneEngine.csproj`**：无需改（新文件在 `engine\` 根自动编译；Samples/Tests/HelloEngine 已排除）。
- **宿主 `Milestone.csproj`**：已 `<ProjectReference Include="引擎\engine\MilestoneEngine.csproj" />`、
  `<Compile Remove="引擎\engine\**\*.cs" />` 只排除引擎；`源码\Play\EngineUi\**`、`源码\Play\EngineTransitions\**`
  会被 SDK 自动纳入。若新增子目录的 `.cs` 确认未被其它 `<Compile Remove>` 遮掉。
- **引擎自检 `引擎\engine\Tests\EngineChecks\EngineChecks.csproj`**：`EngineChecks` 只 `ProjectReference` 引擎 +
  编译 `Samples`；新自检断言（GameLoopCheck/RulesetCheck/SceneTransitionCheck/UiCheck）若放 `Samples\DemoRunner.cs`
  会随 `Samples\*.cs` 自动编入；若放 `Tests\` 需在 csproj 显式 `<Compile Include>`。

### 5.6 依赖方向图与禁止项

```text
宿主 (源码\Play)                                           引擎库 (引擎\engine, 零包依赖, namespace ChartPlayer)
 │  D2DRenderer : IRenderer  ──────────►  ─┐
 │  D2DDrawAdapter : IUiDraw  ──────────►  │  GameObject/Component/Scene/SceneManager/GameLoop
 │  SceneCompositor (读参数) ───────────►  │  Ruleset/ChartContext/4 玩法族
 │                                         │  SceneTransition/5 样式
 │                                         └─ IUiDraw/UiElement/Ui*/UiTheme (全 RgbaColor)
 │                                            复用: Transform/Vec2/Vec3/Mat4/Easing/Tween
 ▼
WinForms/SharpDX (仅宿主)                  引擎永不出现: System.Drawing / System.Windows.Forms / SharpDX / PackageReference
```

**禁止项（硬性）**：

- 引擎任何 `.cs`：`using System.Drawing;` `using System.Windows.Forms;` `using SharpDX.*;` `PackageReference` → 违规。
- 引擎任何公共 API 出现 `System.Drawing.Color`、`System.Drawing.Bitmap`、WinForms 类型 → 违规，一律 `RgbaColor`/`UiImage`。
- 引擎不得开线程/访问窗口/GPU；只允许纯逻辑 + 宿主注入的 `IUiDraw`/`IRenderer`。

---

## 6. 落地顺序建议（实现→自检→集成）

1. **GameObject/Component/Scene/SceneManager/GameLoop**（引擎 3 文件）→ `GameLoopCheck` 断言生命周期。
2. **SceneTransition + 5 样式**（引擎 1 文件）→ `SceneTransitionCheck` 断言时序/缓动/完成事件。
3. **IUiDraw/UiRect/UiElement/Ui*/UiTheme**（引擎 3 文件）→ `UiCheck` 断言布局/颜色（零 System.Drawing）。
4. **Ruleset + ChartContext + 4 玩法族**（引擎 1 文件）→ `RulesetCheck`（4 个 family 判定闭环）复用/对齐 `DemoRulesets`/`DemoRunner`。
5. **宿主接入**：`D2DDrawAdapter`（IUiDraw→D2DRenderer）、`SceneCompositor`（读参数合成转场），接到主流程与自检工程。
6. **构建 + 自检**：`dotnet build 引擎\engine\MilestoneEngine.csproj`、`dotnet run --project 引擎\engine\Tests\EngineChecks\EngineChecks.csproj`（全断言通过）、再 `dotnet build Milestone.csproj`。

---

## 7. 验收清单（Definition of Done）

- [ ] 引擎库新增 7~8 文件，`namespace ChartPlayer`；`MilestoneEngine.csproj` 编译零包依赖。
- [ ] 引擎库任何文件不出现 `System.Drawing`/`System.Windows.Forms`/`SharpDX`；全部颜色用 `RgbaColor`。
- [ ] `GameObject/Component/Scene/SceneManager/GameLoop` 生命周期符合 1.1 表，`GameLoopCheck` 通过。
- [ ] 四个玩法族 Ruleset 无头判定闭环（`RulesetCheck` 通过），断言与 `DemoRulesets` 语义一致。
- [ ] `SceneTransition` 只算时序/缓动与曝光参数，5 样式齐；`SceneTransitionCheck` 通过。
- [ ] `IUiDraw` 用 `RgbaColor`，`D2DDrawAdapter` 完成到 `D2DRenderer` 的映射；`UiCheck` 通过。
- [ ] `SceneCompositor` 读引擎参数合成转场，接入主流程。
- [ ] `dotnet build`（引擎 + 宿主）+ `EngineChecks` 自检全部通过。
