# RhygeMaker 使用教程（零基础可上手）

> RhygeMaker = 可编程游戏引擎（Unity 式：C++ 引擎核 + C# / Python 双开发层 + 六区编辑器）。本教程从「打开引擎」到「做出一个能玩的玩法」逐步引导。

---

## 1. 启动

- 双击桌面 **RhygeMaker编辑器-启动.bat** —— 打开六区编辑器（原生分辨率自适应、中文界面、文字清晰）。
- 双击 **RhygeMaker演示-启动.bat** —— 打开演示窗口（山/日/方块动画，Esc 退出）。
- 专业用法（可写进自定义启动器）：
  ```bat
  set PATH=C:\Users\etgya\AppData\Local\Microsoft\WinGet\Packages\BrechtSanders.WinLibs.POSIX.UCRT_Microsoft.Winget.Source_8wekyb3d8bbwe\mingw64\bin;%PATH%
  start "" "D:\Users\etgya\Desktop\rhygemaker\build\tools\RhygeMakerEditor.exe"
  ```

---

## 2. 六区编辑器速览（Unity 对照）

| 区域 | 名称 | 干什么 |
|---|---|---|
| 顶栏 | 工具栏(Toolbar) | ▶播放 / ■停止 / ⏸暂停 / 保存场景 / 加载 / **新建对象** / AI 开关 |
| 左 | 层级(Hierarchy) | 场景对象树（选择对象 → 右侧检查器） |
| 中 | 场景(Scene) | 物体可视化视图（网格/方块——Play 中可看运行） |
| 右 | 检查器(Inspector) | 选中对象的组件+字段（点字段可直接改——可折叠/可拖拽资源） |
| 下左 | 控制台(Console) | 运行日志（中文）——错误/警告/信息 |
| 下右 | 资产项目(Project) | 项目资产列表（.mscene/.msprefab/图片等） |

### 3. 第一个场景（30 秒）
1. 点 **新建对象(New)** → 左侧出现 `NewObject`，编辑器中显示方块。
2. 左侧选中它 → 右侧检查器改 `position/scale` 等字段 → 保存(SaveScene)。
3. 点 **加载(Load)** → 场景还原 = 保存生效。
4. 点 **播放(Play)** → 进入运行态（检查器只读/琥珀色，工具条保存新建变灰）→ 点 **停止(Stop)** → 快照还原（运行中改动丢弃）。

### 4. 资产
- 项目资产默认放 `Assets/`（相对项目根；.mscene/.msprefab/.bmp）。
- 谱面 **.mil** = JSON（音乐节奏谱面）——引擎可直接读取（chartio 权威解析 + 校验）。

---

## 5. 用代码写玩法（Python——最简路径）

引擎 = 通过代码编写游戏：组件（八回调）挂在场景对象上，由引擎驱动。

```python
from rhygemaker.engine import GameEngine
from rhygemaker.component import PyComponentBase

class 计分器(PyComponentBase):          # 组件 = PyComponentBase 子类
    def __init__(self):
        super().__init__()
        self.score = 0
    def update(self, dt):                # 每帧回调（Unity 式八回调之一）
        self.score += 1

with GameEngine() as engine:             # 引擎创建（窗口/软渲/输入……）
    obj = engine.root.add_child("玩家")   # 场景对象
    obj.add_component(计分器)             # 挂组件
    engine.run(frames=120, dt=1/60)      # 跑 120 帧
    print("得分:", obj.get_component(计分器).score)
```

运行（先在终端加环境）：
```powershell
$env:PYTHONPATH='D:UsersetgyaDesktophygemakerpython'
$env:RHYGEMAKER_DLL='D:UsersetgyaDesktophygemakeruildsrcindhygemaker.dll'
$env:RHYGEMAKER_MINGW='C:UsersetgyaAppDataLocalMicrosoftWinGetPackagesBrechtSanders.WinLibs.POSIX.UCRT_Microsoft.Winget.Source_8wekyb3d8bbwemingw64in'
python 你的脚本.py          # 或 python -m rhygemaker --pf1 看看官方示例
```

**八回调（顺序=Unity 官方）**：awake → on_enable → start → fixed_update → update → late_update → on_disable → on_destroy。

---

## 6. 用 C# 写玩法（完整项目风格）

项目：`引擎源码/rhygemaker/dotnet/Milestone.Game`（可直接参考）。
玩法链（页面栈状态机）：MainMenu → SongSelect → Play → Result；游玩区用组件（NoteSpawner / JudgementZone / LaneRenderer / MusicDriver / HudLayer），由 `RhygeMaker.Engine.Rhythm` 门面驱动（图表加载/规则集/判定 tracker——全部真接 C ABI；能力握手：域权威 OK 用域判定，否则托管镜像回退）。

自检/冒烟：
```powershell
dotnet run --project D:UsersetgyaDesktophygemakerdotnetMilestone.Game -- --selftest    # 21/21
dotnet run --project D:UsersetgyaDesktophygemakerdotnetMilestone.Game -- --abi-check  # 全链 1400 帧 acc=1.000
```

---

## 7. 命令行工具

| 工具 | 命令 | 作用 |
|---|---|---|
| 黄金帧 | `ms_demo --renderhash` / `--renderhash3d` | 渲染确定性校验（2D/3D） |
| 窗口演示 | `ms_demo --window` | 演示场景 |
| 谱面巡检 | `ms_rhythm --mil-check 谱面.mil` | 解析+统计（title/mode/keys/notes/dur） |
| 预设自检 | `ms_rhythm --preset mania4 谱面.mil` | 无头自动游玩（hit/acc——10 预设） |
| 预览渲染 | `ms_rhythm --preview mania4` | 四族渲染（Lane/Line/Ring/Path） |
| 预设列表 | `ms_rhythm --preset-list` | 10 玩法预设 |
| 三族示例 | cookbook_a_4k / b_phigros / c_arcaea 谱面.mil | 玩法示例（acc=1.0 + 渲染） |

```powershell
$env:PATH 加 mingwin + buildsrcind 后运行上述 exe（均在 build\tools 下）
```

---

## 8. 开发/构建（想改引擎）

- 源码双树：`D:UsersetgyaDesktophygemaker`（ASCII 构建源）↔ `引擎源码hygemaker`（源镜像，保持同步）。
- 构建：`cmd /c 构建产物uild-v3c.bat`（或 `cmake --build build` —— 先关掉运行中的编辑器否则链接占用）。
- 测试：`ctest --test-dir build`（需 PATH 加 mingw64in）；套件：core/platform/bind/editor/render3d/rhythm。
- 架构：Core（场景/组件/生命周期）← Platform（窗口/软渲/输入/音频）← App/Render3D/Editor（工具） ← Rhythm（域库：谱面/判定/规则集/渲染） ← Bind（C ABI：ms_* 43+15 函数） ← C#/Python 层。

---

## 9. 常见问题

| 现象 | 解决 |
|---|---|
| 双击无反应/黑屏 | 关闭再启；确认用 `build\tools\RhygeMakerEditor.exe`（勿用旧副本）；DLL 在 build\src\bind |
| 构建失败 Permission denied | 编辑器/演示进程在运行——先全部关闭再构建 |
| 中文路径报错 | 控制台 GBK：先 `chcp 65001`；引擎侧已支持（_wfopen） |
| 修改了源码看不到变化 | 双树未同步——从镜像复制或反向同步；重建看 build 最新产物 |
| 文字异常 | 反馈截图（我们将逐条修——当前=全量 GDI 清晰渲染） |

---

## 10. 更多资料

- 引擎 README：`引擎源码hygemakerREADME.md`
- 游戏层接线说明：`dotnetMilestone.GameREADME.md`
- 域库设计/验收：`编辑指南文档精选协作	132-m4-design.md`
- 原版功能对标：`编辑指南文档精选协作original-feature-matrix.md`
- 编辑指南合集（PDF 素材）：`编辑指南文档精选协作\*.md`
