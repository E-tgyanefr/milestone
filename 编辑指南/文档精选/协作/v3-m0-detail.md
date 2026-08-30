# v3 M0 派单细目（CMake 骨架 + Core API + 生命周期断言 + ms_test）——eng-coder-vis 实施蓝本

> 起草：eng-design-vis（引擎策划）· 任务 t107 后续 · V3-2..8 拍板后产出 · 用户指令：M0 立即派（t108 与之对齐）。
> 本文件=**实现蓝本**（规格/签名级，非成品代码）——eng-coder-vis 按此落地；M0 验收=MSVC+Clang 双链构建 + ms_test 全绿（生命周期顺序断言+树/变换/时间用例）+退出码约定。

---

## 1. 项目布局（新根：引擎源码/engine-v3/——独立 C++ 项目）

~~~
engine-v3/
├─ CMakeLists.txt                     # 顶层：project(MilestoneEngineV3 CXX) C++20；选项；add_subdirectory
├─ include/milestone/core/            # 公共头（M0：Core 域）
│   ├─ core.hpp                       # umbrella（统一 include）
│   ├─ math.hpp                       # Vec3/Quat/Mat4（双精度+基础运算；M0 最小集）
│   ├─ instance.hpp                   # InstanceId 单调计数器
│   ├─ transform.hpp                  # Transform（层级/矩阵/SetParent(keepWorld)）
│   ├─ game_object.hpp                # GameObject（AddComponent/GetComponent/SetActive/Destroy/Transform）
│   ├─ component.hpp                  # Component（八回调虚接口 + enabled）
│   ├─ scene.hpp                      # Scene（AddRoot/根集合/LifecycleDriver）
│   ├─ lifecycle_driver.hpp           # 组件注册表+按 Unity 顺序分派+SequenceLog
│   ├─ time_singleton.hpp             # TimeSingleton{deltaTime/timeScale/真实时间}
│   └─ event_bus.hpp                  # EventBus（类型擦除+每帧 flush）
├─ src/core/                          # 实现（game_object.cpp/transform.cpp/scene.cpp/
│   │                                 #   lifecycle_driver.cpp/time_singleton.cpp/event_bus.cpp/instance.cpp）
├─ tests/
│   ├─ ms_test/ms_test.hpp            # 自研测试头（TEST_CASE/CHECK_*/RUNNER——零第三方）
│   ├─ CMakeLists.txt                 # ms_test 可执行（test_main + core 用例）
│   └─ core/
│       ├─ test_main.cpp              # Runner（注册表遍历+退出码=fails 数）
│       ├─ test_lifecycle.cpp         # 八回调顺序断言（SequenceLog 比对）
│       ├─ test_tree_transform.cpp    # 子树/SetParent(keepWorld) 矩阵断言
│       └─ test_time_event.cpp        # 时间/变速+事件顺序断言
└─ README.md                          # M0：构建/测试/退出码（简短）
~~~

**CMake 顶层要点**：CMAKE_CXX_STANDARD 20（REQUIRED）；MSVC=/W4 /permissive-；Clang=-Wall -Wextra -Wpedantic（-Werror 可选开关）；选项：MS_BUILD_TESTS=ON（默认）、MS_TEST_VERBOSE；add_subdirectory(src/core)（静态库 milestone_core）与 tests/。

## 2. 命名空间（逐文件）

- 根=**Milestone**（全部）——子域：**Milestone::Core**（M0）/Platform（M1）/Domain（M4：Rhythm 子域）/Editor（M3）/Tools。
- M0 文件归属：include/milestone/core/* + src/core/* = namespace Milestone::Core（统一）。
- 无全局命名污染；头文件不 using namespace。

## 3. Core API 签名集（M0 蓝本）

~~~cpp
namespace Milestone::Core {
using InstanceId = std::uint64_t;

// —— 数学（M0 最小）——
struct Vec3 { double x=0,y=0,z=0; double Dot(const Vec3&) const; Vec3 Cross(const Vec3&) const; };
struct Quat { double w=1,x=0,y=0,z=0; static Quat Euler(double degX,double degY,double degZ); };
struct Mat4 { static Mat4 Identity(); static Mat4 RT(const Vec3& t,const Quat& r,const Vec3& s); Mat4 Mul(const Mat4&) const; Mat4 InverseRT() const; };

// —— 对象身份 ——
class InstanceIds { public: static InstanceId Next(); };

// —— Transform（层级+矩阵，双精度）——
class Transform {
public:
  Vec3 Position() const; void SetPosition(const Vec3&);
  Quat Rotation() const; void SetRotation(const Quat&);
  Vec3 Scale() const; void SetScale(const Vec3&);
  Transform* Parent() const; const std::vector<Transform*>& Children() const;
  void SetParent(Transform* parent, bool keepWorld=false);
  Mat4 LocalMatrix() const; Mat4 WorldMatrix() const;
  Vec3 Forward() const; Vec3 Right() const; Vec3 Up() const;
private: /* parent/children/worldDirty */ friend class GameObject;
};

// —— 组件（八回调——顺序由 LifecycleDriver 保证）——
class Component {
public:
  explicit Component(); virtual ~Component() = default;
  virtual void Awake() {} virtual void OnEnable() {}
  virtual void Start() {} virtual void Update(double) {}
  virtual void FixedUpdate(double) {} virtual void LateUpdate(double) {}
  virtual void OnDisable() {} virtual void OnDestroy() {}
  bool IsEnabled() const; void SetEnabled(bool v);
  GameObject* Owner() const; InstanceId Id() const;
protected: bool enabled_{ true }; private: /* id/owner */ friend class GameObject; friend class LifecycleDriver;
};

// —— GameObject ——
class GameObject {
public:
  explicit GameObject(std::string name); ~GameObject();
  const std::string& Name() const; void SetName(const std::string&);
  Transform* Transform() const;
  template<class TComponent> TComponent* AddComponent();
  template<class TComponent> TComponent* GetComponent() const;
  void SetActive(bool v); bool ActiveInHierarchy() const;
  void Destroy();
  InstanceId Id() const;
private: /* name/transform/active/components */ friend class Scene; friend class LifecycleDriver;
};

// —— Scene ——
class Scene {
public:
  Scene();
  GameObject* AddRoot(const std::string& name);
  const std::vector<std::unique_ptr<GameObject>>& Roots() const;
  LifecycleDriver& Lifecycle();
private: std::vector<std::unique_ptr<GameObject>> roots_;
};

// —— LifecycleDriver（核心：Unity 顺序分派 + 顺序日志）——
class LifecycleDriver {
public:
  void Register(Component* c, GameObject* owner);
  void Unregister(Component* c);
  void Tick(double dt);
  void SetActive(Component* c, bool v);
  void PushDestroy(GameObject* go);
  const std::vector<std::string>& SequenceLog() const;
private: /* registry/phases/destroy queue */
};

// —— Time ——
class TimeSingleton {
public:
  double DeltaTime() const; double UnscaledDeltaTime() const;
  void SetTimeScale(double); double TimeScale() const;
  double TimeSinceStart() const;
  void Tick(double realDt);
};

// —— EventBus ——
class EventBus {
public:
  template<class E> void Subscribe(InstanceId id, std::function<void(const E&)> f);
  template<class E> void Publish(const E& e);
  void Flush();
  void Unsubscribe(InstanceId id);
};
}
~~~

**范围注释**：GetComponent=类型 RTTI key（M0 简单映射，无反射依赖）；Destroy=延迟队列（帧尾执行，Unity 语义）；FixedUpdate=固定步长回调（默认 60Hz，可配）；EventBus=Publish 入队、Flush 派发（保序防迭代中增删）。

## 4. ms_test 脚手架（零第三方）

~~~cpp
// ms_test.hpp（唯一发布头——Catch2 风格最小集）
#define TEST_CASE(name)                    /* 注册静态 Case */
#define CHECK_TRUE(expr)                   /* 记录 文件/行/表达式 */
#define CHECK_EQ(a,b)                      /* == 并打印值 */
#define CHECK_NEAR(a,b,eps)                /* 数值近似 */
#define CHECK_THROW(expr)                  /* 异常断言 */
#define REQUIRE_TRUE(expr)                 /* 失败即中断用例 */

namespace ms_test {
struct Case { const char* name; void (*fn)(); };
std::vector<Case>& Registry();
int  RunAll(bool verbose);                 // 输出 PASS/FAIL 清单；返回 fails 数
}   // main 由 test_main.cpp：return ms_test::RunAll(verbose);
~~~

- Runner：输出 [PASS]/[FAIL] 汇总+失败明细；**退出码=fails 数（0=全绿）**——CI 门禁用。

## 5. 八回调生命周期断言（验收核心）

**用例 test_lifecycle.cpp**：

~~~cpp
class LifeProbe : public Component {
  void Awake() override { Log("Awake"); }        // Log → driver.SequenceLog
  void OnEnable() override { Log("OnEnable"); }
  void Start() override { Log("Start"); }
  void Update(double) override { Log("Update"); }
  void FixedUpdate(double) override { Log("FixedUpdate"); }
  void LateUpdate(double) override { Log("LateUpdate"); }
  void OnDisable() override { Log("OnDisable"); }
  void OnDestroy() override { Log("OnDestroy"); }
};
// 场景: go=scene.AddRoot("p")->AddComponent<LifeProbe>(); 驱动 2 帧 Tick
// 断言: SequenceLog 预期 [Awake,OnEnable,Start,FixedUpdate,Update,LateUpdate,...]（Unity 官方顺序——R1 修正：FixedUpdate 在 Update 前；固定步长累加器每帧 0..N 次，步数全局一致）
// 附加: SetActive(false)→OnDisable 即时; Destroy()→帧尾 OnDisable→OnDestroy
~~~

**用例 test_tree_transform.cpp**：父子树 SetParent(keepWorld) 世界位姿保持（parent 位移+子本地偏移→WorldMatrix 等式）；Children 顺序稳定；Local/World 互逆。
**用例 test_time_event.cpp**：TimeSingleton delta/timeScale（scale=0.5→delta 减半；TimeSinceStart 单调）；EventBus Publish 顺序/订阅/退订/Flush 一次派发。

## 6. CMake 构建/测试（验收命令）

~~~powershell
cmake -S 引擎源码/engine-v3 -B 构建产物/engine-v3/build -DCMAKE_BUILD_TYPE=Debug
cmake --build 构建产物/engine-v3/build --config Debug          # 0 警告 0 错误（/W4 或 -Wall -Wextra）
构建产物/engine-v3/build/tests/ms_test_core.exe               # exit=0（fails=0）
# Clang 链（可选第二验证）：-DCMAKE_CXX_COMPILER=clang++ ...（同上 0 警告）
~~~

## 7. M0 验收清单（评审用）

| # | 项 | 判据 |
|---|---|---|
| 1 | 目录/命名空间 | engine-v3 结构+Milestone::Core 全文件（无 ChartPlayer/C# 残留） |
| 2 | CMake 双链 | MSVC 默认+Clang 可选：0 警告 0 错误；C++20 启用 |
| 3 | 生命周期顺序 | test_lifecycle 全绿（SequenceLog=Unity 官方顺序；SetActive/Destroy 附加断言） |
| 4 | 树/变换 | test_tree_transform 全绿（keepWorld 数学正确/children 稳定） |
| 5 | 时间/事件 | test_time_event 全绿（scale/单调/订阅-退订-顺序） |
| 6 | ms_test 脚手架 | RUNNER 退出码=fails（0=全绿） |
| 7 | 零第三方 | 仓库无第三方 include/包（ImGui 不在 M0） |

---

*M0 蓝本完毕*（规格/签名级；eng-coder-vis 按此落地；与 t108 对齐；M1 窗口+渲染+输入 待 M0 验收后出细目）
