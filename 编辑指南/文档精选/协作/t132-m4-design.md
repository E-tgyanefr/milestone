# t132 · M4 设计：Rhythm.* 域库 + 引擎版 Milestone 项目重制（与原版非引擎化功能对标）

> 起草：eng-design-vis（引擎策划）· 任务 t132 · 依据：engine-v3-cpp-spec.md §4（M4 原文）+ captain 扩展指令『引擎版 Milestone 项目重制（与原版非引擎化功能对标）』。
> 范围：① Rhythm.* 域库 C++ 本体（签名级）+ ms_rhythm_* C ABI + CLI --preset/--preview + cookbook A/B/C；② 引擎版 Milestone 项目（C# 开发层）重制设计：架构 + 功能 parity 矩阵 + 分阶段 + 引擎缺口（M4-X 渲染桥）+ 验收。只做设计不写码。
> 红线延续：域库零第三方；Core 零音游（V3-6 可插拔）；命名=RhygeMaker（历史文档/ms_ 前缀=稳定 ABI 保留）。

---

## ⚡ WIP 中间版（v0.9——captain 聚焦指令：先交付 域签名 + P0 映射表；全量细节=下文各节（尾注）；本版供 t134 骨架直接开工）

> 状态：**WIP**——本块=frozen 的 v0.9 主体（可直接引用）；后续细节/偏离一律记入下文/§11，不回头改本块（除非回归性错误）。
> 引用：mil_probe 实证（captain——msjson 直读 .mil 可跑通）✅ 已定位：**D:/Users/etgya/Desktop/rhygemaker/mil_tool/main.cpp + mil_probe.exe**（编译：g++ -std=c++20 -I rhygemaker\include；运行：mil_probe.exe '输出产物\bin\Chart\Milestone示例\Mania 4K 示例.mil' → format=milestone-1 mode=mania bpm=120 keys=4 notes=20 holds=4 MIL_OK）——工具目录（非 编辑指南 下）；域 chartio 解析路径按 mil_probe 验证口径=msjson(direct)+MSASSET 容器化（A-3'）。

### W-1 Rhythm.* 域签名（Chart / .mil / 时间轴 / 判定四档 / 规则集四族）

命名空间 `RhygeMaker::Rhythm`；目录 `include/rhygemaker/rhythm/ + src/rhythm/`；**数据平面=纯数据无 Core**（§1）；msjson/math=Util（M4-a）；零第三方。

```cpp
// ---- model.hpp：Chart 模型 ----
struct StageSpec { int Id; std::string Name, Mode; int KeyCount; double X,Y,W=1,H=1; };
class ChartData {
public:
  std::string Title, Artist, DifficultyName;   double AudioOffsetMs = 0;
  BpmTimeline Bpm;                              std::vector<RhythmNote> Notes;
  std::vector<StageSpec> Stages;                std::vector<PlayfieldLine> Lines;
  double StartTimeMs() const; double EndTimeMs() const;
  void SortNotes();                              std::span<const RhythmNote> NotesInRange(double t0, double t1) const;
  DifficultyStats RecomputeStats(int windowMs = 2000) const;
};
class RhythmNote {                              // 子类 SliderNote/RingNote/SpinNote/KnobNote（+Type 判别）
public:
  double TimeMs=0, EndMs=0; double Lane=-1; int StageId=0; double X=0.5, Y=0.5;
  std::string Type="tap"; std::any Tag;
  bool IsLong() const; double DurationMs() const;
  Judgement Judge(const JudgementProfile&, double offsetMs) const;
  bool HitHead(double nowMs, double windowMs); bool HitTail(double nowMs, double windowMs);
  bool KeepHolding(double nowMs) const; void BreakHold(); void ResetHold();
  bool HeadHit() const; bool TailHit() const; bool BodyBroken() const; bool HoldComplete() const;
};

// ---- chartio.hpp：.mil（milestone-1 JSON）----
struct MilParseResult { std::optional<ChartData> Chart; std::string Error; int Line = 0; };
MilParseResult ParseMilText(std::string_view json, const std::string& sourcePath);
std::string    SerializeMil(const ChartData&);          // round-trip：默认字段省略+数组序=参考
std::optional<ChartData> LoadMilFile(const std::string& path, std::string* err);
// 字段契约（mil_probe 实测已核）：format=milestone-1/mode/title/artist/version/bpm/offset/audio/keys/
//   notes[{t,c,e,type(hold|arc),ec}]/events[{t,e,type(moveY|rotate...),v,ev}]
// 容器双态（A-3'）：磁盘=原文 JSON；引擎内=MSASSET 容器(type=mil+guid+payload+FNV 尾部)——SceneSerializer 机制复用

// ---- beat.hpp：时间轴 ----
struct BpmChange { double TimeMs; double Bpm; double BeatsPerMeasure; };
class BpmTimeline {
public:
  BpmTimeline(double initialBpm = 120, double beatsPerMeasure = 4);
  void AddChange(double timeMs, double bpm, double beatsPerMeasure = 4);  // 排序插入；同刻替换(1e-9)
  double BpmAt(double timeMs) const; double MeasureAt(double timeMs) const;
  double TimeToBeat(double timeMs) const; double BeatToTime(double beat) const;
  double TimeToMeasure(double timeMs) const; double MeasureToTime(double measure) const;
  double SnapToBeat(double timeMs, int subdiv) const;   // 变速感知吸附
};   // 注意：惰性重建缓存 + Banker's rounding（对拍 v1 Math.Round）

// ---- judgement.hpp：判定四档 ----
struct JudgeTier { std::string Name; double WindowMs; double Score; double Weight; bool BreaksCombo; };
class JudgementProfile {
public:
  const JudgeTier* Evaluate(double offsetMs) const; bool IsHit(double offsetMs) const;
  double MissWindowMs; double WindowMultiplier = 1.0;   // ×0.25..4 钳制（EN-2）
  void SortTiers();
  static JudgementProfile Arcaea(); Phigros(); Iidx(); Cytus(); Adofai(double bpm);
  static JudgementProfile OsuMania(double od); OsuStandard(double od); Sdvx(); Maimai();
  Chunithm(); Taiko(); Catch(); Cytus2(); GrooveCoaster(); Lanota(); Dynamix();
};
struct Judgement { const JudgeTier* Tier;   // null=MISS
  double OffsetMs; double NoteTimeMs; bool IsHit() const;
  std::string TierName() const; int Score() const; bool BreaksCombo() const; };
class JudgementTracker {
public:
  JudgementTracker(const ChartData&, const JudgementProfile&, ScoreBoard* board = nullptr);
  void SetProfile(const JudgementProfile&);             // 运行时热换（t6 契约）
  void Update(double nowMs);
  Judgement Judge(const RhythmNote&, double offsetMs);
  std::optional<Judgement> PressLane(double lane, double nowMs);
  std::optional<Judgement> PressRing(const Vec2&, const RingField&, double nowMs);
  std::optional<Judgement> PressField(const Vec2&, const LineField&, double hitRadius, double nowMs);
  bool HoldLane(double lane, double nowMs, const RhythmNote*& held);
  void Reset(); int JudgedCount() const; int Remaining() const;
  std::function<void(const Judgement&)> OnJudged;       // 事件（宿主桥 EventBus）
};
class ScoreBoard { /* Score/Combo/MaxCombo/HitCount/MissCount/Accuracy/Tp/Rank/Apply*/ };
class JudgementLog { /* 滚动 200 可视化 */ };

// ---- ruleset.hpp：规则集四族 ----
struct RulesetResult { int HitCount, MissCount, TotalNotes; double Accuracy; bool Perfect() const; };
struct InputState { std::array<bool,256> Down; std::vector<Vec2> Touch; bool DownK(int vk) const; void EndFrame(); };
class ChartContext { ChartData Chart; JudgementProfile Profile; ScoreBoard Board;
  /* RhythmClock */ double NowMs() const; void SetTime(double sec); void Advance(double dt);
  JudgementTracker Tracker; InputState Input;
  std::function<double()> ExternalTimeMs;   // 音频同步锚（AudioClock→§6.1）；Rate（练习变速）};
class Ruleset { public: virtual ~Ruleset(); std::string Name;
  virtual void BuildField() = 0;                            // 数据平面（无 Core Scene）
  virtual RulesetResult RunHeadless(ChartContext&) = 0;     // 无头判定闭环
protected: RulesetResult RunAuto(ChartContext&, void(*play)(const RhythmNote&, double, void*), void* ud); };
class LaneRuleset : Ruleset { /* LaneField/TimeDepthMapper/LaneCount/KeyBase/KeyMap/slant/speed */ };
class LineRuleset : Ruleset { /* LineField+判定线映射 */ };
class RingRuleset : Ruleset { /* RingField+扇区映射 */ };
class PathRuleset : Ruleset { /* PathField（bezier/路径）*/ };
enum class FieldType { Lane, Line, Ring, Path };
struct RulesetDescriptor { std::string Id, Name, ModeId; FieldType Field; int Keys;
  std::string KeyHint; JudgementProfile MakeProfile(); Ruleset MakeRuleset(const ChartData&, int keys); };
```

**验收摘（全量=§2.6 R-1..R-10）**：R-1 时间轴 4 组 / R-2 Profile 16 工厂+Multiplier / R-3 长条状态机 8 用例 / R-4 四族 RunHeadless 10 预设 vs legacy 1:1（acc=1.000）/ R-5 .mil round-trip 3+ 样本逐键（前置：G9 custom type round-trip 修复——test_g9_red.exe 已备） / R-6 Tracker 热换+事件序。

### W-2 功能矩阵 P0 映射表（曲库→选歌→游玩→判定→结果）

| 步 | 原版证据（游戏源码/源码/） | 引擎版落点 | 可复用引擎能力 | 状态 | 验收 |
|---|---|---|---|---|---|
| 曲库 | ChartParser.cs L3-4（exts/zip）+ZipImport +FolderPanel | AssetDatabase List+容器化 .mil（A-3'）+域 ChartIO 解析 | ✅ AssetDatabase（root/List/Load）；msjson 直读 .mil（mil_probe 实证口径） | 🔧 P0 扫描/解析（.mil）；导入器=P1 | G-6：200 谱扫描基线 34ms 参考+空库文案 |
| 选歌 | 功能复刻对比清单 §2+SongCardView（分类 chips/卡片/单击·双击/▶✏🎲/空库文案） | SongSelectPage（PageStack+Widgets+UiText 逐字） | ✅ PageStack+Widgets（ms_rnd_*+ms_text_*） | 🔧 | G-2：文案逐字+操作流 diff |
| 游玩 | GamePanel.Play/Hud/Editor（判定线/滚动/HUD/判定区/暂停/跳过/重开） | PlayScene 组件化：LaneRenderer+NoteSpawner+JudgementZone+MusicDriver（§6.2/§9 签名） | ✅ IRenderable 通行证+IRenderer 10 原语+域 FieldRenderer 四族+AudioClock 锚 | 🔧 | G-3/G-7/G-11：HUD 数值对拍+RunAuto acc=1.000+组件生命周期序 |
| 判定 | GamePanel.Play.cs L214-246（MultiKeyDown）+JudgeSettings | 域 JudgementProfile（四档）+Tracker（Press*/Update/热换） | ✅ 域判定（W-1）+Settings 档位热换（SetProfile） | 🔧 | G-1：同谱同偏移==legacy 判定数/ACC/得分/评级/时间分布 1:1；G-5 档位热换 |
| 结果 | GamePanel.Result.cs L16-54（SumHits/CytusTp）+ScoreBoard | ResultPage（域 ScoreBoard.Score/Accuracy/Tp/Rank 直取；CytusTP 公式端口） | ✅ 域 ScoreBoard（W-1） | 🔧 | G-4：Score/Rank/Tp 同谱一致（含 CytusTP） |

---



---

## 0. 背景与定位

- v3 规格 M4 原文：『Rhythm.*（JudgementProfile/Ruleset 四族/StageSpec/Storyboard/Practice——端口 v1 域库，零托管化）+cookbook A/B/C（4K/Phigros/Arcaea 原生实现）+CLI --preset/--preview』；验收=『域库 10 预设 100% 命中（端口断言）；cookbook 编译+运行；--preset acc=1.000；--preview 四族可视』。
- captain 扩展：M4 需同时给出『引擎版 Milestone 项目重制』——即把原版非引擎化 Milestone.exe（游戏源码/源码/，WinForms+D2D，31.8K 行）的功能逐项在 v3 引擎上重做，行为与原版对标（判定/文案/数值/操作流），作为引擎的 example 项目闭环。
- 本设计交付两件事：**A=RhygeMaker::Rhythm 域库规格**（M4 本体）、**B=引擎版 Milestone 项目重制方案**（架构+parity 矩阵+分阶段+缺口），并给出供 captain 拍板的决策点（A-1..A-8 / B-1..B-8）。

---

## 1. 总体架构（域库层叠加）

```
┌────────────────────────────────────────────────────────────────────┐
│ projects/milestone/（C# 开发层示例项目 = 引擎版 Milestone 重制）      │
│   页面栈（Menu/SongSelect/Play/Result/Settings/Folder/Calibration）│
│   + RhygeMaker.Bind.Rhythm 代理（ms_rhythm_* P/Invoke）            │
│   + 渲染：OnRender 全委托（M4-X 桥：ms_rnd_*+ms_text_*）             │
├────────────────────────────────────────────────────────────────────┤
│ bind 层 ms_bind.h（稳定 C ABI——M2.5 已有 47 函数 + M4 增 ms_rhythm_*）│
├────────────────────────────────────────────────────────────────────┤
│ Rhythm.*（RhygeMaker::Rhythm——C++ 域库，零第三方）                   │
│   Chart/BPM/Note/Field/Ruleset 四族/Judgement/Story/Practice/      │
│   Validator/Preset/Mode/.mil ChartIO/FieldRenderer                 │
│   依赖：Util（msjson+math 共享头） + Platform::IRenderer（仅接口）     │
│   ★ 不依赖 Core（零引用断言——V3-6 可插拔红线）                        │
├────────────────────────────────────────────────────────────────────┤
│ Core（场景/组件/生命周期/资产/反射——零音游）/ Platform（Win32/软渲/    │
│   WinMM/输入）/ Render3D / Editor / Tools                            │
└────────────────────────────────────────────────────────────────────┘
```

- **数据平面/实例平面分离**：域库核心=纯数据结构+纯函数（ChartData/ChartContext/Ruleset/RhythmNote 等无 Core 类型）；Ruleset.Build(Scene)（v1 依赖 Core Scene/Component）**数据平面化**——Ruleset 产出 Field 描述与运行时状态，Core 配对（RulesetAnchor 组件/渲染）=示例层适配器（cookbook/重制项目内实现）。这是『Core 零引用』成立的关键。
- **渲染归属**：域库收 FieldRenderer（参考 RulesetRenderer.cs 四族）——依赖 Platform::IRenderer 接口（仅抽象接口，不含 Win32/软渲实现）；C++ 宿主直接调；C# 经 ms_rhythm_render_field 在录制面调度（见 §3.3 X1）。
- **共享 Util（M4-a 前置）**：msjson（零第三方 JSON——.mil=milestone-1 JSON 需要）与 math（Vec2/Vec3/Quat/Mat4）目前位于 core/assets/ 与 core/，域库引用即违反零引用。**M4-a：提取为 RhygeMaker::Util（include/rhygemaker/util/msjson.hpp·math.hpp，header-only）**；Core 保留旧路径转发（行为零变，双源 MD5 校验后切换）。

---

## 2. A 部分：Rhythm.* 域库规格（签名级）

### 2.1 目录与构建

```
引擎源码/rhygemaker/
  include/rhygemaker/rhythm/{beat,model,judgement,field,ruleset,story,practice,
                            validator,preset,mode,chartio,render}.hpp
  src/rhythm/*.cpp        （CMake 目标 rhygemaker_rhythm 静态库；链接 rhygemaker_util）
  tests/rhythm/test_main.cpp + test_*.cpp   （ms_test）
  工具：tools/ms_rhythm/main.cpp（--preset/--preview/--mil-check）
  cookbook：tools/cookbook/{a_4k,b_phigros,c_arcaea}/main.cpp（3 个独立 exe 目标）
```

- 命名空间：RhygeMaker::Rhythm（子域=历史 Milestone::Domain::Rhythm 的 v3 正名）。
- 依赖规则（红线）：Rhythm→Util+Platform(IRenderer 接口)；Rhythm→Core ✗；Core/Platform→Rhythm ✗；Editor/Tools/项目→Rhythm 可。
- 零分配热路径目标（PerfGate）：判定循环（Tracker.Update/Press*/ScoreBoard.Apply）0 分配；NotesInRange 二分 0 分配。

### 2.2 模块明细（每模块=源参考 + 关键类型/签名 + 端口注意）

#### rhythm/beat.hpp —— BPM/拍/吸附
源参考：引擎源码/engine/ChartModel.cs L9-136（BpmChange/BpmTimeline）、RhythmCore.cs L191-220（BeatMath）。
```cpp
struct BpmChange { double TimeMs; double Bpm; double BeatsPerMeasure; };   // BeatsPerMeasure 缺省 4
class BpmTimeline {                                                       // 变速时间轴（拍坐标插值）
public:
  BpmTimeline(double initialBpm = 120, double beatsPerMeasure = 4);
  void     AddChange(double timeMs, double bpm, double beatsPerMeasure = 4); // 排序插入；同刻替换（1e-9）
  double   BpmAt(double timeMs) const;         // 段内 BPM
  double   MeasureAt(double timeMs) const;     // 拍号分母
  double   TimeToBeat(double timeMs) const;    // 谱面 ms → 拍坐标
  double   BeatToTime(double beat) const;      // 拍坐标 → 谱面 ms
  double   TimeToMeasure(double timeMs) const;
  double   MeasureToTime(double measure) const;
  double   SnapToBeat(double timeMs, int subdiv) const;  // 变速感知吸附（当前段 BPM）
  size_t   Size() const;  const BpmChange& At(size_t) const;
private: /* _changes + _segStartBeat 惰性重建缓存 */ };
namespace beatmath { BeatMs/MeasureMs/BeatCount/MeasureCount/SnapToBeat/SnapToMeasure; }
```
端口注意：① 同刻替换语义（1e-9）；② _segStartBeat 惰性重建（AddChange 后计数不符才重算）；③ SnapToBeat=round 半格（Math.Round 银行家舍入 vs C++ round——**四舍五入对齐口径：v1 用 Math.Round（银行家）→ C++ 必须独立实现 banker's rounding**，写入测试：BankerAtHalf 对拍）。

#### rhythm/model.hpp —— 谱面容器 + 音符族
源参考：ChartModel.cs L140-240（StageSpec/ChartData/DifficultyStats）、RhythmCore.cs L271-520（RhythmNote/SliderNote/RingNote/SpinNote/KnobNote）。
```cpp
struct StageSpec { int Id; std::string Name, Mode; int KeyCount; double X,Y,W=1,H=1; };   // 0..1 归一化矩形
struct DifficultyStats { int NoteCount; double DurationMs, AverageDensityPerSec, PeakDensityPerSec;
                         int MaxSimultaneous; double LongNoteRatio; };
class ChartData {
public:
  std::string Title, Artist, DifficultyName;
  double AudioOffsetMs = 0;                       // 校准偏移（写谱起始偏差）
  BpmTimeline Bpm;                                // 变速
  std::vector<RhythmNote> Notes;                  // 音符（含子类——std::variant 或多态+Type 判别）
  std::vector<StageSpec> Stages;                  // 多场同屏；空=单主场
  std::vector<PlayfieldLine> Lines;               // 判定线（Phigros/Cytus 类）
  double StartTimeMs() const; double EndTimeMs() const;
  void SortNotes();                               // 命中时间升序（查询前置）
  std::span<const RhythmNote> NotesInRange(double t0, double t1) const;  // 二分 O(log n+k)
  DifficultyStats RecomputeStats(int windowMs = 2000) const;             // 确定性（含并行可选=JobSystem 版）
};

class RhythmNote {   // 基类（tap/hold 通用）
public:
  double TimeMs=0, EndMs=0;    double Lane=-1;       // 轨道列；-1=自由场
  int    StageId=0;            double X=0.5, Y=0.5;  // 自由场归一化坐标（Arcaea 缺省 y=1.0）
  std::string Type="tap";      std::any Tag;         // 宿主自定义（A-8 决策）
  bool IsLong() const; double DurationMs() const;
  Judgement Judge(const JudgementProfile&, double offsetMs) const;
  // 长条分段状态机（头/保持/尾/断开）
  bool HitHead(double nowMs, double windowMs); bool HitTail(double nowMs, double windowMs);
  bool KeepHolding(double nowMs) const; void BreakHold(); void ResetHold();
  bool HeadHit() const; bool TailHit() const; bool BodyBroken() const; bool HoldComplete() const;
};
struct SliderNote : RhythmNote { /* 路径点 + tick（HitTick/ResetTicks）+ HOLD 分段 */ };
struct RingNote   : RhythmNote { /* SectorAngle/AccumulatedAngle/SlideProgress/TargetAngleAt/AngleError/
                                    InTolerance/AddRotation —— 滑星（maimai）*/ };
struct SpinNote   : RhythmNote { /* 转盘点（IIDX 转盘）*/ };
struct KnobNote   : RhythmNote { /* 旋钮（SDVX FX 长旋——P1：SDVX=Removed 玩法——登记不阻塞）*/ };
```
端口注意：① 音符多态=std::variant 还是继承+RTTI——**建议：继承+虚析构+Type 字段判别（端口 v1 友好；variant 大修改风险高）**；② 长条状态机必须逐方法对拍（HitHead 窗口含端点）；③ Tag=std::any（域不解释）；④ EndMs 语义=点键 EndMs==TimeMs。

#### rhythm/judgement.hpp —— 判定域（核心）
源参考：RhythmCore.cs L9-160（JudgeTier/JudgementProfile/Judgement）、JudgementTracker.cs、JudgementLog.cs、ScoreBoard.cs。
```cpp
struct JudgeTier { std::string Name; double WindowMs; double Score; double Weight; };
class JudgementProfile {       // 16+ 工厂 + 自定义
public:
  JudgeTier& Tier(int i); const JudgeTier* Evaluate(double offsetMs) const;  // 窗口扫表
  bool IsHit(double offsetMs) const;
  double MissWindowMs; double WindowMultiplier = 1.0;   // EN-2：×0.25..4 钳制（t6 已实现于 C# 参考）
  void SortTiers();
  static JudgementProfile Arcaea(); Phigros(); Iidx(); Cytus(); Adofai(double bpm);
  static JudgementProfile OsuMania(double od); OsuStandard(double od); Sdvx(); Maimai();
  Chunithm(); Taiko(); Catch(); Cytus2(); GrooveCoaster(); Lanota(); Dynamix();
};
struct Judgement { const JudgeTier* Tier; double OffsetMs; double NoteTimeMs; bool IsHit; };
class JudgementTracker {       // 事件驱动热换（EN-1）
public:
  JudgementTracker(const ChartData&, const JudgementProfile&, ScoreBoard* board = nullptr);
  void SetProfile(const JudgementProfile&);            // 运行时热换（t6 契约）
  void Update(double nowMs);                            // 扫查 Miss（+Skip: WindowStartMs 段界跳过）
  Judgement Judge(const RhythmNote&, double offsetMs);
  std::optional<Judgement> PressLane(double lane, double nowMs);   // 二分下界（t9 修复语义）
  std::optional<Judgement> PressRing(const Vec2& touch, const RingField&, double nowMs);
  std::optional<Judgement> PressField(const Vec2& world, const LineField&, double hitRadius, double nowMs);
  bool HoldLane(double lane, double nowMs, const RhythmNote*& held);
  void Reset();  int JudgedCount() const; int Remaining() const;
  // 事件（回调 vs EventBus——域零 Core：std::function 回调表；宿主桥 EventBus=E4）
  std::function<void(const Judgement&)> OnJudged;
  std::function<void(const Judgement&, const RhythmNote&)> OnJudgedDetail;
  double WindowStartMs = -INF;
};
class JudgementLog { /* 滚动 200：均值/标准差/打早打晚/9 桶直方图/每轨热图（端口） */ };
class ScoreBoard {
public:
  ScoreBoard(double maxScorePerNote = 300, int totalNotes = 0, int comboBonusMax = 100);
  void Apply(const Judgement&); void ApplyTick(const JudgeTier&); void ApplyHoldTail(const JudgeTier&);
  double Score; int Combo, MaxCombo, JudgedCount, HitCount, MissCount, TickCount;
  double ScoreRatio; double Accuracy; double Tp; std::string Rank; double MaxScorePerNote;
  int CountOf(const std::string& tierName) const; void Reset();
};
```
端口注意：① Evaluate 扫表顺序=SortTiers 后（WindowMs 升序；同窗=后定义覆盖？v1 语义=首个命中）——对拍锁定；② MissWindowMs 与 Tier 窗口关系（Miss=最大窗口外的判定档）——按 v1 逐字段；③ ScoreBoard 计分公式（组合加成 comboBonusMax=宿主口径 100——t42 parity 参数）写出公式引用 v1 行号；④ Rank 阈值表=字符串映射（逐字端口）。

#### rhythm/field.hpp —— 场几何（Lane/Line/Ring/Path）
源参考：RhythmCore.cs L224-269（LaneField）、Playfield.cs（LineField/RingField/PlayfieldLine）+ Path（PathField 在规则集中——按参考实现端口）。
```cpp
class LaneField { double OriginX, LaneWidth, LaneCount, TrackWidth;
  double LaneCenter(double lane) const; double LaneToX(double lane, double frac = 0.5) const;
  double XToLane(double x) const; double FreeX(double nx) const; double NormalizeX(double x) const;
  double ClampLane(double lane) const; };
class LineField { std::vector<PlayfieldLine> Lines;
  PlayfieldLine& AddLine(const Vec2& pos, double rotDeg = 0); Vec2 Normalize(const Vec2&) const;
  Vec2 Denormalize(const Vec2&) const; const PlayfieldLine* NearestLine(const Vec2&) const;
  bool InHitBand(const PlayfieldLine&, const Vec2&) const; };
class RingField { Vec2 Center; double Radius=400; int SectorCount=8; double StartAngleDeg=0; bool Clockwise=true;
  double SectorAngle(int i) const; Vec2 SectorPoint(int i) const; double AngleToSector(double) const;
  int ClampSector(double) const; double SectorToAngle(double) const; int SectorIndex(const Vec2&) const;
  double AngleOf(const Vec2&) const; bool Contains(const Vec2&, double tol = 0) const;
  double ArcLength(double fromDeg, double toDeg) const; static double ShortestAngleDelta(double,double);
  double AngleDelta(double fromDeg, double toDeg) const; };
```
端口注意：顺时针符号 Sign=Clockwise?1:-1；ShortestAngleDelta=±180 归一化——数学对拍断言（±179/±181 边界）。

#### rhythm/ruleset.hpp —— 玩法族（四族 + 无头闭环）
源参考：Ruleset.cs（全）、RulesetFactory.cs（Descriptor/FieldType/ChartSeedOptions/工厂）。
```cpp
struct RulesetResult { int HitCount, MissCount, TotalNotes; double Accuracy;
  bool Perfect() const; /* hit==total && miss==0 && acc>0.999999 */ };
struct InputState { std::array<bool,256> Down; std::vector<Vec2> Touch;   // 域内输入（A-6：宿主桥入）
  bool DownK(int vk) const; void EndFrame(); /* 边沿=宿主 EndFrame 时机 */ };
class ChartContext { ChartData Chart; JudgementProfile Profile; ScoreBoard Board;
  RhythmClock Clock;                          // 域内时钟（NowMs 可设；真实时间=宿主↔AudioClock 桥）
  JudgementTracker Tracker; InputState Input;
  double NowMs() const; void SetTime(double sec); void Advance(double dt); };
class Ruleset { public: virtual ~Ruleset(); std::string Name;
  virtual void BuildField() = 0;             // 数据平面：构造 Field/映射（无 Core 场景）
  virtual RulesetResult RunHeadless(ChartContext&) = 0;   // 无头判定闭环（虚拟时钟驱动）
protected: RulesetResult RunAuto(ChartContext&, void(*play)(const RhythmNote&, double, void*), void* ud); };
class LaneRuleset : Ruleset { /* LaneField/TimeDepthMapper/LaneCount/KeyBase/KeyMap/slant/speed */ };
class LineRuleset : Ruleset { /* LineField + 判定线映射 */ };
class RingRuleset : Ruleset { /* RingField + 扇区映射 */ };
class PathRuleset : Ruleset { /* PathField（bezier/路径）*/ };
class RulesetRunner { static RulesetResult RunHeadless(Ruleset&, ChartData&, const JudgementProfile&, int comboBonusMax); };
```
源参考补充：RulesetFactory——```cpp
enum class FieldType { Lane, Line, Ring, Path };
struct RulesetDescriptor { std::string Id, Name, ModeId; FieldType Field; int Keys;
  std::string KeyHint; JudgementProfile MakeProfile(); Ruleset MakeRuleset(const ChartData&, int keys); };
RulesetDescriptor* RulesetFactory::Find(const std::string& modeId);   // 注册表（一模式一描述符）
```
端口注意：RunAuto 逐音符推进（SetTime(t)→Tracker.Update(t)→play(t)→EndFrame）——判定计数确定性必须与 C# 参考逐位一致（浮点序=重算累计顺序不变）。

#### rhythm/story.hpp —— Storyboard 域
源参考：Storyboard.cs（StoryEventType/StoryEvent/StoryFrame/StoryCameraPose/Storyboard/StoryboardPlayer）。
```cpp
enum class StoryEventType { Fade, Slide, Wipe, CircleReveal, Beam, Camera, /* v1 全集 */ };
struct StoryEvent { double TimeMs, EndMs; StoryEventType Type; std::string Key; /* 目标/参数 */ };
struct StoryFrame { double TimeMs; /* 镜头/图层快照 */ };
class Storyboard { std::vector<StoryEvent> Events;   // 时间排序
  void ResetTo(double nowMs);                        // 回跳语义（t6 EN-4）
  double HookFired() const; double TotalHooks() const; double HookRate() const;
  const StoryFrame* FrameAt(double nowMs) const; };
class StoryboardPlayer { void Play(); void Pause(); void Seek(double ms);
  void Update(double dt); double Time() const; };
```
端口注意：HookFired 计数语义（触发一次算一次？）——逐方法对拍 t6 断言。

#### rhythm/practice.hpp —— 练习模式
源参考：PracticeSession.cs。
```cpp
struct SegmentScore { /* 分段结果 */ };
class PracticeSession {
public:
  PracticeSession(const ChartData&, JudgementProfile);
  void StartSegment(double fromMs, double toMs, double rate = 1.0);
  void Update(double nowMs); double Rate() const; void Ghost(bool);
  bool SegmentCompleted() const; SegmentScore Latest() const;
  /* 段循环自动重开 / 分段分不回写全局 / 幽灵回放 */ };
```

#### rhythm/validator.hpp —— 谱面校验
源参考：ChartValidator.cs。
```cpp
enum class IssueSeverity { Error, Warning, Hint };
struct ChartIssue { IssueSeverity Severity; std::string Code, Message; int Line; int NoteIndex; };
struct ValidatorOptions { /* 模式（mania/osu/...）、阈值 */ };
class ChartValidator {
public:
  static std::vector<ChartIssue> Validate(const ChartData&, const ValidatorOptions&);
  static bool HasError(const std::vector<ChartIssue>&);
  /* 30 项规则（v1 语义——AI 助手 R-01..R-12 规则复用本域校验子集，见 t130）*/};
```

#### rhythm/preset.hpp —— 10 预设注册表（验收核心）
源参考：EnginePresets.cs（PresetLayout/PresetBackground/EngineModePreset/EnginePresets）。
```cpp
struct PresetLayout { /* 场/判定线/背景令牌 */ };
struct PresetBackground { /* 背景令牌 */ };
struct EngineModePreset { std::string Id, Name; int Keys; JudgementProfile Profile;
  RulesetDescriptor* Descriptor; ChartData BuildSampleChart() const; };
class EnginePresets {
public:
  static const std::vector<EngineModePreset>& All();
  static const EngineModePreset* Get(const std::string& id);   // mania/maimai/phigros/arcaea/cytus/osustd/adofai/iidx/(+taiko/catch/lanota/dynamix 注册但 Removed)
  static ChartData BuildSampleChart(const std::string& id);
  static RulesetResult RunHeadless(const std::string& id);     // acc 返回
  static std::string ToDocTable(); };
```

#### rhythm/mode.hpp —— 玩法元数据注册表（原版单源端口）
源参考：游戏源码/源码/CoreUtil/ModeSystem.cs（20 条全端口）。
```cpp
enum class GameMode { Mania, Maimai, Wacca, Phigros, Arcaea, Cytus, Deemo, OsuStandard,
  Taiko, Catch, Sus, Sdvx, MuseDash, Adofai, AdofaiReal, Rotaeno, Dynamix, Lanota,
  ToneSphere, Iidx, Pump, LoopComposer };   // 序号与 v1 枚举一致（序列化兼容！）
struct ModeInfo { GameMode Mode; std::string Id, Display, KeyHint; int Keys;
  bool Tracked, Adjustable, Removed, Editable; };
class ModeRegistry { static const ModeInfo* Find(GameMode); static const ModeInfo* FromId(const std::string&);
  static const std::vector<ModeInfo>& All(); /* Available=!Removed */ };
```

#### rhythm/chartio.hpp —— .mil（milestone-1 JSON）解析/序列化
源参考：游戏源码/源码/Charting/ChartParserExtra.cs L1030-1480（ParseMil/SerializeMil + AddMilNotes/AddMilEvents/AddMilLineParents/AddMilLineMeta）+ 引擎源码/engine/Tool/ChartImport.cs（子集参考）。
```cpp
struct MilParseResult { std::optional<ChartData> Chart; std::string Error; int Line = 0; };
MilParseResult ParseMilText(std::string_view json, const std::string& sourcePath);
std::string    SerializeMil(const ChartData&);      // round-trip：默认字段省略、数组序=参考
std::optional<ChartData> LoadMilFile(const std::string& path, std::string* err);
```
- **键清单（M4 全格式）**：根=format("milestone-1")/mode/title/artist/version/bpm/offset/od/dr/ar/audio/tickRate/keys/danName/danSet/notes/events/lineParents/lineMeta/parts/stages；音符=t,e,c,x,y,ex,ey,ec,type,f(场)；parts=name/mode/keys+notes/events/lineParents/lineMeta；stages=x,y,w,h,name,keyMap（键=Win32 VK 名）。**端口实现必须逐键对照 ChartParserExtra 源码**（keys 全集以源码为准，本表为索引）。
- 解析=msjson（Util）→ 域模型；错误=行号（JsonElement 行号透传=ChartImport 语义）。
- **范围决策（A-3）**：.mil 全格式=M4；.osu/.adofai/.aff/.qua/.mc/.sm/.ssc/.json(phigros/cytus) 导入=P1（按 ChartParser 家族映射表，M4 只留钩子）。
- **容器托管（A-3' 修订——captain 蓝本指令『.mil=msjson 基础资产（MSASSET 容器+FNV 校验托管）』）**：磁盘 .mil 保持原样（JSON 文本——与原版谱面库逐字节兼容）；**AssetDatabase 加载=透明容器化**（Load→MSASSET 容器：'MSASSET1'+type=mil+guid+payload(原文)+FNV 尾部校验——复用 SceneSerializer 容器机制）→Rhythm::Chart 解析 payload；Save=回写原始 JSON（round-trip 原版兼容）。域不感知容器（ChartIO 对 payload 工作）。

#### rhythm/render.hpp —— 域渲染器（四族）
源参考：引擎源码/engine/Tool/RulesetRenderer.cs（Draw/DrawLane/DrawLine/DrawRing/DrawPath/DrawLaneFallback）。
```cpp
namespace render {
  void DrawField(Platform::IRenderer& r, const Ruleset& rs, const ChartData& chart,
                 const ChartContext& ctx_or_null, double nowMs, double vw, double vh);
  // 家族分派：Lane/Line/Ring/Path + Fallback（未识别仅底）
  // 图形语义：Lane=轨道+下落条+判定线；Line=判定线+音符点（法线偏移）；Ring=外环+扇区线+
  //   RingNote 方位圆点+滑星弧（DrawLine 折线）；Path=路径+拐角标记（ADOFAI 类）
  void DrawBackground(const PresetBackground&, IRenderer&, double vw, double vh);
}
```

### 2.3 ABI：ms_rhythm_*（并入 ms_bind.h——单头单 ABI，A-5 建议）

> 线程/所有权/错误码同 M2.5（单线程；句柄=引擎拥有；MS_ERR_* 复用；新增 MS_ERR_UNSUPPORTED_FORMAT? —— 用 NOT_SUPPORTED=8）。C# 侧仅为同一 DLL 的附加 P/Invoke 段。

```cpp
/* ---- Rhythm.* 域库（M4） ---- */
typedef struct ms_chart ms_chart;            // RhygeMaker::Rhythm::ChartData*
typedef struct ms_tracker ms_tracker;        // RhygeMaker::Rhythm::JudgementTracker*
typedef struct ms_ruleset ms_ruleset;        // RhygeMaker::Rhythm::Ruleset*
typedef struct ms_story ms_story;            // Storyboard*
// 图表
MS_API int  ms_rhythm_chart_load(ms_engine* e, const char* path, ms_chart** out);       // 0/错误码
MS_API int  ms_rhythm_chart_load_text(ms_engine* e, const char* json, const char* sourcePath, ms_chart** out);
MS_API int  ms_rhythm_chart_save(ms_engine* e, ms_chart* c, const char* path);
MS_API void ms_rhythm_chart_release(ms_engine* e, ms_chart* c);
MS_API int  ms_rhythm_chart_meta(ms_engine* e, ms_chart* c, char* outTitle, char* outArtist, char* outVersion, int cap, double* outOffsetMs);
MS_API int  ms_rhythm_chart_stats(ms_engine* e, ms_chart* c, int* outNotes, double* outDurationMs, double* outAvgDensity, double* outPeakDensity);
// 规则/预设
MS_API int  ms_rhythm_ruleset_create(ms_engine* e, ms_chart* c, const char* modeId, int keys, ms_ruleset** out);
MS_API int  ms_rhythm_ruleset_release(ms_engine* e, ms_ruleset* rs);
MS_API int  ms_rhythm_preset_list(ms_engine* e, char* outJson, int cap);                   // [{id,name,keys}]
MS_API int  ms_rhythm_preset_run(ms_engine* e, const char* id, int* outHit, int* outMiss, double* outAcc);
// 判定
MS_API int  ms_rhythm_tracker_create(ms_engine* e, ms_chart* c, const char* profileId, ms_tracker** out);
MS_API int  ms_rhythm_tracker_release(ms_engine* e, ms_tracker* t);
MS_API int  ms_rhythm_tracker_update(ms_engine* e, ms_tracker* t, double nowMs);
MS_API int  ms_rhythm_tracker_press_lane(ms_engine* e, ms_tracker* t, double lane, double nowMs, int* outTier);
MS_API int  ms_rhythm_tracker_press_field(ms_engine* e, ms_tracker* t, double x, double y, double hitRadius, double nowMs, int* outTier);
MS_API int  ms_rhythm_tracker_result(ms_engine* e, ms_tracker* t, int* outHit, int* outMiss, double* outAcc, double* outScore, int* outMaxCombo);
// 渲染（重制游戏宿主用——C# OnRender 录制面调度）
MS_API int  ms_rhythm_render_field(ms_engine* e, ms_chart* c, ms_ruleset* rs, double nowMs, double vw, double vh);
// Story（P1 先注册——M4.2）
MS_API int  ms_rhythm_story_load(ms_engine* e, ms_chart* c, ms_story** out);
MS_API int  ms_rhythm_story_frame(ms_engine* e, ms_story* s, double nowMs, double* outFade, double* outSlide, double* outWipe);
```

- C# 代理（RhygeMaker.Bind.Rhythm）：RhythmChart（ChartData 代理+Meta/Stats/Notes 枚举）/Ruleset/Tracker/Profile/PresetRunner/RenderBridge——API 与 ms_* 对称（DIF=0 契约表，同 M2.5 做法）。
- Python：_interop.py 增 ms_rhythm_*（同一 ABI——免费对称；M4.2 验证）。

### 2.4 CLI（Tools）

```bash
RhygeMakerRhythm.exe --preset <id>            # 无头 10 预设 acc=1.000；退出码=0/1
RhygeMakerRhythm.exe --preset-list            # 预设表（ToDocTable）
RhygeMakerRhythm.exe --preview <id> [--size WxH]   # 开窗四族预览（Esc 退出；渲染 hash 非空）
RhygeMakerRhythm.exe --mil-check <path>       # .mil 解析+校验（错误行号）
```
新工程 tools/ms_rhythm（独立 exe；CMake 目标 rhygemaker_rhythm_cli）——不并入 ms_demo（职责分离：ms_demo=M1 平台演示）。

### 2.5 cookbook A/B/C（3 个独立 exe=引擎示例链）

| # | 目标 | 内容 | 验收 |
|---|---|---|---|
| A | cookbook_a_4k | .mil 加载→LaneRuleset→RunHeadless acc→窗口绘制循环（下落+判定线） | 编译+运行；acc=1.000；窗口画面非空 |
| B | cookbook_b_phigros | Line 族+判定线（lineMeta/lineParents）+音符（自由场 x/y） | 编译+运行；headless acc=1.000；预览画面 |
| C | cookbook_c_arcaea | Ring 族+滑星（RingNote SectorAngle/Angle）+天键（y=1.0） | 编译+运行；acc=1.000；预览画面 |
代码位：tools/cookbook/（每 cookbook 独立 CMake 目标；==C# 参考 CookbookA_4K/B_Line/C_Arcaea 的 C++ 对应）。

### 2.6 域库验收 10 项（端口化 = M4 本体验收）

| # | 验收 | 断言方式 |
|---|---|---|
| R-1 | BpmTimeline 变速 | 4 组用例（往返/同刻替换/SnapToBeat 变速感知/银行家舍入对拍） |
| R-2 | Profile 16 工厂 | Evaluate 窗口值逐档断言（v1 常量固化）；WindowMultiplier ×0.25..4 钳制+×1.0 零差异硬断言 |
| R-3 | RhythmNote 长条状态机 | 8 用例（HitHead/HitTail/KeepHolding/BreakHold/ResetHold/HoldComplete/窗口端点） |
| R-4 | **四族 RunHeadless 10 预设** | 同谱同偏移 vs legacy 参考：判定总数/ACC/得分/评级/时间分布 1:1（acc=1.000 全命中） |
| R-5 | .mil round-trip | **前置修复：G9（AssetDatabase custom type round-trip）**——test_g9_red.exe 已实锤（save ok=1 / load NO；red 工具已备）→ 修复后：6 组样本（其他/Chart/Milestone示例 + 引擎 BuildSampleChart 生成物）+15+ 语料（§7.1）逐键一致（SerializeMil↔ParseMilText） |
| R-6 | JudgementTracker | 热换（SetProfile 事件）+Judged/JudgedDetail 事件序+二分 PressLane（t9 语义） |
| R-7 | Storyboard | ResetTo/HookFired/HookRate 断言（t6 EN-4 语义）+StoryboardPlayer Play/Pause/Seek |
| R-8 | PracticeSession | 段循环自动重开/幽灵/rate 0.5..1.5/分段不回写 |
| R-9 | **域零引用+ABI** | include 扫描（rhythm/* 不得 include core/*；core 不得 include rhythm/*；rhythm 第三方=0）+ms_rhythm_* 契约测试（句柄/错误码/生命周期） |
| R-10 | 示例闭环 | cookbook A/B/C 编译+运行；--preset acc=1.000；--preview 四族可视（截图非空+2 帧差异>0） |

---

## 3. B 部分：引擎版 Milestone 项目重制

### 3.1 目标与『对标』定义

- **原版=游戏源码/源码/（Milestone.exe——WinForms+D2D，非引擎化）**。重制=v3 引擎上的 example 项目（C# 开发层），功能逐项对标。
- parity 口径（沿用功能复刻对比清单.md §0）：
  1. **判定口径（机械化）**：同谱同偏移 → v3 vs legacy autoplay：判定总数/ACC/得分/评级/时间分布 1:1。
  2. **行为口径**：UI 文案（UiText 逐字）/数值/操作流 diff（引擎版页面对照原版页面）。
  3. **视觉口径**：布局/配色/符号形态=对标；**字体=引擎 RuntimeFont（VGA8x8+CJK 位图 GDI 光栅）vs 原版 D2D 矢量=声明近似（B-5）**；像素级逐帧 diff 非目标（2D parity 黄金帧=引擎 Demo 面专用——重制画面以『布局要素一致+截图取证』验收）。

### 3.2 项目架构（C# 开发层示例）

```
引擎源码/projects/milestone/
  Milestone.Game.csproj            (net8.0; 引用 RhygeMaker.Bind; P/Invoke ms_rnd_*/ms_text_* 仅 #if WINDOWS)
  Program.cs                       (GameEngine.Run 循环: Pump→OnFrame→OnRender→Present)
  App/PageStack.cs                 (页面栈: Push/Pop/Transition(Fade/Slide——域 Storyboard 复用)
  App/MainMenuPage.cs / SongSelectPage.cs / PlayPage.cs / ResultPage.cs / SettingsPage.cs /
      FolderPage.cs / CalibrationPage.cs)
  Play/PlayState.cs                (ms_rhythm_* 宿主: Chart+Ruleset+Tracker+Profile+ScoreBoard)
  Play/ChartDriver.cs              (AudioClock 桥→域 Clock/InputState 桥; Offset; Speed/scroll 事件)
  Play/Hud.cs                      (判定字/连击/ACC/进度/自动游玩 AI 游玩徽章——ms_rnd_* 绘制)
  Ui/Widgets.cs                    (Label/Button/Card/TabBar/Scroll/TextEdit——ms_text_*+ms_rnd_*)
  Ui/Theme.cs                      (原版配色令牌: 主蓝 #2C6CFF/深底/文字白——口径 §5.5/§5.6)
  Charting/ChartLoader.cs          (ms_rhythm_chart_load + 曲库扫描 ChartsFolder .mil)
  Data/AppConfig.cs                (设置持久化 JSON — ms_assets_save_text)
```
- **运行模型**：GameEngine.RunFrame + OnFrame（逻辑；Update 生命周期）+ **OnRender（绘制——M4-X 全委托）**；页面=场景外视图状态机（页面栈更贴近原版；不强制把每个页面做成 Core 场景——**重制=引擎示例：页面栈+域渲染+组件可选**。Scene 用于可编辑场景（编辑器已有）；游戏页=命令式绘制（Godot _Draw 式）——B-3 决策）。
- **输入**：InputState 桥=raw vk 直通（X4；KeyCode 枚举扩展非必需——raw vk 优先）+ 鼠标（3 键+滚轮 X4）+ 键位映射=ModeInfo.KeyHint→动作表（B-6）。

### 3.3 引擎缺口与前置（M4-X——重制 P0 的前置条件，实现随 M4.1）

| # | 缺口 | 现状 | 设计 |
|---|---|---|---|
| X1 | **OnRender 完全委托** | ms_engine_render ctx=IntPtr.Zero；C# OnRender 为 RendererStub（M2.5 P1） | ms_engine_render(engine, ctx) → 引擎进入**命令录制模式**（录制器=IRenderer 实现）；C# OnRender 经 ms_rnd_* 命令 → 帧末**双面回放**：parity 固定面 1280×800 原生（确定性——render hash 稳定）+ 窗口面 viewport 缩放+letterbox（沿用 M1 语义）。录制器=引擎拥有；C# 只调命令 |
| X2 | **ms_rnd_* 绘制命令** | 无（C# 只碰 RendererStub） | 10 原语 1:1（Clear/ClearRect/FillRect/FillRoundedRect/DrawLine/FillCircle/DrawCircle/FillTriangle/FillQuad/BlitRect 同 IRenderer 签名）——回放=同签名直调 |
| X3 | **RuntimeFont（文本）** | Editor 有 VGA8x8+CjkFont（编辑器专用） | M4-a-2：VGA 8×8 位图 + CJK GDI 光栅 提取为 Platform::Font（RuntimeFont）——Editor 复用（消重复）；ABI ms_text_draw/ms_text_measure（UTF-8；emoji 宽=码点×1.15 口径；有界缓存 512 语义=EngineText 先例） |
| X4 | **Input 扩展** | KeyCode 枚举 12 键+GetAxis/GetButton；鼠标仅位置+单键 | Input::GetKeyDownRaw(int vk)/GetKeyUpRaw(int vk)/GetKeyRaw(int vk)（vk 直通——序列化/映射灵活）；OnMouse 增 button(0/1/2)+wheel；窗口回调已具备（WM_RBUTTONDOWN 先例 t124）。**状态更新（t137 G2 已具：ms_input_* 五方法 key_down/key_up/get_key/axis/get_button——C#/Python 对称封装落地）——剩余：①输入是否 vk 直通（若是→X4 键面基本满足）②鼠标 3 键+滚轮 ABI 待补** |
> **X4 更新（t137 G2 回执证实）**：vk 直通=是（ms_input_get_key(e,vk)→Input::GetKey((KeyCode)vk)——Win32 VK 直通 t111 口径）→**X4 键面满足 ✅**；剩余=鼠标 3 键+滚轮 ABI=P1（已注记——M4-X X4 收尾项）。X1 双渲染面=parity 面+window 面初始化一致（G1 已修——双面政策确认 ✅）；G3 空场景回退+parity 哈希不变=符合。 |
| X5 | **.mil 资产化** | t115 钩子=类型表注册（DomainType(Chart) .mil 骨架） | AssetDatabase 类型表注册 type="mil"（Load=路径解析+TraversalDenied+内容 FNV；payload=原文——非容器化；域 ChartIO 负责解析语义。库浏览/guid 复用） |
| X6 | 音频变速 | WinMM TimeScale=1.0 | 域 RhythmClock 变速（练习 rate）；音频客观 1.0（文档化差异；变速音频=P1 WinMM 再评估） |

### 3.4 功能矩阵（parity matrix——原版 vs 引擎版）

> 原版文件引用=游戏源码/源码/...；判定口径=§0.1；阶段=M4.1（P0）→M4.2/P1→M4.3/P1→M4.4/P2（§3.5）。

| 功能 | 原版实现 | 引擎版方案 | parity 口径 | 阶段 |
|---|---|---|---|---|
| 主菜单 | MainForm+UiText（18 按钮 2 列+玩家卡+统计行+副标题+底部提示） | MainMenuPage（PageStack+Widgets；文案 UiText 逐字端口） | 文案+操作流 | P0 |
| 选歌页 | SongCardView（分类 chips/卡片/单击选中双击游玩/▶✏🎲随机/空库文案） | SongSelectPage（曲库扫描 X5 + 卡片网格） | 文案+操作流 | P0 |
| 游玩-模式 | ModeSystem 20（可用 10）+ GamePanel.Draw* 各模式 | 域 FieldRenderer 四族映射（Mania4/8=Lane；Phigros/Cytus=Line；Arcaea/maimai=Ring；ADOFAI=Path；osustd=Line(近似)；IIDX=Lane+转盘） | **判定口径 1:1**；视觉=四族形态 | P0 |
| 游玩-判定 | JudgementEngine+JudgeSettings（档位 16 档 window 表） | 域 JudgementProfile（R-2/R-6）+Settings 档位热换（SetProfile） | 判定 1:1+热换 | P0 |
| HUD | GamePanel.Hud（HiT 线/连击/ACC/进度/速度事件 scroll/zoom/Bezier） | Hud.cs（ms_rnd_*；事件=域 Storyboard/事件表） | 数值+布局要素 | P0 |
| 结算 | GamePanel.Result（成绩/ACC/评级/TP/CytusTP/重试/返回） | ResultPage（ScoreBoard.Rank/Tp 域直取） | 数值 1:1 | P0 |
| 自动游玩/AI 游玩 | StartAutoplay/AiEngine（规则三档） | 域 RunAuto（自动）+规则 AI（重制项目内实现——规则档 t5 语义） | 判定 1:1 | P0 |
| 设置 9 分区 | SettingsPanel（游戏/判定/分数/界面/评级/音效/皮肤/存档/键位） | SettingsPage（P0 子集=判定/分数/界面/键位/音效；皮肤/评级=P1） | 文案+数值+操作流 | P0（子集）/P1 |
| 曲库管理 | FolderPanel（选择/导入/扫描解压/刷新/打开/删除/复制路径） | FolderPage（.mil 扫描+osu 导入=P1） | 操作流 | P0 |
| 校准 | CalibrationForm+Dsp | CalibrationPage（BeatAlignEngine 端口=P1——Dsp 域库 P1 A-7） | 操作流 | P1 |
| 编辑器 | ChartEditorPanel（画布/工具栏 6 组/音符笔刷/自动偏移/AI 检查/制谱助手/多场） | **复用 v3 六区编辑器 + .mil 音符编辑扩展**（Editor 面板扩展——M3 编辑器与 t131 AI 助手） | edshot 截图对比 | P1 |
| 练习 | PracticeSession（段循环/幽灵/rate） | 域 practice（R-8） | 段界行为 | P1 |
| 回放 | ReplaySystem | 域事件录制回放（P1） | 时间分布 | P1 |
| 皮肤/布局编辑器 | SkinSettings/LayoutCustom | 域 PresetLayout/PresetBackground+页面布局文件（P1） | 布局要素 | P1 |
| 玩家信息/我的数据 | PlayerData（成绩库） | Data 层 JSON（ms_assets）+统计页（P1） | 数值 | P1 |
| 段位挑战 | DanSelectDialog（分组/曲目/HP） | CourseData（.mc 段位档）→域判定串联（P1） | 判定序 | P1 |
| 回环作曲 | LoopComposer（9 模式演绎） | 域 LoopComposer（展平导出 .mil——P1；引擎零改动） | 导出一致 | P1 |
| 迷你 mania | MiniManiaGame | cookbook A 变体（P2） | — | P2 |
| 联机 | MpManager/Leaderboard | **范围外声明**（原版网络=托管套接字；v3 域名网络=PlatformSockets 参考——重制不含联机；M4.4 评估） | — | P2/范围外 |
| 主题/转场 | AppConfig 主题+UiTransition/SceneCompositor | 域 Storyboard（Fade/Slide/Wipe 已有）+主题令牌（P2） | — | P2 |
| 多场同屏 | stages+parts（t27） | 域 StageSpec+多 Ruleset 实例（P1） | 判定分场 | P1 |
| 事件系统 | events（zoom/noteSpeed/scroll/Bezier Keyframe） | 域事件表+EvalEvents 端口（P1） | 数值 | P1 |
| 音频 | AudioPlayer（WPF MediaPlayer 参考） | WinMM AudioBackend+AudioClock（P0） | 时间对齐 | P0 |
| 启动对齐分辨率 | StartupClientSize（P0-4 修复） | ViewportPolicy（M1 已有） | 行为 | P0 |

### 3.5 分阶段（建议 captain 拍板——B-2）

- **M4.1（=M4 本体+重制 P0 循环）**：域库全量（§2）+ms_rhythm_*+M4-X 桥（X1-X5）+cookbook A/B/C+CLI + 重制 P0=主菜单→选歌→游玩（四族映射 10 模式）→判定→HUD→结算→返回 + 自动/AI 游玩 + 设置子集 + 曲库管理 + 校准简单版。
- **M4.2（P1）**：编辑器 .mil 扩展 + t131 AI 助手 + 练习/回放 + 皮肤/布局编辑器 + 玩家信息/我的数据 + 事件系统 + 多场。
- **M4.3（P1）**：回环作曲 + 段位 + 全模式视觉细化（各模式 Draw* 对拍）+Storyboard 演出 + 校准（Dsp）。
- **M4.4（P2）**：主题/转场/迷你 mania/联机评估/多语言（编辑器汉化同款）。
- 每阶段验收=对应原版功能清单 1:1 行为对比（§3.4 行）+试玩人类走查+回归全绿。

### 3.6 重制验收 10 项（P0 阶段）

| # | 验收 | 方法 |
|---|---|---|
| G-1 | 判定 parity | 10 可用模式同谱同偏移：legacy autoplay vs v3 域 headless——判定总数/ACC/得分/评级/时间分布 1:1（自动化：ParityCli 对齐参考——重制 P0 内实现） |
| G-2 | 主菜单/选歌 parity | 文案逐字（UiText）+操作流（点击→页面）diff |
| G-3 | 游玩 HUD | 判定字/连击/ACC/进度/速度和 scroll 事件=数值对拍；布局要素截图取证 |
| G-4 | 结算 | Score/Rank/Tp/TP 与 legacy 同谱结算一致（含 CytusTP 公式） |
| G-5 | 设置子集 | 档位（判定档热换）/键位（KeyHint 映射）/界面（分辨率/刷新率 FpsGovernor 复用）行为一致 |
| G-6 | 曲库 | .mil 扫描（200 谱性能基线 34ms 参考）+空库文案+导入（P0=扫描；导入 P1） |
| G-7 | 自动/AI 游玩 | RunAuto acc=1.000；规则 AI 三档可玩（t5 语义） |
| G-8 | 多分辨率/多刷新率 | ViewportPolicy letterbox+刷新率档（1.0/1.35/1.868/0.78 scale 居中无裁切——t7 复验法） |
| G-9 | 渲染确定性 | OnRender 命令录制 → parity 面 render hash 稳定（帧内确定性）；窗口面=缩放 letterbox；性能目标=P0 1000+ FPS（无头）/窗口 60+ |
| G-10 | 回归全绿 | 引擎全部 ms_test（core/platform/bind/editor/render3d/rhythm/新）+C# CsTestRunner+Python unittest（A-5 对称） |

### 3.7 决策点 B-1..B-8（供 captain 拍板）

| # | 决策 | 建议 |
|---|---|---|
| B-1 | 重制语言 | C# 开发层（主面——双层架构定案）；Python 对称承诺 P-7=M4.2 验证 ms_rhythm_*（免费同 ABI） |
| B-2 | **M4 交付范围** | **M4=域库+CLI+cookbook+重制 P0 循环**（M4.1）；M4.2-4.4（P1/P2）另立里程碑——**推荐**（否则 6-12 月长跑） |
| B-3 | 重制渲染模式 | OnRender 全委托（Godot _Draw 式）命令录制双面回放——**推荐**（域 FieldRenderer C+++C# HUD/UI 混合；不强制 Scene 化游戏页） |
| B-4 | 重制 UI | 自绘 Widgets（ms_rnd_*+ms_text_*+主题令牌）——零第三方红线保持；ImGui 白名单仍=Editor 专属候选（决策点 V3-5 不变） |
| B-5 | 字体 | RuntimeFont=VGA8x8（ASCII+CP437 公有域）+CJK GDI 光栅（同 t125 编辑器）——视觉=位图近似声明；emoji 宽 1.15 口径 |
| B-6 | 输入 | raw vk 直通优先（X4）+ ModeInfo.KeyHint→动作表；KeyCode 枚举=扩展候选（不依赖） |
| B-7 | 音频 | WinMM mci+AudioClock（客观时间）；域 RhythmClock 变速（练习 rate）——变速音频=P1（X6） |
| B-8 | 联机 | 重制**范围外**（P2 评估：PlatformSockets 参考/零三方最小子集——另立决策） |

### 3.8 红线与诚实声明

1. **零第三方**：域库/Util/渲染/字体/CLI/代理=全自研；C# 侧纯 BCL+平台 P/Invoke（#if WINDOWS——HardwareProbe 先例）。
2. **视觉近似**：位图字体（VGA8x8+CJK）vs 原版 D2D 矢量布局——布局/配色/符号对标，像素 diff 非目标；素材（背景图/音效）=复用仓库（版权/尺寸策略同原版）。
3. **多格式导入**：.mil 全格式=M4；.osu/.adofai/.aff/.qua/.mc/.sm/.ssc/json =P1（ChartParser 家族映射表）。
4. **成本估算**（诚实）：域库 3-4 周（eng-coder-vis）+M4-X 桥 1 周+重制 P0 4-6 周；M4 全程≈8-12 周（单引擎编写）；与 v3 规格 §8 风险声明一致→建议 captain 按 B-2 切里程碑。
5. **命名**：RhygeMaker::Rhythm / ms_rhythm_*（稳定 ABI 前缀保留——历史文档 Milestone::Domain 为 v3 前名，不改 ABI）。

---

## 4. 端口映射表（v1 参考 → v3 域模块）

| v1 参考（引擎源码/engine/） | v3 模块（rhythm/*.hpp） | 状态 |
|---|---|---|
| ChartModel.cs（BpmTimeline/StageSpec/ChartData/DifficultyStats） | beat.hpp + model.hpp | 设计（本文件） |
| RhythmCore.cs（BeatMath/JudgeTier/JudgementProfile/Judgement/TimeDepthMapper/LaneField/RhythmNote 族） | beat/judgement/field/model.hpp | 设计 |
| Ruleset.cs + RulesetFactory.cs（四族/Runner/Descriptor/ChartSeed） | ruleset.hpp | 设计 |
| JudgementTracker.cs + JudgementLog.cs + ScoreBoard.cs | judgement.hpp | 设计 |
| Storyboard.cs | story.hpp | 设计 |
| PracticeSession.cs | practice.hpp | 设计 |
| ChartValidator.cs | validator.hpp | 设计 |
| StarterChartGenerator.cs + Dsp.cs + BeatAlignEngine.cs | starter.hpp + dsp.hpp（**P1**） | P1 |
| EnginePresets.cs（10 预设） | preset.hpp + mode.hpp | 设计（验收核心） |
| Tool/RulesetRenderer.cs（四族渲染） | render.hpp | 设计 |
| Tool/ChartImport.cs + 游戏 ChartParserExtra.ParseMil/SerializeMil | chartio.hpp | 设计（全格式） |
| 游戏 CoreUtil/ModeSystem.cs（20 玩法） | mode.hpp | 设计 |

---

## 5. 实现顺序建议（供 eng-coder-vis 排期）

1. M4-a：Util 提取（msjson+math→RhygeMaker::Util；Core 转发；双源 MD5 校验）——**前置**
2. beat/model/field/judgement（含四族字段+Profile 工厂）——域地基
3. preset.hpp + 10 预设端口断言（**R-4 首达**——与 legacy RunHeadless 对拍）
4. ruleset（数据平面化 RunAuto）+render（四族 DOM）
5. chartio（.mil 全格式）+mode（模式注册表）
6. story/practice/validator（域附属）+tests 全量（R-1..R-10）
7. ABI（ms_rhythm_*）+C# 代理+C# 断言（对称表）
8. CLI（--preset/--preview/--mil-check）+cookbook A/B/C
9. M4-X：X1 录制回放桥/X2 ms_rnd_*/X3 RuntimeFont/X4 Input 扩展/X5 资产化
10. 重制 P0（页面栈+游玩循环+设置子集+曲库）→ G-1..G-10 验收

---

*终稿*（t132 设计交付：A=Rhythm.* 域库 12 模块签名级（v1 端口映射+R-1..R-10 验收）+ABI 20 函数+CLI+cookbook 3；B=引擎版 Milestone 重制（架构/parity 矩阵 22 行/分阶段 M4.1-4.4/缺口 M4-X 5 项/验收 G-1..G-10）+决策 A-1..A-8/B-1..B-8；未写代码；等待 captain 拍板（A-1/A-3/A-5/B-2/B-3 关键）后派 eng-coder-vis）。

---

## 6. 增补（t132 二轮——captain 二次指导：①域 C++ 核确认 ②游戏层改场景组件化 ③功能矩阵=评估基准盘）

> 对应 captain 指令：『设计①Rhythm.* 域（C++ 核：.mil 谱面资产/时间轴/Ruleset 四族/判定四档/音频同步/导入器引用）②引擎版 Milestone 游戏层（建议 C# 开发层+场景组件化：LaneRenderer/NoteSpawner/JudgementZone/MusicDriver）③功能矩阵（原版非引擎化 Milestone.exe 功能清单逐一映射引擎版状态：已具/待做/跳过+理由）——评估“不像原版不停”的基准盘』。
> 冻结基准=输出产物/bin/net8.0-windows/win-x64/Milestone.exe（官方构建黑盒）；行为证据=游戏源码/源码/* 文件:行号索引（exe 构造=对应源码版本）。

### 6.1 域 ① 补强：判定档精确定义 + 音频同步

- **判定四档（档位=判级结构）**：```cpp
struct JudgeTier { std::string Name; double WindowMs; double Score; double Weight; bool BreaksCombo; };
struct Judgement { const JudgeTier* Tier;   // null=MISS
  double OffsetMs; double NoteTimeMs;
  bool IsHit() const; std::string TierName() const;  // Tier?Name:"MISS"
  int Score() const; bool BreaksCombo() const; };
```
  档位=各 Profile 自定义（Arcaea 3 档 / Phigros 3 档 / IIDX 4 档 / Cytus 3 档 / ADOFAI 按 BPM 角度换算（30°/45°/60°→20/30/65ms 上限）——**端口注意：Adofai(bpm) 角度→ms 公式+cap 语义**（RhythmCore.cs L121-136）；Settings 判定档位热换=WindowMultiplier ×0.25..4（EN-2）。**v1 大档位表（JudgeSettings 16 档）**=M4.1 由 Profile 工厂+Multiplier 两轴覆盖（档位选择=不同 Profile 实例化参数；对拍常量固化）。
- **音频同步（域零依赖关键）**：ChartContext 不含 Platform::AudioClock（域零 Platform 具体类引用）；以 **时钟源回调注入**（A-6' 修订）：
```cpp
class ChartContext {
  ...
  std::function<double()> ExternalTimeMs;   // 宿主注入：audio.SongTimeMs()+offset（Pause 时宿主冻结）
  double NowMs() const;                      // 域时钟（虚拟/真实二选一：SetTime 直设 或 ExternalTimeMs 缓动）
  double Rate;                               // 练习变速（域时钟推进步长×Rate；音频=客观 1.0——X6 声明）
};
```
  MusicDriver（组件）每帧 `ExternalTimeMs = [&]{ return audio.SongTimeMs() + chart.AudioOffsetMs; }`；Pause/Resume=宿主冻结/恢复该回调推进（audio Pause/Play 同步）——单时钟源（域时钟），无双时钟漂移。

### 6.2 游戏层 ② 重定调：场景组件化（captain 建议采纳——B-3 修订为 B-3'）

架构（替代 §3.2 纯页面栈的命令式渲染——**页面栈保留给菜单/设置/结果**；**游玩区=Core::Scene 场景组件化**）：

```
projects/milestone/Milestone.Game
  Pages（UI/导航层）: MainMenuPage / SongSelectPage / SettingsPage / FolderPage / ResultPage
      —— 页面栈（复 ms_text_*+ms_rnd_* 自绘 Widgets；主题令牌 §3.4）
  PlayScene（Core::Scene——游玩区=场景组件化）
    Root
     ├─ MusicDriver : ComponentBase   Awake→AudioBackend.Open+AudioClock.Bind；Update→externalTime 桥
     │                                +EndTimeMs 结束检测；Pause()/Resume()（audio 同步+时钟冻结）
     ├─ ChartRuntime: ComponentBase   域 ChartData+Ruleset+Tracker+ScoreBoard 唯一宿主
     │                                （SpawnWindow/ReceiveInput/Advance/Reset；事件→HUD）
     ├─ PlayfieldRoot: GameObject
     │   ├─ LaneRenderer  : ComponentBase, IRenderable   RenderOrder=1（域 FieldRenderer 分派）
     │   ├─ NoteSpawner   : ComponentBase, IRenderable   RenderOrder=2（NotesInRange 窗口→池；
     │   │                                                    OnRender=音符绘制（域 note 坐标→场变换））
     │   ├─ JudgementZone : ComponentBase, IRenderable   RenderOrder=3（命中区/判定线；
     │   │                                                    Input→Tracker.PressLane/PressField/HoldLane）
     │   └─ (音符/判定线/背景 GameObjects——运行时由 Spawner/Zone 管理)
     └─ HudLayer    : ComponentBase, IRenderable   RenderOrder=9（判定字/连击/ACC/进度/徽章）
```

- **渲染通行证（M4-X X1 修订）**：引擎 Render() → ① bind 场景遍历：场景内实现 `IRenderable { int RenderOrder; void OnRender(IRenderer r); }` 的组件按 RenderOrder 升序被调用（Unity 序后、每帧）→ 全部绘制进**命令录制器**（IRenderer 实现）→ ② 帧末双面回放：parity 固定面 1280×800 原生（确定性+render hash）+ 窗口面 viewport 缩放+letterbox。
- **DevLayer 签名（RhygeMaker.Bind）**：
```csharp
public interface IRenderable { int RenderOrder { get; } void OnRender(IRenderer r); }
public class LaneRenderer  : ComponentBase, IRenderable { public string ModeId = "mania"; public int Keys = 4; }
public class NoteSpawner   : ComponentBase, IRenderable { public RhythmChart Chart; public float WindowSec = 1.2f; }
public class JudgementZone : ComponentBase, IRenderable { public float HitRadius = 32; public float HitLineY = 0.74f; }
public class MusicDriver   : ComponentBase             { public string AudioPath; public double OffsetMs; }
```
- **ABI 修订（B-9）**：ms_component_spec 增补可空 `ms_cb_void onRender` 字段 + `ms_component_register(e, spec, specSize)` 尺寸协商（旧调用方 specSize=旧长度→onRender=NULL=兼容）；ms_engine_render(e, ctx) 的 ctx=录制器指针（C# 侧 IRenderer 适配）。
- 与编辑器通路：PlayController 快照（M3 已具）可复用——重制游玩场景经 PlayController 可进编辑器演示（P1 预览复用）。

### 6.3 功能矩阵 v2（captain 评估基准盘——逐项 已具/待做/跳过+理由）

状态标记：✅已具（引擎/域库/重制已具备可复用件）/ 🔧待做（重制实现任务：P0/P1/P2 标注）/ ⏭跳过（范围外+理由）。
冻结基准：输出产物/bin/net8.0-windows/win-x64/Milestone.exe（黑盒对照）；证据=游戏源码/源码/* 文件:行号。

| # | 功能 | 原版证据（文件:行号） | 引擎版方案/落点 | 状态 | 阶段/理由 |
|---|---|---|---|---|---|
| M1 | 曲库：扫描 ChartsFolder（.mil 等 9 扩展+zip） | ChartParser.cs L3-4（ChartExts/ChartZipExts）+ZipImport.cs | 域 chartio .mil 全格式+AssetDatabase type=mil（X5）；扫描=FolderPage | 🔧 扫描 P0；.osu/.adofai/.aff 等导入=🔧 P1（导入器引用=ChartParser 家族映射——§2.2 chartio A-3） | P0/P1 |
| M2 | 选歌：分类 chips/卡片（曲名·作者·难度·徽章）/单击选中·双击游玩/▶游玩·✏编辑·🎲随机/空库文案 | 功能复刻对比清单.md §2+UiText.SongEmptyLibrary | SongSelectPage（UiText 逐字端口+卡片网格） | 🔧 | P0 |
| M3 | 游玩：10 可用模式（Mania4-8/Phigros/Arcaea/Cytus/osu std/ADOFAI×2/IIDX/maimai） | ModeSystem.cs L34-58（20 条全表；Removed=不可用） | 域四族映射+FieldRenderer+ModeRegistry（§2.2 mode/render） | 🔧（域已设计；游玩渲染=LaneRenderer 组件） | P0 |
| M4 | 游玩：判定（档位=判级结构+Multiplier 热换） | GamePanel.Play.cs L214-246（MultiKeyDown 双向下界选择）+JudgeSettings.cs | 域 JudgementProfile+Tracker（§6.1 档位定义；热换=SetProfile t6 契约） | 🔧（域设计；对拍常量=R-2/R-4） | P0 |
| M5 | 游玩：HUD（判定字/连击/ACC/进度/速度事件 scroll·zoom·Bezier 键帧） | GamePanel.Hud.cs L62-300（EvalEvents/BezierEval/BuildSpeedKeyframes） | HudLayer 组件（ms_rnd_*；事件表=EvalEvents 端口） | 🔧（事件=🔧 P1——P0 先直线速度） | P0/P1 |
| M6 | 游玩：跳过开头空白 | GamePanel.Editor.cs L324-347（SkipIntro；_canSkip=_firstNoteTime≥3000）+UiText.cs L56（S=跳过空白） | PlayState.SkipIntro（同门限 3000ms）+S 键 | 🔧 | P0 |
| M7 | 游玩：暂停/继续（含音频） | GamePanel.cs L1203-1212（TogglePause=音频 Pause/Play） | MusicDriver.Pause()/Resume()（externalTime 冻结+audio 暂停）——Space/P | 🔧 | P0 |
| M8 | 游玩：重开 | GamePanel.cs L1215-1227（Restart=ResetState+audio.Restart） | PlayState.Reset（Tracker/Board/Clock+audio Seek(0)+Play）——R | 🔧 | P0 |
| M9 | 结果：结算（评分/ACC/评级/TP/CytusTP/重试/返回） | GamePanel.Result.cs L16-54（SumHits/CytusTp）+ScoreBoard.cs L116-146 | ResultPage（域 ScoreBoard.Rank/Tp 直取；CytusTP 公式端口）——R/Enter=重试 Back=返回 Esc=菜单 | 🔧（域已设计） | P0 |
| M10 | 快捷键（游玩） | GamePanel.Editor.cs L1733-1856（Space/P=暂停 R=重开 A=自动 S=跳过 T=斜轨 V=3D M=换部 Esc=退出；结果页 R/Enter/Back/Esc） | InputState 动作表（raw vk 直通 X4）→PlayView 命令对象 | 🔧（T 斜轨/V 3D=M2.6（原版 3D 视觉=P2）） | P0（T/V=P2） |
| M11 | 设置：9 分区（游戏/判定/分数/界面/评级/音效/皮肤/存档/键位） | SettingsPanel.cs（9 分区） | SettingsPage（P0 子集=判定档/分数口径/界面（分辨率+刷新率）/键位/音量；皮肤=域 Preset/评级=P1） | 🔧 | P0 子集/P1 全量 |
| M12 | 曲库管理页（选择/导入/扫描解压/刷新/删除/打开目录/复制路径） | FolderPanel.cs | FolderPage（.mil 扫描 P0；osu 导入 P1） | 🔧 | P0/P1 |
| M13 | 全局快捷键（页面导航/编辑器内） | MainForm/各页（KeyDown 处理） | 页面栈输入路由（raw vk 动作表） | 🔧 | P0 |
| M14 | 编辑器：画布/工具栏 6 组/音符笔刷/自动偏移 | ChartEditorPanel.cs | 复用 v3 六区编辑器+Editor 面板 .mil 音符编辑扩展 | 🔧 | P1 |
| M15 | 编辑器：自动游玩（预览/F5 同窗承载） | GamePanel.StartAutoplay（t5 在引擎壳实现） | 域 RunAuto+PlayController 快照复用（M3 已具）+编辑器预览视图 | 🔧 | P1 |
| M16 | 编辑器：AI 辅助（AI 检查/制谱助手——t12 后=规则可用） | AiChartReview.cs（t12 重写=规则）+ChartAiAssistant.cs | **t131 AI 助手**（A 规则 12 条+B 本地服务档；已设计 t130 待实现） | 🔧 | P1（t131 已建） |
| M17 | 编辑器：多场/事件/线 meta/事件曲线 | ChartParserExtra.cs L1183-1210（lineParents/lineMeta）+GamePanel.Play L29-76（多场）+Hud L62-300（事件） | 域 StageSpec+lineMeta/lineParents 端口+多 Ruleset 实例（M4.3 多场） | 🔧 | P1/P2 |
| M18 | 练习模式（段循环/幽灵/rate） | PracticeSession.cs | 域 practice（R-8） | 🔧 | P1 |
| M19 | 回放 | ReplaySystem.cs | 域事件录制回放 | 🔧 | P1 |
| M20 | 段位挑战 | DanSelectDialog（分组/曲目/HP） | CourseData（.mc）→域判定串联 | 🔧 | P1 |
| M21 | 回环作曲 | LoopComposer.cs（9 模式） | 域 LoopComposer（展平导出 .mil；引擎零改动） | 🔧 | P1 |
| M22 | 皮肤/布局编辑器 | SkinSettings.cs/LayoutCustom.cs | 域 PresetLayout/PresetBackground+布局文件（游走拖放） | 🔧 | P1 |
| M23 | 玩家信息/我的数据 | PlayerData.cs（成绩库） | Data JSON（ms_assets_save_text）+统计页 | 🔧 | P1 |
| M24 | 校准 | CalibrationForm.cs+Dsp.cs | CalibrationPage（BeatAlignEngine+Dsp=P1 域库 starter 依 A-7） | 🔧 | P1 |
| M25 | 主题/转场 | AppConfig+UiTransition/SceneCompositor.cs | 域 Storyboard（Fade/Slide/Wipe 已具语义 t6）+主题令牌 | 🔧 | P2 |
| M26 | 迷你 mania | MiniManiaGame.cs | cookbook A 变体 | 🔧 | P2 |
| M27 | AI 训练 | AiTrainer.cs/AiDevice.cs | ⏭ **跳过**（训练=外部数据脚本工具链；引擎零增益；重制目标=游玩体验） | ⏭ | 理由：工具链域外 |
| M28 | 联机（大厅/排行榜） | MpManager.cs/MpLeaderboard.cs | ⏭ **跳过**（网络=PlatformSockets 参考+零三方最小子集=另立决策 B-8） | ⏭ | 理由：范围外（B-8 拍板） |
| M29 | 多分辨率/多刷新率 | ViewportPolicy.cs（P0-4）+FpsGovernor.cs（17 档） | ✅ Platform viewport（M1 已具）；刷新率=🔧（v3 无 FpsGovernor——重制循环 accumulator 驱动（t114 语义）+屏幕探测——🔧 P0） | ✅/🔧 | P0 |
| M30 | 音频（偏移/变速/点击音效） | AudioPlayer.cs+SoundFx.cs | ✅ WinMM AudioBackend+AudioClock（M1 已具）；偏移=ChartData.AudioOffsetMs（已具设计）；音效=🔧 P1 | ✅/🔧 | P0/P1 |

### 6.4 修订决策（B-3' / 新增 B-9..B-11）

| # | 决策 | 内容 |
|---|---|---|
| B-3' | **重制游戏区渲染=场景组件化**（修订 B-3） | 游玩=Core::Scene 场景组件化（LaneRenderer/NoteSpawner/JudgementZone/MusicDriver+IRenderable 通行证）；菜单/设置/结果=页面栈（轻视图）；渲染=场景遍历（RenderOrder 升序）→命令录制→双面回放 |
| B-9 | ABI v1.1 | ms_component_spec 增可空 onRender + ms_component_register(e,spec,specSize) 尺寸协商（旧调用方兼容） |
| B-10 | IRenderable | DevLayer（Bind）定义；RenderOrder 常量=背景1<音符2<判定线3<HUD9 |
| B-11 | 冻结基准 | 输出产物/bin/net8.0-windows/win-x64/Milestone.exe（黑盒对照；行为证据=源码行号索引——exe SHA/时间戳标注于重制报告） |

### 6.5 验收增补（对应二轮指导）

- G-11：组件化走查——PlayScene 生命周期序（Awake→Enable→Start→Update→OnRender(RenderOrder)）断言；Space Pause→externalTime 冻结+audio 暂停；S Skip 门限=第一音符≥3000ms。
- G-12：快捷键矩阵全键开通（M10 逐键走查）+判定四档（Profile 工厂）在设置页热换→SetProfile 事件→Tracker 窗口即时生效（对拍 t6 契约）。
- G-13：功能矩阵 M1-M30 逐行验收（试玩人类走查+自动化对拍；跳过项=≪【M27/M28】理由登记）。

---

*v2 终稿*（t132 二轮增补：①域=判定档精确定义+音频同步回调注入（域零平台依赖）；②游戏层=场景组件化（四组件签名+IRenderable 通行证+ABI v1.1）；③功能矩阵 30 行（M1..M30，逐项 已具/待做/跳过+理由+原版证据行号）+决策 B-3'/B-9..B-11+验收 G-11..G-13；等待 captain 拍板（B-3'/B-9 关键）后并入 t133 实现设计基线）。

---

## 7. 蓝本加速确认（t132 三轮——captain：.mil 格式实测+建议容器化+P0 聚焦）

> 指令：『.mil=msjson 基础资产（MSASSET 容器+FNV 校验托管；Rhythm::Chart 解析）；判定四档/时间轴参照 v1 Ruleset 语义；AudioClock 作判定同步锚；功能矩阵聚焦=曲库扫描（AssetDatabase List 已有）→选歌→游玩（NoteSpawner/JudgementZone/LaneRenderer）→结果→编辑器谱面（六区编辑器+AI 助手 t131）』。

### 7.1 .mil 实测字段核对（输出产物/bin/Chart/Milestone示例/——与 captain 描述一致 ✅）

| 样本 | 字段 | 实测（文件:行号） |
|---|---|---|
| Mania 4K 示例.mil | format/mode=mania/title/artist/version/bpm=120/offset/audio/keys=4/notes[{t,c},{t,c,e,type:hold}] | L2-33 |
| Phigros 示例.mil | 同上 + events[{t,e,type:moveY|rotate,v,ev(起→止)}];mode=phigros/keys=4 | L2-37 |
| Arcaea 示例.mil | 同上 + notes arc：{t,c,e,type:arc,ec(终点通道)};mode=arcaea/keys=6 | L2-31 |

- 结论：**M4 域 .mil 最小契约（P0 基线）**=format/mode/title/artist/version/bpm/offset/audio/keys/notes{t,c,e,type,ec}+events{t,e,type,v,ev}；**全量契约**=§2.2 chartio 键清单（+x/y/ex/ey/f/stages/parts/lineParents/lineMeta/danName/danSet/od/dr/ar/tickRate——其余样本与生成器覆盖；端口实现以 ChartParserExtra 源码为准，样本=回归夹具）。
- **回归夹具**：M4 直接以输出产物/bin/Chart/Milestone示例（6 样本）+ 测试格式（adofai_h/phigros_dual/arcaea_spec/arcaea_long/lineparents_test/multi_stage_test/iidx_test/loopcompose_test 等）+ 段位文件 = 域 chartio 测试语料库（R-5 断言样本扩充到≥15）。

### 7.2 容器托管决策（A-3'——采纳 captain 建议）

- **双态**：磁盘=原始 JSON（原版兼容/谱面库零迁移）；引擎内=MSASSET 容器（type=mil + guid + payload + FNV 尾部——SceneSerializer 容器机制复用）。
- 路径：ms_assets_load(path) → 容器化校验（内容 FNV vs 尾部——损坏检测）→ type=mil → ms_rhythm_chart_load(assetHandle) 解析 payload → Rhythm::Chart。
- Save 回写=原始 JSON（SerializeMil——round-trip 与 ChartParserExtra.SerializeMil 逐键一致）。
- **AssetDatabase List 已有**（captain 确认点）——曲库扫描 P0=ms_assets_*（root+list+load）→ 域 Chart 解析——无需新扫描器（X5 收敛）。

### 7.3 P0 聚焦确认（与 §6.3 矩阵对齐）

captain 聚焦链=曲库扫描→选歌→游玩（NoteSpawner/JudgementZone/LaneRenderer）→结果→编辑器谱面（六区编辑器+t131 自动游玩/检查）——**即 M4.1（B-2 推荐范围）**：M1（曲库）→M2（选歌）→M3/M4/M5/M7/M8（游玩/判定/暂停/重开）→M9（结果）→M14/M15/M16（编辑器+t131——**P1 边界内**；t131 已建 dep=t130）。全域库 C++ 侧交付同 §5 顺序；M4-X 桥 X1/X2/X3/X4/X5 为 P0 前置（X5 已因 7.2 收敛为「容器化+List 复用」）。
判定四档/时间轴=参照 v1 Ruleset 语义（§2.2 ruleset/beat——已设计）；AudioClock=判定同步锚（§6.1 ExternalTimeMs 注入——已设计）。

### 7.4 结论

设计基线冻结：**§2（域）+§6.2（组件化）+§6.3（矩阵）+§7（蓝本确认）=t133 实现任务基线**——建议 captain 拍板 B-3'/B-9 后直接创建 t133（dep=t132）。

---

*v3 终稿*（t132 三轮：.mil 实测核对（3 样本字段=蓝本描述 ✅）+容器托管 A-3' 采纳（双态：磁盘 JSON/引擎 MSASSET+FNV；AssetDatabase List 复用——X5 收敛）+P0 聚焦链确认（M1→M2→M3-9→M14-16）+回归语料库扩充（15+ 样本）；设计基线=§2+§6.2+§6.3+§7。等待 captain 拍板后建 t133）。

---

## 8. 对标映射（引 captain 原始清单 original-feature-matrix.md——13 域×引擎版×分级×可复用能力）

> captain 指令：『你的 t132 功能矩阵直接引用此清单作「原版×引擎版」对标（建议按 P0（核心游玩链：曲库→选歌→游玩→判定→结果）→P1（编辑器+AI）→P2（联机/回放/工具）分级映射，标注可复用引擎能力）』。**上位清单=original-feature-matrix.md（13 域·v1 冻结基准）**；本表=引擎版落点+分级+能力复用标注；细目（每功能点证据行号/状态 已具·待做·跳过）见 §6.3 M1-M30。

| 域（原版清单） | 原版功能点（original-feature-matrix.md） | 引擎版落点 | 分级 | 可复用引擎能力（标注） |
|---|---|---|---|---|
| 导航 | 引擎壳主菜单/二级页（曲库管理/我的数据/段位/键位/设置） | PageStack（MainMenu/SongSelect/Settings/Folder/Data/Dan） | P0（主菜单/选歌/设置入口）/P1（我的数据/段位页） | ✅ PageStack+Widgets（ms_rnd_*+ms_text_*）；菜单文案=UiText 逐字端口 |
| 曲库 | 多目录扫描/多模式/导入/谱面模型 | AssetDatabase List+容器化 .mil（A-3'）+域 ChartIO | P0（.mil 多模式）/P1（.osu/.adofai/.mc/zip 导入） | ✅ **AssetDatabase=曲库**（root/List/Load——X5 收敛）；✅ **.mil=msjson（Rhythm::Chart 解析——MSASSET 容器+FNV 校验）**；域 ModeRegistry=多模式 |
| 游玩 | 判定线/音符滚动/HUD/判定区渲染/暂停/跳过/布局自定义 | PlayScene 组件化（§6.2）+FieldRenderer | P0（判定线/滚动/HUD/判定区/暂停/跳过）/P1（布局自定义） | ✅ IRenderable 通行证+IRenderer 10 原语；✅ 域 FieldRenderer（Lane/Line/Ring/Path×四族）；✅ AudioClock=判定同步锚（§6.1）；布局自定义=域 PresetLayout（P1） |
| 判定 | 四档判定（PERFECT·GOOD·BAD·MISS 族）/判定配置 | 域 JudgementProfile+Tracker+WindowMultiplier 热换 | P0 | ✅ **判定四档**=JudgeTier/Profile 工厂（§6.1 精确定义）；档位热换=SetProfile（t6 契约）；判定配置=Settings 判定档×Multiplier |
| 结果 | 结算（准度/连击/评级/成绩记录） | ResultPage（域 ScoreBoard 直取） | P0（HUD 计数）/ P1（成绩记录=PlayerData 落盘） | ✅ 域 ScoreBoard（Score/Combo/Accuracy/Tp/Rank——m4 直取）；成绩记录=ms_assets_save_text（P1） |
| 音频 | 音乐引擎（WinMM 类）/音效 | WinMM AudioBackend+AudioClock+SoundFx | P0（音乐/暂停/恢复/Seek）/P1（音效合成/资源） | ✅ M1 WinMM 后端+AudioClock（采样时钟）；音效=🔧 P1（SoundFx 端口/合成） |
| 视觉 | D2D 渲染/质量档/粒子/转场 | IRenderer 软渲（M1）+ViewportPolicy+域 Storyboard+Fx（粒子 P2） | P0（分辨率/刷新率）/P1（转场）/P2（粒子/质量档） | ✅ ViewportPolicy（letterbox/DPI——P0-4 语义）；✅ 域 Storyboard（Fade/Slide/Wipe——t6 EN-4）；质量档/粒子=🔧 P2 |
| 编辑器 | 谱面编辑（事件/属性/判定线/音符/预览/AI 面板）/AI 检查/教练 | 六区编辑器+Editor 面板扩展（.mil 音符/事件编辑）+t131 | P1 | ✅ **六区编辑器=谱面编辑**（M3 已具：Hierarchy/Inspector/Project/SceneView+PlayController 快照）；✅ **AI 助手 t131=AI 检查**（R-01..R-12 规则+本地服务档——已设计待实现）；教练=ChartMentor 端口（P1 附项） |
| AI | AI 引擎/设备检测/AI 训练/谱面审查/AI 设置 | AiEngine 规则三档（重制内）+t131 A/B 档+AiSetup | P0（AI 游玩三档——GamePanel 语义）/P1（AI 设置页）/⏭（AI 训练=M27 跳过：工具链域外） | ✅ t131 设计（A 规则+B 本地服务档）；AI 游玩=域 RunAuto+规则 AI；设备检测=HardwareProbe 语义参考 |
| 数据 | 玩家数据/回放/配置/暗色模式 | Data JSON（ms_assets）+域事件录制回放+AppConfig 端口 | P1（玩家数据/配置）/P1（回放）/P2（暗色模式） | ✅ ms_assets_save_text（持久化）；回放=域事件录制（P1）；暗色=主题令牌（P2） |
| 联机 | 联机大厅/嵌入面板/排行榜 | ⏭ 范围外（B-8） | P2/跳过 | ⏭ 理由=网络=PlatformSockets 参考+零三方最小子集=另立决策（M28） |
| 工具 | 打包导出/parity 对拍/压测 CLI | CLI --preset/--preview/--mil-check+重制 ParityCli 对拍 | P1（重制 parity 对拍=G-1 自动化）/P2（打包导出） | ✅ ms_rhythm_preset_run（acc 对拍）+域 RunHeadless；打包=🔧 P2（PackExport 参考） |
| 优化 | 帧率治理/GPU 保护 | 重制循环=accumulator 驱动（t114 语义）+屏幕探测；GPU 保护=观察 | P0（刷新率主循环）/P2（GpuGuard） | ✅ Platform 无 FpsGovernor——重制实现（§6.3 M29）；GpuGuard=🔧 P2 |
| 其他 | 循环合成/皮肤设置/校准 | 域 LoopComposer+PresetLayout/PresetBackground+CalibrationPage（Dsp） | P1（回环/皮肤/校准） | ✅ 域 preset（PresetLayout/Background）；LoopComposer=展平导出 .mil（P1）；Dsp=P1（A-7） |

### 8.1 分级汇总（captain 建议 P0/P1/P2 口径）

- **P0（核心游玩链）**：导航（主菜单/选歌入口）+曲库（.mil 多模式扫描+AssetDatabase）+游玩（组件化 NoteSpawner/JudgementZone/LaneRenderer+FieldRenderer+HUD+暂停/跳过/重开）+判定（四档+热换）+结果（ScoreBoard）+音频（WinMM+AudioClock）+视觉（ViewportPolicy）——= §6.3 M1-M13 的 P0 子集。
- **P1（编辑器+AI+数据）**：六区编辑器谱面编辑+**AI 助手 t131**（自动游玩/检查）+练习/回放/皮肤/布局/段位/玩家数据/回环/校准+导入器（.osu/.adofai）+parity 对拍 CLI。
- **P2（联机/工具/视觉扩展）**：联机（跳过候选）、主题/转场/粒子/质量档/GpuGuard、打包导出、迷你 mania、暗色模式。

### 8.2 引擎能力复用速查（captain 标注四项+扩展）

| captain 标注 | 落点 | 说明 |
|---|---|---|
| AssetDatabase=曲库 | M1/X5-A-3' | root+List+Load+容器化（FNV 校验）——扫描零新件 |
| .mil=msjson | chartio | MSASSET 容器+payload（原文 JSON）+Rhythm::Chart 解析——msjson 经 Util（M4-a） |
| 六区编辑器=谱面编辑 | M14 | M3 已具（Hierarchy/Inspector/SceneView/Project/Console/Toolbar+PlayController 快照）——.mil 音符/事件扩展 P1 |
| AI 助手 t131=AI 检查 | M16 | R-01..R-12 规则+本地服务 B 档（默认关）——已设计 t130 待实现 |
| （扩展）IRenderer+FieldRenderer | M3/M5 | 10 原语+四族域渲染（录制→双面回放） |
| （扩展）JudgementProfile+Tracker | M4 | 四档+热换+事件（G-11 对拍 t6 契约） |
| （扩展）AudioClock=判定同步锚 | §6.1 | ExternalTimeMs 注入；PlayState.Reset=Seek(0)+时钟复位 |
| （扩展）Storyboard | M25 | Fade/Slide/Wipe——转场已具语义 |

*v4 终稿*（t132 四轮：引 captain 原始清单 original-feature-matrix.md 13 域→§8 对标映射（逐域=引擎版落点+分级 P0/P1/P2+可复用引擎能力标注）+§8.1 分级汇总+§8.2 能力复用速查（captain 四项+扩展四项）；细目=§6.3 M1-M30。**设计基线最终=§2（域）+§6.2（组件化）+§6.3（矩阵 M1-M30）+§7（蓝本确认）+§8（13 域对标）——可直接供 t133 实现任务引用；等待 captain 拍板 B-3'/B-9**）。

---

## 9. 决策预判确认：C# 主游戏层 + Python 可选脚本层（captain 倾向定案）

> captain：『C# 开发层为主（RhygeMaker.Bind 成熟（43+4 函数/八回调 thunk/CsTestRunner+csdemo 基座）/编译期强类型利于大型玩法/与 Unity 官方 C# 心智一致——原汁原味）；Python 层保持并存（脚本化小功能——t122 已验）。请按 C# 主游戏层 + Python 可选脚本层出组件化方案（GameEngine→Scene→GameObject→MeshRenderer/NoteSpawner/JudgementZone/MusicDriver——3D 组件也可用）』。**B-1 由【建议】转【定案】**。

### 9.1 C# 主游戏层（定案）

- **职责**：页面栈（导航/UI）+ PlayScene 全部玩法组件（§6.2）+ 编辑器扩展（Inspector 自定义——t119 语义）+ 重制全量逻辑。编译期强类型=大型玩法的容错与重构基础（与 Unity 心智一致）。
- **组件链（captain 链=官方口径）**：`GameEngine → Scene → GameObject → Component`（现有 Bind 面：GameEngine/Scene/GameObject/Transform/ComponentBase 八回调）——重制组件=**派生 ComponentBase**：
  ```csharp
  public class MeshRenderer    : ComponentBase, IRenderable  // 3D（t126）：RenderOrder 基准层
  public class NoteSpawner     : ComponentBase, IRenderable
  public class JudgementZone   : ComponentBase, IRenderable
  public class MusicDriver     : ComponentBase
  public class LaneRenderer    : ComponentBase, IRenderable  // 2D 域渲染分派（§6.2）
  public class HudLayer        : ComponentBase, IRenderable
  public class Camera3DComp    : ComponentBase                // 3D 相机（t126——3D 视图/3D 模式）
  ```
- **3D 组件可用（captain 确认）**：MeshRenderer/Camera3DComponent（t126 设计已落=C++ 反射组件 RHYGEMAKER_REFLECT）经现有通道入 C# 场景：`ms_go_add_component(go,"MeshRenderer")` + `ms_property_set(...)` （mesh/color/enabled——反射属性 JSON 通道 M2.5 已具）；3D 渲染=Render3D 模块 → **BlitRect 合成**（M3D-4：合成到 IRenderer 帧缓冲——IRenderable 通行证 RenderOrder<背景 前先跑 3D pass）。重制 3D 用途=主菜单背景/游玩 V 键 3D 视角（原版 GamePanel.Camera3D=t2 语义——P1 挂接）。
- **C# 基建（已具，直接复用）**：GameEngine（Handle/OnFrame/OnRender（M4-X 前的 P/Invoke 面）/Assets/Root/RunFrame/Run）+CsTestRunner（重制断言基座）+csdemo（parity 基座）——**M4.1 重制=P-Invoke 扩展面（ms_rnd_*/ms_text_*/ms_rhythm_*）在 RhygeMaker.Bind 内新增 Rhythm 代理命名空间**（RhygeMaker.Bind.Rhythm），不改动 GameEngine 既有 43+4 函数签名（兼容）。

### 9.2 Python 可选脚本层（并存——t122 已验）

- **定位**：脚本化小功能（生成器/工具/一次性分析/菜单小挂件）+ 引擎 cookbook 脚本版——**不承载重制主玩法**（主玩法=编译期强类型 C#——原汁原味）。
- **接入**：同一 ms_bind.h ABI（43+4+ms_rhythm_*）——py_demo 已验三语言 parity；Python 侧组件=PyComponentBase 八回调（_ComponentRegistry/CFUNCTYPE thunk）——**Python 脚本组件可注册进 C# 主场景吗？** 边界：**不建议**（场景组件宿主=C#；Python=独立解释器进程跑独立 Engine 或工具脚本——t121 外部解释器驱动模型）。P-7 对称承诺=ms_rhythm_* 同步 _interop.py（M4.2 验证）。
- **重制项目中的 Python 位**：数据工具（.mil 批量生成/校验钩子——unittest 7 组基座已有）、首启示例谱生成（原版 T7 引导——Python cookbook 或 C# 均可——推荐 Python（脚本化小功能口径））。

### 9.3 双语言边界表（重制开发契约）

| 面 | 语言 | 边界 |
|---|---|---|
| 游戏主逻辑/页面/组件/编辑器扩展 | C# | 全部（RhygeMaker.Bind+RhygeMaker.Bind.Rhythm） |
| 3D 组件 | C++（组件本体）+C#（装配/属性） | add_component+property_set 通道 |
| 域/Ruleset/FieldRenderer | C++（Rhythm::——性能/确定性） | ms_rhythm_* ABI（C# P/Invoke） |
| 脚本小功能/工具/生成器 | Python 可选 | 独立解释器；不嵌入主场景 |
| 断言/对拍 | C# CsTestRunner + C++ ms_test + Python unittest | 三语言 parity（renderhash 既有） |

### 9.4 验收增补

- G-14：C# 组件链走查（GameEngine→Scene→GO→MeshRenderer/NoteSpawner/JudgementZone/MusicDriver 装配+八回调序）+3D pass（MeshRenderer BlitRect 合成 Order=Background 前帧序断言）。
- G-15：Python 可选层=工具脚本独立跑通（生成示例 .mil → C# 曲库扫描可见）——双语言协作冒烟。

*v5 终稿*（t132 五轮：B-1 定案=C# 主游戏层 + Python 可选脚本层；组件链=GameEngine→Scene→GameObject→Component（MeshRenderer（3D·t126 经现有通道）/NoteSpawner/JudgementZone/MusicDriver/LaneRenderer/HudLayer——IRenderable 通行证）；3D 组件可用（BlitRect 合成 Order 定义）；双语言边界表（G-14/G-15 验收）；**设计基线最终=§2+§6.2+§6.3+§7+§8+§9——可供 t133 直接引用**；等待 captain 拍板 B-3'/B-9）。

---

## 10. t134 骨架先行引用清单（captain：交付中间版——先行启动 .mil 解析+时间轴）

> 指令：『设计主体已成框架→先交付中间版（可先行启动 t134 骨架：.mil 解析+Curve 时间轴——避免等全量完稿）；补全部分随尾注更新』。
> 状态：设计主体已全量完稿（v5 终稿 791 行）——无需另出中间版；本清单=t134 骨架的可直接引用的抽点（骨架先行，其余随 §5 顺序推进）。

### 10.1 骨架范围（t134 第一批）

| 件 | 引用章节 | 内容 | 验证断言 |
|---|---|---|---|
| .mil 解析 | §2.2 chartio（签名）+§7.1（实测字段契约：format/mode/title/artist/version/bpm/offset/audio/keys/notes{t,c,e,type,ec}/events{t,e,type,v,ev}）+A-3'（容器双态：磁盘原文/引擎 MSASSET+FNV）+§4（源参考行号） | ParseMilText/SerializeMil/LoadMilFile；MSASSET 容器化（msjson util M4-a） | **G9 前置修复后** R-5 初版：3 样本 round-trip（Mania/Phigros/Arcaea 示例）逐键一致（test_g9_red.exe=red 工具已备） |
| 时间轴 | §2.2 beat（BpmTimeline 签名+端口注意：同刻替换/惰性重建/银行家舍入）+§2.2 model（ChartData/BpmChange） | BpmTimeline（TimeToBeat/BeatToTime/BpmAt/SnapToBeat）+ChartData 容器 | R-1：4 组用例（往返/同刻替换/变速吸附/舍入对拍） |
| 域骨架 | §1（数据平面=纯数据无 Core）+§2.1（目录/命名空间/CMake 目标） | include/rhygemaker/rhythm/{beat,model,chartio}.hpp+src+ms_test 骨架+CMake | 编译绿+R-1/R-5 初版绿 |
| 事件曲线 | §6.3 M5（event=zoom/noteSpeed/scroll/Bezier——P1 但时间轴曲线可先行：EvalEvents/BuildSpeedKeyframes 端口） | 备注：Curve 时间轴=事件曲线（EvalEvents 语义）——骨架第二项可选（先行=纯 BpmTimeline；Curve 随 M4.1 全量） | 随 M5 |

### 10.2 骨架后第二批（§5 顺序 2-8）+第三批（M4-X+重制 P0）

- 第二批：field/judgement→preset（10 预设 R-4 首达）→ruleset（RunAuto）→render→story/practice/validator→ABI+CLI+cookbook。
- 第三批：M4-X（X1-X5）+重制 P0（组件化+页面栈）——§6.2/§9 签名即骨架扩展点。

*v6 终稿*（t132 六轮：中间版=设计主体已成框架（v5）=可直接引用——§10 输出 t134 骨架先行清单（.mil 解析+时间轴=第一批；断言 R-1/R-5 初版；事件曲线=可选先行备注）；t134 依赖（t132）已满足，captain 可即刻放行；补全项（story/practice/validator/ABI/CLI/cookbook/M4-X/重制 P0）随 §5 顺序+尾注更新——全部已在本文档，无未决设计项。

---

## 11. 拍板定案记录（captain · M4 决策——t134 执行基线）

| 决策 | 拍板 | 文档落点 | 执行基线（t134） |
|---|---|---|---|
| A-1 | ✅ 采纳 | §1（Util 提取 msjson+math→RhygeMaker::Util）+§5 第 1 步 | **M4 前置**：msjson/math 提取共享（Core 旧路径转发，行为零变；双源 MD5 校验） |
| A-3 | ✅ 采纳 | §2.2 chartio+A-3'（§7.2 双态容器） | .mil 全格式=M4；.osu/.adofai/.aff 导入=P1（chartio 钩子） |
| A-5 | ✅ 采纳 | §2.3 | ms_rhythm_* 并入 ms_bind.h 单头单 ABI（与 C#/Python 同契约；C# 侧=同一 DLL 附加 P/Invoke 段，不动既有 43+4 签名） |
| B-2 | ✅ 采纳 | §3.5/§6.3 | **M4 交付范围=域库+CLI+cookbook+重制 P0 核心循环**（M4.1）；M4.2-4.4（P1/P2）另立里程碑 |
| B-3 | ✅ 采纳 | §3.3 X1+§6.2（B-3'） | **OnRender 全委托（Godot _Draw 式）**：命令录制→双面回放；入口=IRenderable 场景遍历（B-3' 组件化细化——captain §6.2/§9 组件链指令一致；B-9 ABI v1.1（ms_component_spec 增可空 onRender+specSize 尺寸协商）=B-3 实现细节，随 t134 一并执行） |

**口径注（captain t132 v2 拍板——最终版）**：①**B-3' 采纳**（场景组件化：LaneRenderer/NoteSpawner/JudgementZone/MusicDriver+IRenderable 渲染通行证（背景1<音符2<判定线3<HUD9→命令录制→双面回放）；菜单/设置/结果=页面栈保留）——B-3（OnRender 全委托）与 B-3' 合并定案；②**B-9 采纳**（ABI v1.1：ms_component_spec 增可空 onRender+ms_component_register(e,spec,specSize) 尺寸协商——向前兼容）；③**功能矩阵 M1-M30 收讫**（B-11 冻结基准=输出产物/bin/net8.0-windows/win-x64/Milestone.exe ⭐；M27 AI 训练=M4 范围外 ✅；M28 联机=B-8 范围外 ✅）；④**任务映射（最终）**：M4 实现=t134（Rhythm.* 域库+CLI——eng-coder-vis）+t138（C# Milestone.Game 游戏层——coder-vis）；t133=编辑器修复批（--editor 崩溃+文字放大）非 M4；⑤未拍板项（X3 RuntimeFont/X4 Input 扩展/X6 音频变速/B-4 自绘 Widgets/B-5 位图近似）按文档建议值默认执行（偏差登记尾注）；⑥里程碑基线=§0-§11（WIP v0.9+全量尾注）——t134/t138 开工即有效。

*v7 终稿*（t132 七轮：captain 拍板 A-1/A-3/A-5/B-2/B-3（含 B-3'）定案入档 §11——t134 执行基线冻结；设计交付完成）。

---

## 12. t139 实现评审记录（eng-design-vis · t139 完成后——Rhythm 域首批）

> 评审对象：t139（beat/model/chartio——Rhythm 域首批；R-1 时间轴+R-5 .mil round-trip 首批口径）。
> 自验（eng-coder-vis）：ms_test_rhythm 5 用例（新增）+全链 76+；结构：include/rhygemaker/rhythm/{beat,model,chartio}.hpp+src/rhythm/chartio.cpp+tests/rhythm/{test_main,test_chartio}.cpp；CMake 接线 src/rhythm+tests ms_test_rhythm（ctest 已注）✅。

### 12.1 验收 mapping（R-1/R-5 首批 — 通过）

| 项 | 证据 | 判定 |
|---|---|---|
| R-1 时间轴 | test L10-28：恒定 120→变速 180/BpmAt 段界/同刻替换 1e-9（180→200）/TimeToBeat↔BeatToTime 往返（1e-6）/SnapToBeat 变速吸附 | ✅（四组覆盖=恒定/单变速/多段替换/反演） |
| R-5 首批 | test L30-53 六真实样本（Mania/Phigros/Arcaea/Cytus/IIDX/多模式——绝对路径链输出产物/bin/Chart/Milestone示例）+L57-89 Mania 逐键（title/mode/keys/notes[0].TimeMs·Lane/hold EndMs·Type）+Serialize→Parse 同字段+L93-99 错误行号/format 拒绝 | ✅（首批口径） |
| 容器 A-3' | test L103-117：MilkContainerMake/Parse round-trip（type=mil/guid/payload）+FNV 损坏→拒绝 | ✅（域层编解码；入库接线=t134=偏离②） |

### 12.2 偏离记录（5 项——全部采纳+注解）

| # | 偏离 | 处置 |
|---|---|---|
| ① | **chartio 首批键=t/c/e/type/x/y/ex/ey/ec**；lineParents/lineMeta/parts/stages=跳过保留（P1 结构映射）——R-5 **全格式**口径未达（设计 A-3：.mil 全格式=M4） | 采纳——**R-5 全量验收分两段**：首批（6 样本+逐键+round-trip）✅；全量（15+ 语料全键）随 t134 chartio 全键实现后补验（多模式/parts/stages 样本=全键门） |
| ② | **MSASSET 容器化入库（AssetDatabase 透明容器 Load→type=mil）未接**——当前=域层容器编解码 | 采纳——**t134 接线项**（ms_rhythm_chart_load 经 AssetDatabase；X5/A-3' 引擎侧） |
| ③ | **M4-a Util 未提取**——chartio.cpp 直 include core/assets/msjson.hpp（**cpp 级 1 处**） | 采纳——**R-9 口径**：头文件层零 core=✅（beat/model/chartio.hpp 仅 std+自身）；cpp 级 msjson 归属=**M4-a 必做首步**（captain A-1 已拍板；切换 util/msjson.hpp 后 R-9 含 cpp 断言方可达） | **✅ 已闭环（t139 后 M4-a 首步）**：include/rhygemaker/util/msjson.hpp（RhygeMaker::Util 转发：JsonValue/JsonParse/JsonStringify/Fnv1a64Bytes）+chartio.cpp 切 util 头——**rhythm 头+源零 Core include（grep 断言 0 匹配）——R-9 头级+cpp 级达成**；基线 ms_test_rhythm 13/13+全链 91+ctest 6/6+0 警告+MD5=0。
| ④ | windows.h 直依赖（UTF-8 路径 _wfopen——中文名样本） | 采纳——一期 win-only 可接受（V3-3）；零第三方红线不破（系统头）；**跨平台 M7 需 Platform::FileIO 抽象**（登记） |
| ⑤ | **15+ 语料库未达**（当前 6 样本） | 采纳——**语料目录路径=输出产物\bin\Chart\测试格式\**（11 个 .mil + exports 2 + 段位文件）；**当前键集可直接接入**：adofai_h/switch_test/arcaea_long/arcaea_skyheight_test/iidx_test/loopcompose_test/loopcompose_empty（7 个）；**需全键后接入**（偏离①门）：multi_stage_test/phigros_dual/arcaea_spec/lineparents_test（4 个）——首批=6+7=13 样本（≥12 即达 R-5 首批规模）；全量=15+ 随 t134 |

### 12.3 观察项（不阻塞）

- O-1：测试绝对路径=机器相关（D:/Users/etgya/Desktop/milestone/...）——**建议 CMake 传参（-DCORPUS_DIR）或相对路径探测**（迁移/CI 风险——BOM 坑录案 §9 同族）。 **✅ 已闭环（t134 收官）**：MS_MIL_ROOT_DEFAULT 编译注入（仓库树 tests/data/mil）+MS_MIL_ROOT 环境变量覆盖——等价于 -DCORPUS_DIR 语义（CMake 注入默认值+外部覆盖）；命名微调非必要，随 M7/发布链如需对齐再改（已答复 eng-coder 无需即改）。
- O-2：样本解析容差（parsed>=5 of 6）——多模式示例（parts/stages）的"格式变化容忍"注记=偏离①外显；建议：容差只对"已宣布支持键集样本"，全键后零容差。
- O-4：**错误行号透传=实现 ✅（JsonParse 失败→LineOf→out.Line；format 拒绝 L59-67）——但测试未断言具体行号值（L94-96 仅 Error 非空）——登记：补 1 用例（多行坏 JSON→Line>0 断言）随 t134 全量轮。

### 12.4 结论

**t139 通过（R-1 ✅ + R-5 首批 ✅ + 容器编解码 ✅）**——结构/CMake/测试链健康（域头零 core；实现=首批键+错误路径+中文路径实证）。**增量验收**（非失败）：R-5 全量（全键 15+ 语料）随 t134；R-9 含 cpp 口径随 M4-a 首步；引擎侧容器入库随 t134 ABI。**提醒 eng-coder-vis**：①语料接入路径=输出产物\bin\Chart\测试格式\（可先接 7 个）；②M4-a 切换后改 util 包含路径；③测试绝对路径参数化（O-1）。

### 12.5 R-5 口径终审（captain 提案——已确认接受）

> **联合通告（captain+eng-design 联签生效）：2026-08-29-Y-FINAL**——①终版=Y（parse-noted+Warning）确认（未改命）；②任何『21+3 铁令』=无效（基于过时消息）；③仅 captain+eng-design 联合通告可变更（本通告=Y 冻结确认）；④本小节=Y 唯一现行。

- **口径（终版·Y 定稿——captain 签名确认）**：R-5 全量补验=**『parse-noted+Warning=终态』**——全库 24/24 **全部解析**（零硬拒），P1 键结构（parts/stages/lineParents/lineMeta 未全映射）经 **Warning 通道记录**（非静默：Warning 含键名+P1 语义——严禁静默跳过/半解析）——**本态优于『21+3 显式拒』**：多模式/段位/多场样本可加载（与**原版 root 镜像语义一致**——硬拒=游戏层功能损失）；『21+3 显式拒』=历史裁决记录（不再执行）。
- **终态条件（Y 定稿）**：①语料测试=全库 24/24 解析零硬拒断言（parsed==24/failed==0）②Warning 通道=非空断言（P1 键结构样本 Warning 含键名+『P1 结构映射未全实现』语义）③**全键结构映射（parts/stages/lineParents/lineMeta 全模型）=P1 必做项保持**（M4.2——不因口径取消）④**round-trip 逐键=全部 24 解析样本全量执行**（Serialize→Parse 同字段）⑤设计 §2.2 A-3『.mil 全格式=M4』=保持（全键结构映射=P1 推进直至达成）。
- **登记**：R-5=parse-noted+Warning 终版（首批 6+13 → 全量 24/24 全解析+Warning+24 全量 round-trip）；P1 项=全键结构映射（M4.2）；R-2..R-6 随收官批（R-4 四族 RunHeadless 10 预设对拍 legacy=核心关口——G-1）。

---

## 13. t134 域库全量评审记录（eng-design-vis · t134 提交后——R-1..R-10）

> 评审对象：t134 域库全量（片1 judgement／片2 ruleset+render／片3 ABI+CLI+cookbook／补项 mil_asset+全键 chartio+R-9）。
> 自验（eng-coder-vis）：ms_test_rhythm 14 用例+全链（数量随批）+ctest 6/6+0 警告+黄金帧不变+MD5=0；哨兵修复（captain）+引擎使用者终验 acc100=18/18+Evaluate(0)=PERFECT。

### 13.1 逐项判定（R-1..R-10）

| 项 | 判定 | 证据/说明 |
|---|---|---|
| R-1 时间轴 | ✅ | test_chartio L11-28（恒定/变速/同刻替换/往返/吸附）——t139 已验持续 |
| R-2 Profile 工厂 | ⚠️ 部分 | 16 工厂存在（judgement.cpp L43-68）✅；**语义差异**：①档名=统一四档族 PERFECT/GR/GOOD/BAD（**≠v1 模式档名**：Arcaea PURE+ 25/50/100 三档/IIDX PGREAT…/Phigros Perfect…/Adofai PURE·PERFECT·COUNTED 角度表）②**窗值与 v1 不同**（如 v1 Arcaea Miss 100 vs 实现 90；Phigros 80/160/180 vs 实现 42/72/90…）③Adofai=1/12 拍近似（≠v1 deg/360×beatMs+30°→20ms cap）④**ScoreBoard=100 制+ACC=二分值**（≠v1 ScoreBoard 300 制/权重 0.5-1.0/TP/Rank）——**与 R-2『v1 常量固化』不符** |
| R-3 长条状态机 | ✅ **已交付（升级——t134 收官后轮）** | HoldState（None/Holding/Completed/Broken/Missed）+头/尾档位（HoldHeadTier/HoldTailTier）+ReleaseLane EndMs 位窗+自动完成+ResetHold（judgement.hpp L83-84/L103-110/L129）+8 用例单测（test_judgement L186『R-3：长条状态机（8 用例）』——rhythm 19/19 在册）——**P1 注记撤销** |
| R-4 四族 10 预设 | ⚠️ 部分 | test_ruleset L24-53：mania4 8/8 acc=1.000 ✅；**10 预设=构造谱（全 lane/keys=4 通用制造）→ hits≥8/10 容差**（Ring/Path 未真制造——注记）；**无与 legacy 同谱同偏移 1:1 对拍**（判定数/ACC/得分/评级/时间分布——R-4 全额口径未验） |
| R-5 .mil 往返 | ⚠️ 部分（当时口径——**最终定稿=§12.5 终版：parse-noted+Warning 24/24 全解析**） | 全键 chartio 已入（parts/stages/lineParents 样本可解析——16 样本语料≥15/≤1 容差）；当时强调 21+3 显式拒/round-trip 全量未达——**后由 §12.5 终版取代（24/24 全解析+Warning+24 全量 round-trip——优于 21+3）** |
| R-6 Tracker | ⚠️ 部分 | SetProfile 热换+Judge/Miss/Reset ✅（L94-112）；**PressLane=占位**（L100-105 恒 nullopt——片2 需 ChartData 接线）；PressRing/PressField=不存在；Judged/JudgedDetail 事件序=不在（headers 无事件回调）；二分下界（t9 语义）=未实现——**重制输入判定依赖** |
| R-7 Storyboard | 🔻P1 | story.hpp 不存在——报告注记 P1 |
| R-8 PracticeSession | 🔻P1 | practice.hpp 不存在——报告注记 P1 |
| R-9 域零引用+ABI | ⚠️ 部分 | 核心 rhythm 头/源零 Core ✅（grep 0 匹配）；**例外= mil_asset.hpp L2 include core/assets/asset_database.hpp**（资产登记=Core 集成）——例外登记（建议迁出 rhythm/→绑定/引擎层或列为白名单例外）；ABI 契约=bind 10/10（报告）✅ |
| R-10 cookbook/CLI | ✅ | tools/ms_rhythm（--preset/--preset-list/--preview/--mil-check）+cookbook A/B/C 3 文件+CMake 目标；render 四族非空测试 L55+；--preview 输出四族渲染路径 | 

### 13.2 核心结论与建议（供 captain 拍板）

- **主线判定：通过（M4 域库引擎语义版具备 R-1/R-9 核心/R-10 基座）——但 R-2/R-4/R-5/R-6 为『简化语义版』，与设计 R 验收的 v1 保真度存在 4 组差异**（档名/窗值/Adofai 公式/ScoreBoard 计分制+ACC 公式；Press*/事件/二分；R-5 严格口径；R-4 对拍）。
- **建议（推荐 A）**：A=**接受简化语义版为 M4 域库基线**——真 parity（同谱同偏移 1:1）归**重制 G-1 关口**（v3 headless vs legacy autoplay 自动化对拍——判定总数/ACC/得分/评级/时间分布）；差异登记表=§13.3（重制按此对拍时逐项修正）。B=返工 v1 逐档（窗值/档名/ScoreBoard 300+权重/TP/Rank/Adofai 公式）+R-5 严格口径——成本 1-2 周。
- **P1 必做（终态修正）**：R-7（Storyboard 全量）/R-8（Practice）——重制 P1 前必达；~~R-3（长条状态机）=已交付（升级为非 P1——§15.1 修正）~~；R-6 Press*+事件=Press* 已交付（P0 闭环；R-6 剩余事件回调+二分=P1/P2）。
- **R-5 补全**（§12.5 口径）：全库 24 路径+严格计数+拒串断言+round-trip 全量（语料缺失=switch_test/exports/段位——路径已在 §12 偏离⑤）。

### 13.3 语义差异表（简化版 vs v1——重制 G-1 对拍时逐项核对）

| 面 | v1（参考） | t134 实现 | 影响 |
|---|---|---|---|
| 档名 | 模式专属（PURE+/PERFECT/COUNTED/PGREAT…） | 统一 PERFECT/GREAT/GOOD/BAD | 判定文本/HUD 显示（重制显示层需映射） |
| 窗值 | 每模式独立（弧 25/50/100+Miss100…） | Profile16 统一参数（近似） | G-1 窗口分布 1:1 时需修正 |
| Adofai | deg/360×beatMs+30°/45°/60° cap（20/30/65ms） | 1/12 拍近似+cap 90 | ADOFAI 判定偏移差异 |
| ScoreBoard | 300 制+ApplyTier（tier.Score/Weight）+TP/Rank+ApplyTick/HoldTail | 100 制+Add(score,hit,breaks)+ACC=hits/(hits+misses) | ACC/得分/评级量化差异（G-1 对拍主源） |
| Tracker | Update(nowMs) scan+PressLane(lane,nowMs) 二分+PressRing/Field+HoldLane+Judged/Detail 事件 | Judge(noteTime,pressTime)+Miss+SetProfile；Press=占位 | 输入判定路径缺失（P0 建议） |

---

## 14. t134 补验闭环记录（captain A 拍板 + eng-coder-vis 3 项遗留处理）

> captain：A 采纳（§13.3 差异表=G-1 关口核对清单——正式 parity 验收主门）；R-6 升 P0（已并单）；R-5 §12.5 随末批；R-3/7/8=P1；**域库评审结论=通过（A 基线）**。

### 14.1 captain 拍板落点

| 项 | 落点 |
|---|---|
| A 基线 | §13.3 差异表=G-1 关口核对清单（真 parity=重制 G-1：同谱同偏移 v3 headless vs legacy autoplay——判定总数/ACC/得分/评级/时间分布；逐表项修正=G-1 实施内容） |
| R-6 升 P0 | 已并单 eng-coder 下一批（PressLane(lane,nowMs) 二分+PressField/PressRing+HoldLane+Judged/Detail 事件——签名=§2.2 judgement 既有） |
| R-5 | §12.5 **终版定稿=parse-noted+Warning（Y）**——**实际已闭环**：全键可解析（24/24 全解析+Warning 非静默）——**比 21+3 拒更优（已定稿采纳）** |
| R-3/7/8 | P1（长条状态机/Storyboard/Practice——重制 P1 批必达） |

### 14.2 eng-coder-vis 3 项遗留处理核验（✅ 已闭环 2.5 项+1 项收尾登记）

| # | 项 | 核验 |
|---|---|---|
| ① | M4-a util 切换 | 字面 R-9 ✅（域 8 头/5 源零 core 直引——grep 0 匹配；chartio/mil_asset.cpp 引 util/msjson.hpp）；**精确注记**：util/msjson.hpp=**别名垫片**（L3-4 自述：实现核心仍在 core/assets——避免巨量 namespace 迁移；头文件上 Rhythm→util→core **传递依赖 Core**——域可插拔性=语义折扣）——**＝eng-coder 自有注记『全量 util 迁移（cpp 级）=M4-a 收尾随批』→ 登记 R-9b：M4-a 收尾项（真迁移或 captain 拍板垫片=可接受〔单向 Core 恒在——bind 场景无影响；M7 跨平台前必须真迁移〕）** | **✅ captain 拍板（R-9b）：接受垫片**（域零直接 Core 引用=语义目标达成；bind 场景 Core 恒在=无影响）——**R-9b 登记为 M7 前置硬门**（跨平台前必须真迁移：util 独立实现或 vendored msjson）。
| ② | O-1+R-5 全量 | ✅ test L31-90（**终版=§12.5（Y 定稿）：parse-noted+Warning**）：MS_MIL_ROOT 环境变量优先>MS_MIL_ROOT_DEFAULT（CMake 注入=仓库树 tests/data/mil 24 样本 ASCII 化）>tests/data/mil 兜底——O-1 闭环；样本=24（段位挑战 2/exports 2/示例 6/测试格式 11/段位文件 3）；**终版 asserts：parsed==24 + failed==0（全解析零硬拒）+ Warning 通道非空（P1 键结构样本——键名+P1 语义——非静默）+ 全部 24 解析样本逐键 round-trip（Serialize→Parse：Notes/Mode/Bpm 同字段）**——『21+3 显式拒』=历史裁决被替代（已标注不再执行）；『24/24 全解析（零容忍）』=终版口径（此前的『中间态』批注=作废——以本节为唯一定稿） |
| ③ | O-4 行号 | ✅ test L113/118/123：首字符错→行 1／多行语法错→行 3／尾随字符→行 1；msjson JsonParse 增 errPos 出参（**additive 默认参数——ABI 兼容；现有调用不变**——core 侧变更=additive ✅）；行号稳定（LineOf(errPos)） |

### 14.3 结论

**域库评审终态=通过（A 基线）✅**——R-1/R-9（字面）/R-10 全 ✅+R-2/R-4=简化语义版（差异表=G-1 清单）+R-5=**24/24 闭环**+R-6=热换✅（Press*=P0 已并单）+R-3/7/8=P1。**遗留登记 1 项**：R-9b（util 垫片→真迁移=M4-a 收尾——M7 前置条件）。基线：15/15+全矩阵 94/94+corpus 24/24+0 警告+双树同步（MD5=0）。 **R-9 例外终审（captain 建议采纳）**：mil_asset.hpp 含 core/assets（资产集成适配器——与 render3d/mesh_asset.hpp 同类别——**白名单例外采纳（无需迁出）**；域 7 头（beat/model/chartio/judgement/ruleset/render/story）零 Core 0 匹配——R-9=通过（白名单例外×2：mil_asset 适配器+util 垫片〔R-9b=M7 硬门〕）**；R-4 真实族谱面（RingNote/Path 节点）构造+legacy 1:1=G-1 实施时补（captain A——RunAuto 语义不变——本批无需）。**

---

## 15. R-2..R-6 收官补验记录（eng-coder-vis · t134 收官后）

> 补验对象：R-2 工厂抽样/R-4 10/10 收紧/R-5 维持/R-6 Press* 真驱动（P0 项闭环）/R-7 story ABI（P1 注册口径）。
> 实测：rhythm 16/16（14→16）、全矩阵 95/95（19+20+10+23+7+16）、0 警告、双树同步。

### 15.1 补验判定

| 项 | 判定 | 证据/说明 |
|---|---|---|
| R-2 工厂 | ✅ 首过（补验口径） | 16 工厂全量抽样新用例：MissWindow>0/window>0/升序/Evaluate(0)==PERFECT——哨兵语义确认；简化版口径=A 基线（§13.3 差异表→G-1） |
| R-3 长条状态机 | ✅ **已交付（修正——本会话后续轮）** | HoldState 四态+头/尾档位+ReleaseLane 位窗+自动完成+ResetHold（judgement.hpp L83-110）+8 用例（test_judgement L186）——**P1 撤销；升级为非 P1✅** |
| R-4 四族 10 预设 | ✅ **10/10 精确**（升级） | test_ruleset L53 CHECK_EQ(hits,10)：同谱同偏移 RunAuto=音符时刻 0 偏移→best-tier acc=1.0000 逐预设 6/6；旧 ≥8/10 容差撤销；legacy 1:1=G-1 关口（A 基线保持） |
| R-5 | ✅ **24/24 全处理（终版=§12.5（Y）：parse-noted+Warning）** | parsed==24+failed==0（全解析零硬拒——P1 键结构样本=Warning 通道记录：键名+P1 语义——非静默）+**24 样本全量逐键 round-trip**（Serialize→Parse：Notes/Mode/Bpm 同字段）——corpus 仓库树 ASCII 化 24 样本（MS_MIL_ROOT_DEFAULT 注入+env 覆盖）——『21+3 显式拒』已被替代（历史裁决记录） |
| R-6 Tracker | ✅（P0 项闭环） | PressLaneAt(lane,nowMs)/PressField(x,y,hitRadius,nowMs)/AutoMiss(nowMs) 真驱动（judgement.hpp L97-99——tier/未中=-1）+SetProfile 热换+result board 真值——**先前「占位=恒 nullopt」已消除**；剩余=Judged/Detail 事件回调+二分 PressLane=P1/P2 登记 |
| R-7 Storyboard | ⚠️ P1 口径 | story.hpp 已立+ABI ms_rhythm_story_load/frame/release（ms_bind.h L124-126）——P1 注册口径（M4.2 全量事件） |
| R-8 PracticeSession | 🔻 P1 | 未提及——保持 P1 登记（practice.hpp 未见——M4.2） |

### 15.2 终态更新

**域库评审终态=通过（A 基线+收官补验）——遗留项收敛为**：R-4 legacy 1:1=G-1 关口（A 基线）、R-3/R-7（全量事件）/R-8=P1、R-6 事件回调+二分=P1/P2、R-9b=M7 硬门。**R-6 P0 项（Press* 真驱动）=已闭环**（先前 §13 P0 建议=已落实——感谢）。基线：16/16+95/95+0 警告+双树同步。

---

## 16. G-1 关口首验记录（captain 亲测——通过）

> 首验：g1_parity——v1 表 ↔ 域 Evaluate 逐 ms：**441/441 = 100%**（门=97%——超门）。'v1 表'=域判定语义参考表（四档 best-tier 选择+Multiplier 钳制+哨兵（WindowMs<=0 跳过）；逐整 ms 偏移网格）。

### 16.1 判定

| 项 | 结果 |
|---|---|
| G-1 首验 | ✅ **通过（captain 亲测）**——441/441 100%（gate 97%）——**核心引擎判定语义一致证明**（Evaluate 机械=四档 best-tier/升序/乘数钳制/哨兵语义） |
| §13.3 差异表 | 已知语义差异项（档名统一四档族/窗值近似/ScoreBoard 100 制）——**A 基线口径保持**（不因本对拍改变；重制 G-1 扩展时按游戏层对等实现：成绩/ACC/评级——coder-vis 双口径已备） |
| 对拍边界 | 本对拍=核心语义一致证明（Evaluate 层面）；**计分制/TP/Rank/时间分布**=留游戏层对等（重制 G-1 扩展：成绩/ACC/评级——§13.3 其余行） |

### 16.2 终态

**G-1 关口=首验通过**——重制阶段（G-1 扩展：真实输入→判定/计分/评级对拍）将按 §13.3 差异表全行核对；**域库链+G-1 首验=双闭环**（R-1..R-10 通过（A 基线）+g1_parity 100%）。













---

## 17. R-5 终版裁定记录（captain 最终裁决——Y 定稿·唯一定论）

> 裁定链：captain 最终裁决请求（X=21+3 显式拒 ／ Y=parse-noted+Warning）→ eng-design-vis 选 **Y**（理由：原版 root 镜像语义——多模式/段位/多场样本可加载；Warning 通道满足『严禁静默』；硬拒=游戏层 P0 功能损失）→ captain 签名确认 → **§12.5/§14.2②/§15.1 + 关联行=已统一为 Y**。
> **时间线澄清（防第三轮）**：eng-coder-vis 曾按早前记录（§14.2②/§15.1 '21+3 终态'批注）回切代码至 21+3 并锁定哈希——**该批注=裁定前的过渡表述，已被 Y 定稿取代**（本 §17 发布后）。**唯一定论=Y**：代码=parse-noted+Warning 态；断言见 17.1；锁定哈希=以 captain 发布清单为准（21+3 期锁定=作废）。

### 17.1 验收断言（Y 终版——唯一执行口径）

| 断言 | 值 |
|---|---|
| 解析计数 | parsed==24（全库零硬拒） |
| 失败计数 | failed==0 |
| Warning | parts/stages/lineParents 结构样本 → MilParseResult.Warning 非空（含键名+『P1 结构映射未全实现』语义——非静默） |
| round-trip | 全部 24 解析样本 Serialize→Parse 同字段（Notes/Mode/Bpm） |
| P1 保持 | 全键结构映射（parts/stages/lineParents/lineMeta 全模型）=M4.2 必做（不因终版取消） |

### 17.2 终态

**域库终态=全链一致**（Y 定稿后）：R-1..R-10 通过（A 基线）+R-5=parse-noted+Warning 24/24+R-9 白名单例外×2（mil_asset+util 垫片〔R-9b=M7〕）+G-1 首验 441/441；**代码/锁=以 eng-coder 按 Y 重切后+新锁定哈希为准**。

### 17.3 执行确认（eng-coder Y 冻结态——2026-08-29）

- **Y 态完成确认**：chartio parts/stages/lineParents=parse-noted+Warning（L131-137：键名+『P1 结构映射未全实现』——非静默；chartio.hpp L17 Warning 字段在位——无新 API）；测试断言=parsed==24/failed==0/Warning>=1 样本（键名+P1）（test L85-92）+24 全量 round-trip；复验=corpus 产品 24/24 errors=0 roundtrip=24·仓库树 25/25·acc100=20/20（20 非空+4 EMPTY——全部解析）。
- **锁=Y canonical（015A1098… 系列——双树；21+3 期锁=作废——以 captain 发布清单为准——规则③：发布哈希=权威登记依据；本档登记=eng-design 侧登记版）。**（eng-coder 交叉验证 2026-08-29：磁盘锁文件（双树 23:09:47/48）+4 件产物+发布 bin **4/4 hash 命中=B6C2169D/5CBD06C0/991CA5C1/286F4BB9**（Y FINAL 头注；=captain 23:01-23:09 重建落盘组）——官方清单与磁盘实际一致；015A1098…=重建前 Y 登记版（已作废）；CB586675…=旧 doc 值（无文件匹配）——三者关系=以 captain 公告+磁盘实际为准）
  - **CAPTAIN 官方 Y 终版 canonical（已写入 build-lock.sha256 双树——规则③转正依据）**：corpus_run B6C2169D…6EF8 · acc_run 5CBD06C0…BD8 · rhygemaker.dll 991CA5C1…52B2 · librhygemaker_rhythm.a 286F4BB9…610A；**captain 实测基线**：全矩阵 105/105（19+20+10+30+7+19）· corpus 仓库树 25/25 errors=0 roundtrip=25 RC=0 · acc100 21/21 · 双树同步（chartio 4CFBA59D 等 MD5=0）· 树已从混建恢复。**此前任何哈希系列（015A1098/B0EC09D1/74F3B0D2/CB586675）=一律作废，以本清单为准**；R-5 终版=Y（parse-noted+Warning）冻结规则不变。
- **P1 保持**：全键结构映射（parts/stages/lineParents/lineMeta 全模型）=M4.2 必做。『21+3 显式拒』=历史记录不再执行。

*v8 终稿*（t132 最终裁定：R-5=Y 定稿（parse-noted+Warning 24/24）；§17=唯一定论；域库链+G-1 首验双闭环）。
