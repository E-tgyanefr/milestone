# t121：v3 M2.6 Python 绑定层设计（ctypes 经同一 C ABI + 与 C# 对称的开发层 API）

> eng-design-vis · 新用户需求：引擎内容支持 **Python 编写项目**。
> 基线：RhygeMaker（正式命名）——C++ 核（Engine/Assets/Platform）+ C ABI（ms_bind.h 43 函数——**ABI 稳定=ms_ 前缀保留**（品牌改名不换 ABI；文档标注））+ C# 开发层（RhygeMaker.Bind）——本层=**对称 Python 开发层**（不嵌入 CPython，外部解释器驱动）。
> 产出：方案/运行时模型/API 对称表/PyComponentBase/回调 thunk/parity py_demo/cookbook/验收/决策点。只做设计。

---

## 0. 总则

| 决策 | 取向 |
|---|---|
| 集成方式 | **外部解释器驱动（不嵌入 CPython）**——Python 进程=宿主：ctypes 直呼 rhygemaker.dll（复用 ms_bind.h 43 函数 C ABI——与 C# RhygeMaker.Bind 同一 ABI 层）；嵌入 CPython=**P1**（未来 in-process） |
| 语言版本 | **Python 3.14**（本机已有；类型注解+ctypes 完善） |
| 依赖 | **零第三方**（ctypes/struct/unittest/math=标准库）——红线延续（pytest=可选 P2） |
| 线程/GIL | 单线程（主线程）：Python 主线程驱动 engine.run_frame/tick → C++ 内回调（CFUNCTYPE）**同步**回 Python（GIL 自动重获——调用方持有；文档明示）；跨线程=MS_ERR_THREAD（防御同 C#） |
| ABI 前缀 | ms_ 保留（稳定 ABI——RhygeMaker 品牌下符号不变；py 侧导入 lib=rhygemaker.dll） |

---

## 1. Python 包结构（python/rhygemaker/——纯 Python 零依赖）

~~~
rhygemaker/
├─ __init__.py        # 公开面：Engine/Scene/GameObject/Transform/AssetStore/PyComponentBase/KeyCode
├─ _interop.py        # ctypes 绑定：cdll.LoadLibrary(rhygemaker)+43 函数签名+MS_* 错误码+ms_component_spec 结构
├─ engine.py          # GameEngine（门面——与 C# GameEngine.cs 对称）
├─ scene.py           # Scene/IScene
├─ component.py       # PyComponentBase（八回调基类）+_ComponentRegistry（全局 registry）
├─ gameobject.py      # GameObject（AddComponent/GetComponent/AddChild/SetActive/Destroy）
├─ transform.py       # Transform（pos/rot quat/scale/parent）
├─ assetstore.py      # AssetStore（Load/TypeOf/GuidOf/Unref/SaveText/SaveBinary）
└─ __main__.py        # py_demo 入口（--renderhash/--pf1/--pf2/--run-frames N）
tests/
└─ test_py_bind.py    # unittest（零依赖——fails=0；退出码=失败数）
~~~

---

## 2. API 对称表（Python ↔ C# RhygeMaker.Bind——method 一一对应）

| 语义 | C#（RhygeMaker.Bind） | Python（rhygemaker） |
|---|---|---|
| 门面 | GameEngine(Title/Width/Height/LoadScene/Run/SaveScene/LoadScene/Assets/Dispose) | GameEngine（同名属性/方法；__enter__/__exit__=Dispose） |
| 场景 | IScene/Scene（Build/Update/Root/Add） | Scene（build/update/root/add——snake_case，参数同 C#） |
| 组件 | ComponentBase（八回调虚方法+Owner+Enabled/Dispose） | PyComponentBase（Awake/OnEnable/Start/Update/FixedUpdate/LateUpdate/OnDisable/OnDestroy + owner/enabled；类=用户子类） |
| 对象 | GameObject（Id/Name/AddComponent/GetComponent/SetActive/Destroy/AddChild） | GameObject（id/name/add_component(cls)/get_component(cls)/set_active/destroy/add_child） |
| 变换 | Transform（Position/Rotation(quat)/Scale/Parent） | Transform（position/rotation/scale/parent——list[3]/list[4] 数值） |
| 资产 | AssetStore（Load/TypeOf/GuidOf/Unref） | AssetStore（load/type_of/guid_of/unref——Asset 句柄包装） |
| 错误码 | MS_* 常量 + 异常 | _interop 抛 BindException（MS_ERR_* 消息） |
| 键码 | KeyCode 枚举（C#） | KeyCode（enum.IntEnum——字面同 C#） |

**对称保证**：类/方法/参数名一一对应（diff=0 对照表入文档附录）；C# 语义（八回调/延迟 Destroy/引用计数）=完全一致。

---

## 3. 运行时模型

### 3.1 组件注册（对象引用保持——全局 registry）

~~~python
class _ComponentRegistry:                       # 全局（模块级单例）
    # key=component_id(跨语言唯一) → PyComponentBase 实例（强引用池——防 GC 回收）
    # 另维护 instanceKey（FullName#n——与 C# ComponentBridge 同款多实例安全）
    _instances: dict[int, PyComponentBase]       # component_id → 实例
    _by_key: dict[str, int]                      # MyComp#2 → component_id
    def add(self, inst): ...
    def release(self, comp_id): ...
    def __len__(self): ...
~~~

- C ABI userData = Python 对象指针（ctypes.c_void_p(id(inst))——**registry 强引用**保活；destroy=release（C# GCHandle 对称）。

### 3.2 回调 thunk（ctypes CFUNCTYPE——八回调→C 函数指针表）

~~~python
CALLBACK_VOID = ctypes.CFUNCTYPE(None, ctypes.c_void_p)                     # onAwake/OnEnable/Start/OnDisable/OnDestroy
CALLBACK_DT  = ctypes.CFUNCTYPE(None, ctypes.c_void_p, ctypes.c_double)    # Update/FixedUpdate/LateUpdate

class _ComponentSpec(ctypes.Structure):
    _fields_ = [('scriptType', ctypes.c_char_p), ('onAwake', CALLBACK_VOID), ... , ('userData', ctypes.c_void_p)]

def _thunk_void(fn_name):
    @CALLBACK_VOID
    def thunk(ctx):
        inst = _ComponentRegistry[ctx]
        getattr(inst, fn_name)()                   # GIL：主线程同步回 Python——调用方持有
    return thunk
def _thunk_dt(fn_name):
    @CALLBACK_DT
    def thunk(ctx, dt):
        getattr(_ComponentRegistry[ctx], fn_name)(dt)
    return thunk
~~~

**关键**：① thunk 引用=registry（CFUNCTYPE 对象全局保存——防 GC：_THUNK_TABLE[inst_id]）② GIL=调用方线程持有（文档：**调用回调=在主线程内同步**——无需 PyGILState（纯主线程）；PyGILState_Ensure 仅 P1 嵌入/跨线程使用）③ **线程模型声明**：Python 侧必须单线程驱动（禁用多线程调用 engine——MS_ERR_THREAD 防御）。

### 3.3 数据流（与 C# 一致）

每帧：Python engine.run_frame(dt) → C++ engine_tick → LifecycleDriver（八回调序 Unity）= 同步调用 Python 回调（thunks→registry 实例）→ Events flush → 返回；渲染=engine.render（render_hash 可读）。

---

## 4. py_demo（csdemo 对称——parity 跨语言三边）

| 模式 | 行为 | 断言 |
|---|---|---|
| --renderhash | 固定场景（MS_PARITY_SCENE——同 C++ 固定面 1280×800；Python build 同序列）→ 120 帧 → FNV（C 端 ms_engine_render_hash 直读——不重复实现哈希） | PY_RENDERHASH == 4634E387E024BE90（==C++/C#——三语言 parity） |
| --pf1 | Cookbook PF1（最小引擎程序） | exit=0+输出断言 |
| --pf2 | Cookbook PF2（八回调序断言） | 序列==官方 |
| --run-frames N | 任意帧驱动（调试） | exit=0 |

---

## 5. cookbook（Python 篇）

1. **PF1_Minimal.py**（约 12 行）：GameEngine → scene → add_component(Scorer) → run_frame×120 → 打印 score。每行注释 API 域。
2. **PF2_Lifecycle.py**（约 28 行）：PyComponentBase 八回调日志 → 断言序==官方（unittest 内建断言）。

---

## 6. 测试（unittest——零依赖）

- **test_py_bind.py**（tests/——unittest，退出码=失败数）：①句柄配对（create/destroy×5）②八回调序==Unity 官方（Python 侧精确）③Transform pos/rot quat/scale/parent 往返④资产句柄（纹理 .bmp load/type/guid/unref）⑤场景 save/load 往返（.mscene 容器——Python 侧经 ABI）⑥跨线程 MS_ERR_THREAD（threading 调用=防御断言）⑦错误码（TRAVERSAL_DENIED 等=BindException 消息）。
- **对称一致性**：C#/Python 同一 ABI 契约（ms_test_bind C++ 侧已证——Python 复用同一 DLL——无需重复 C++ 断言）。

---

## 7. 决策点（captain 拍板）

| # | 决策 | 建议 |
|---|---|---|
| P-1 | 嵌入 CPython | 外部解释器（本设计）；嵌入=P1（未来 in-process——决策后置） |
| P-2 | 解释器版本 | Python 3.14（本机；3.11+ 兼容=文档注——ctypes API 稳定） |
| P-3 | GIL/线程 | 单线程主线程（回调同步回 Python——调用方持 GIL）；跨线程=MS_ERR_THREAD；PyGILState=P1 嵌入/多线程（文档明示） |
| P-4 | ABI 前缀 | ms_ 保留（RhygeMaker 品牌下稳定 ABI——43 函数不重命名；C#/Python 共用） |
| P-5 | 依赖 | 零第三方（unittest/ctypes）；pytest=P2 可选 |
| P-6 | DLL 路径 | rhygemaker.dll（build/src/bind 实际产物；_interop.py 搜索=环境变量 RHYGEMAKER_DLL 或同目录；README 注） |
| P-7 | 对称保证 | API 对称表=契约（C#/Python diff=0——文档附录；未来 C# 侧新 API 同步 Python——维护约定） |

---

## 8. 验收清单（M2.6 全绿）

| # | 项 | 判据 |
|---|---|---|
| 1 | 导入/加载 | import rhygemaker（DLL 找到——不抛）；lib 加载=环境变量/同目录 |
| 2 | 生命周期 | py_demo --pf2 八回调序==Unity 官方（精确） |
| 3 | 对象/变换 | GameObject add_child/set_active/destroy（延迟）+Transform 往返（quat） |
| 4 | 组件注册 | add_component(cls) 多实例（FullName#n）+registry 长度（释放=destroy） |
| 5 | 资产 | AssetStore load(bmp)/type_of/guid_of/unref（引用计数） |
| 6 | 场景 | save_text/load round-trip（.mscene 容器）经 Python |
| 7 | 线程防御 | 非主线程调用→BindException(MS_ERR_THREAD) |
| 8 | 错误码 | TraversalDenied/UnknownField 等→BindException 消息 |
| 9 | parity | py_demo --renderhash == 4634E387E024BE90（三语言 parity：C++/C#/Python） |
| 10 | cookbook/测试 | PF1/PF2 运行 PASS；unittest fails=0 exit 0 |

---

## 9. 命名/历史说明

- 文档级：ms_bind.h/ms_ 前缀=**稳定 ABI**（历史名保留——RhygeMaker 品牌下不重命名 ABI；C++/C#/Python 三侧共用）；包/命名=RhygeMaker/Python rhygemaker。
- 与本层关系：C# 层=参照实现（API 对称）；Python=等价开发面（用户可 Python 编写项目）——**两开发层并存**（脚本语言选择=项目偏好）。

---

*t121 完成*（外部解释器+ctypes 复用 ms_bind 43 ABI；API 对称表；PyComponentBase 八回调+registry 保活+CFUNCTYPE thunk；py_demo 三语言 parity；cookbook PF1/PF2；unittest 零依赖；决策 P-1..P-7；验收 10 项）

> **实现后评审（t122，RhygeMaker 名下）：通过（10/10）**——Python 3.14.7 全绿（import ok/py_demo --renderhash PARITY OK（**三语言 parity**）/PF1 score=120/PF2 PASS）；test_py_bind.py 9 用例 OK exit=0（1.5s）；零依赖核实（ctypes/struct/unittest/math/threading/tempfile）；双源 111 文件 MD5 diff=0。**6 偏离全采纳/确认**（①DLL 加载=add_dll_directory（P-6 补强：os.add_dll_directory+ RHYGEMAKER_MINGW——现代加载语义）②userData=id(inst)+registry 强引用（=设计）③回调 snake_case（=对称表约定；C# camelCase↔Python snake 映射）④ms_ 前缀保留（P-4）⑤--renderhash 经 C 端直读（=设计）⑥旧 milestone-v3 路径废弃（改名承接）——无设计违反项）。