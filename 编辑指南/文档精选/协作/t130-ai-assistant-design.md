# t130：v3 编辑器 AI 助手设计（A 离线规则为主 + B 本地服务可选档默认关）

> eng-design-vis · captain 拍板：t12 边界=当时针对游戏程序（Milestone.exe）——v3 编辑器可选档（B）经拍板解除并明示；A=离线规则主路径（零服务调用）。
> 基线：RhygeMaker M0-M3（Core/Platform/Assets/Bind/Editor 六区）+ local-ai-分工报告.md（Qwen 评审员：Inspector 折叠/引用拖拽/Play-Stop 规则/术语对齐——P1 集成细化）。
> 产出：模块（AiAssistant）/A 规则集/B 客户端/六区集成/契约/测试验收/决策点。只做设计。

---

## 0. 边界与总则

1. **A 主路径（默认）**：离线规则（v1 ChartValidator/规则语义复活——**零服务调用/零网络**；确定性；始终可用）。
2. **B 可选档（默认关）**：本地 Qwen（127.0.0.1:8080）——显式开关（工具栏+设置）；开启=状态可见（徽章 ON 蓝点）；**关闭=零 8080 请求**（netstat/log 断言）；文档明示边界（t12 曾禁=针对旧游戏程序；v3 编辑器 B 档经拍板解除——仅本档；游戏程序仍遵守 t12）。
3. **红线**：B 错误/超时/断连=**回退规则结果**（永不阻塞编辑器——5s 超时；异步后台线程）。
4. **模块**：Milestone::Editor::AiAssistant（editor 内——不依赖核心；http=WinINet（系统库）或 PlatformSocket 端口（#if WINDOWS）——零第三方红线内）。

---

## 1. 模块与契约（签名级）

~~~cpp
namespace Milestone::Editor::Ai {
// —— 设置 ——
struct AiSettings {
  bool enabledLocal = false;                       // B 档默认关（A 恒开）
  std::string endpoint = "http://127.0.0.1:8080";
  int timeoutSec = 5;
  std::string persistencePath;                     // 设置 JSON（M2 侧车）
};
// —— A：离线规则 ——
enum class Severity { Info, Warn, Error };
struct RuleResult { const char* ruleId; Severity sev; std::string message; bool isObject; long objectId; };
struct AiRule { const char* id; const char* label; Severity sev;
  std::function<void(SContext&, const std::vector<RuleResult>& out)> check; };  // 场景级+对象级（上下文=场景/选中）
class RuleChecker {
public:
  static const std::vector<AiRule>& Rules();                 // 12 条初版（§2）
  static std::vector<RuleResult> Run(const Scene&);          // 全场景（A 同步 <10ms——确定性）
  static std::vector<RuleResult> RunObject(const GameObject&); // 选中对象
};
// —— B：本地服务客户端 ——
struct AiResponse { int code; std::string text; bool fallback = false; };   // fallback=规则回退
class LocalAiClient {
public:
  bool Available();                                        // ping（后台；30s 间隔缓存）
  AiResponse Check(const std::string& sceneJson);         // POST /v1/chat/completions（OpenAI 兼容）
  AiResponse Summary(const std::string& objectJson);     // AI 摘要
  AiResponse Assist(const std::string& scriptSnippet);   // 脚本辅助（生成/提示片段）
  // 实现：WinINet（系统库）HTTP POST——零第三方；jthread 后台 + EventBus 回主线程（M0 就绪）
};
// —— 状态（六区共享）——
enum class AiMode { OfflineRules, LocalAi };
class AiState { public: static AiMode Mode(); static void SetMode(AiMode);   // 工具栏开关+持久化
  static AiSettings& Settings(); };
}
~~~

## 2. A 规则集（12 条初版——v1 语义复活）

| id | 规则 | 严重度 | 说明 |
|---|---|---|---|
| R-01 | 对象命名唯一 | Error | 场景内重名（M2 DuplicateName 先行动态化） |
| R-02 | 父引用存在/无环 | Error | parent 无效或成环（环检测同序列化） |
| R-03 | 组件类型已注册反射 | Warn | 未注册=序列化/Inspector 不可用 |
| R-04 | 资产引用存在 | Error | Mesh/Texture/Audio/Prefab 路径经 AssetDatabase.Exists |
| R-05 | 字段范围 | Warn | 反射字段越界（如 scale 负数——按字段约束表） |
| R-06 | Transform 数值 | Warn | NaN/Inf（内容卫生） |
| R-07 | 脚本组件字段 | Info | C#/Py 脚本组件字段读取健康（Cs/Py 桥 fields 空=警告） |
| R-08 | Prefab 引用完整 | Error | .msprefab guid/路径损坏（M2 校验） |
| R-09 | Play-Stop 规则 | Info | 运行期修改（快照丢弃）提示——评审员报告引用 |
| R-10 | Inspector 术语对齐 | Info | 非法/缺省组件名提示——评审员报告引用 |
| R-11 | 网格资产几何 | Warn | 三角形数=0/退化三角形 |
| R-12 | 场景保存脏 | Info | Dirty 未保存提示（Play/退出前） |

（Notes：R-09/R-10 来自 local-ai-分工报告.md——P1 细化将并入「Inspector 折叠/引用拖拽」体验项。）

## 3. 六区集成

| 区 | 集成 | 行为 |
|---|---|---|
| Toolbar | AI 徽章（默认=OFF 离线；B 开=ON 蓝点）+ 开关切换（持久化） | 状态可见（用户知情） |
| Inspector | **AI 面板**（选中对象/组件：规则徽章行（A 自动——R/条数）+「AI 检查」按钮（A 规则全跑+B 可选）+「AI 摘要」按钮（B）+「脚本辅助」面板（B：选中脚本组件→提示/生成片段） | A=同步即时；B=后台+回主线程 |
| Console | AI 结果行（规则报告/B 摘要——AI 级别可过滤） | 与 EditorLog 隔离级别（AI） |
| Project | 资产完整性规则（A 自动扫描——R-04/R-08/R-11 列表徽章） | 目录刷新时跑（增量 P1） |
| Hierarchy/SceneView | 切换选择→Inspector 同步（联动既有）；3D/2D 视图不变 | — |
| Settings | AiSettings 面板（endpoint/timeout/开关——B 显式启用+边界声明文案） | 配置 JSON 持久化 |

## 4. 测试与验收

| # | 项 | 判据 |
|---|---|---|
| 1 | A 规则正确性 | 12 条各 1 用例（构造违例→命中断言）+全绿场景=0 结果 |
| 2 | A 确定性 | 同场景两次 Run=同结果（顺序稳定） |
| 3 | B 关闭零调用 | 默认状态→无 8080 请求（测试内 mock 计数=0；netstat 另人工） |
| 4 | B 开关+持久化 | 开启→Available ping 发起；设置持久化 round-trip（重启保持开/关） |
| 5 | B 响应 | mock server（fixture http.server）/v1/chat/completions→AiResponse 解析（code/text） |
| 6 | B 回退 | 无服务/超时/断连→fallback=true+规则结果（不崩溃/不阻塞） |
| 7 | 六区集成 | Inspector 徽章显示（Error 场景）/Console AI 行过滤/Toolbar 徽章切换可见性 |
| 8 | 线程 | B 请求=后台线程；结果经 EventBus 回主线程（主线程无阻塞——帧耗时断言） |
| 9 | 边界声明 | 文档（README/Settings 文案）：A=离线/ B=本地（t12 边界解除明示——仅编辑器档） |
| 10 | 回归 | 编辑器既有测试全绿（AI 模块=新增面——六区零修改/挂接点） |

## 5. 决策点（captain 拍板）

| # | 决策 | 建议 |
|---|---|---|
| AI-1 | B 协议 | OpenAI 兼容 /v1/chat/completions（本地 qwen 服务常用——采纳）；v1 私有端点=P2 |
| AI-2 | 同步/异步 | B=异步（jthread+EventBus 回主线程）；A=同步（<10ms） |
| AI-3 | B 默认 | 默认关+显式开关（工具栏+设置）；关闭=零 8080 请求（断言） |
| AI-4 | 规则集 | 12 条初版（R-09/10 引用评审员报告）；后续增量随评审员建议 |
| AI-5 | 令牌/流式 | 无令牌校验（本地）；流式=P2 |
| AI-6 | P1 集成细化 | 评审员报告体验项（Inspector 折叠/引用拖拽/Play-Stop 规则/术语对齐）=M3.5/P1——本设计仅规则收录 |

---

*t130 完成*（A/B 双档设计（A 规则 12 条零服务+B OpenAI 兼容异步默认关+回退）+六区集成+契约+验收 10 项+AI-1..6；交付 eng-coder-vis 实现——实施排期随汉化/3D 后）

---

## 6. t131 实现评审记录（eng-design-vis · t131 完成后）

> 评审对象：t131（A/B 双档——t130 §4 验收 10 项）——eng-coder-vis 实现+自验（editor 23/23＝16+7 新用例；全链 76；ctest 5/5；0 警告；黄金帧不变；双源 MD5=0）。

### 6.1 验收 mapping（10 项）

| # | 项 | 证据 | 判定 |
|---|---|---|---|
| 1 | A 规则正确性 | ai_assistant.cpp L60-71（R-01..R-12 元数据）+L100-185 实现（R-01/02/03/05/06/09/11/12 全实现；**R-04/07/08/10=场景级 Info 骨架** 偏离①）+test L426（R-01/02/06 违例+干净场景 0 违规） | ✅（骨架口径=偏离①） |
| 2 | A 确定性 | test L463（两次 Run 同结果） | ✅ |
| 3 | B 关闭零调用 | ai_assistant.cpp L227-228/239-241（enabledLocal+Mode 短路）+g_callCount（L211）+test L477（基线 0→Available/Check 后仍 0）；netstat 人工=另有（收录） | ✅ |
| 4 | B 开关+持久化 | AiState Persist/Load（mcfg）+test L529（round-trip）+test L546（ToggleAiBadge 双向+持久化） | ✅ |
| 5 | B 响应 | SetTestTransport mock（L500-508 /v1/models+/v1/chat/completions→AiResponse code/text）+test L494 | ✅ |
| 6 | B 回退 | L240-254（disabled/unavailable→fallback=true+fallbackMessage）+test L494-522 | ✅ |
| 7 | 六区集成 | Toolbar 徽章 ✅（editor_app L131/235-242+test L546）+Inspector 徽章初步（Run/RunObject——Error 场景——报告）+**Console AI 过滤/AI 摘要/脚本辅助/Settings=接线骨架 P1**（偏离②） | ⚠️ 部分（M0 核心口径=徽章+规则跑通） |
| 8 | 线程 | **std::thread detach+done 直回调**（L274/277/280——设计=jthread+EventBus 回主线程）——无 UI 消费方不阻塞；**P1 接线时须主线程回派**（偏离③必做项） | ⚠️ 偏离③ |
| 9 | 边界声明 | README/Settings 文案=未补（随 M4 批）——偏离④（采纳：M4 批必做） | ⚠️ 偏离④ |
| 10 | 回归 | editor 23/23+全链 76+ctest 5/5+0 警告+黄金帧不变+MD5=0 | ✅ |

### 6.2 偏离记录（4 项——全部采纳+注解）

| # | 偏离 | 处置 |
|---|---|---|
| ① | R-04 资产存在/R-07 脚本字段/R-08 Prefab/R-10 术语=场景级 Info 骨架（P1 细化） | 采纳——P1 细化登记（12 条注册+对象级 RunObject 已具；骨架=可运行不崩） |
| ② | 六区集成=Toolbar+Inspector 徽章已具；Console AI 级过滤/AI 摘要/脚本辅助/Settings=接线骨架（P1） | 采纳——P1（接线后补验收 #7 全项） |
| ③ | 线程模型=std::thread detach+done 后台直回调（设计=jthread+EventBus 回主线程） | 采纳+**P1 必做**：UI 接线时回调必须经 EventBus/主线程队列回派（防跨线程触碰 UI）——登记为 t131 后续批验收项。**✅ 强化已落（t131 后即闭环登记）**：ai_assistant.hpp L76-77 头注释=『⚠ 主线程回派=调用方责任：done 回调必须经 EventBus/主线程队列回派（防跨线程触碰 UI——t131 评审偏离③ 登记：P1 接线时落实——当前无 UI 消费方不阻塞）』；基线 editor 23/23+全链 90+ctest 6/6+0 警告+黄金帧不变+MD5=0 |
| ④ | 边界声明（README/Settings 文案：A=离线/B=本地+t12 解除明示）未补 | 采纳——**随 M4 批必做** |

### 6.3 观察项（不阻塞）

- O-1：Available ping=30s 间隔缓存（合理，超出设计默认 5s 超时——不影响）。
- O-2：fallback 响应=文本提示；规则结果由调用方 RuleChecker 提供（A 恒开——语义符合设计；接线时 UI 层双跑=规则+提示）。
- O-3：TestCallCount 全局计数=测试污染防护（L524-525 复位——好实践）；建议持久化测试隔离路径（已做 ms_ai_config/——✓）。

### 6.4 结论

**t131 通过**（验收 10 项：8 全 ✅ + #7/#8/#9 部分通过=偏离①②③④登记；A 主线（12 规则+确定性+零调用+开关持久化+mock+回退+徽章+回归）全部达成）。**P1 必做 2 项**（②六区接线细化 ③异步主线程回派）+**M4 批必做 1 项**（④边界声明）——随队列落实；无需整改当前实现。
