# P3 发现：窗口标题实测『R』（GetWindowTextW len=1）——双因素根因（引擎侧，冻结=只报告）

- **发现**：引擎使用者波2窗口行为子测——窗口存在/输入泵正常/后台链绿色；标题=『R』（1字符）。本侧实证复现：发布树 exe（native 在位）→ EnumWindows+GetWindowTextW → title=[R] len=1（与报告一致）。
- **根因①（Engine 侧 Utf8ToWide off-by-one）**：src/platform/win32_window.cpp L15-23
```
int n = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, nullptr, 0);   // n=含 NUL 的 wchar 数
std::wstring w((size_t)(n - 1), L'\0');                               // ← 分配 n-1（少 1）——bug
MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, &w[0], n);              // ← 写 n 个（越界 1）——bug
```
MinGW COW std::wstring 缓冲越界写 NUL → 内存损坏 → 长度字段被破坏 → c_str() 截断
（RhygeMaker→R：首字符保留=长度=1 损坏形态；实测 GWC len=1 完全吻合）。
- **根因②（Bind 侧 Title 初始化次序，叠加放大）**：RhygeMaker.Bind/GameEngine.cs——Create() 在构造器内执行
（ms_engine_create(Title,...) 用默认 RhygeMaker），对象初始化器 new GameEngine { Title=... } 在构造器之后
才赋值 → 引擎窗口永远收不到游戏意图标题（无 set_title ABI——标题=创建即定）。
- **修复建议（B 批——eng-coder/captain 决策；本侧不做引擎改动）**：
  ①win32_window.cpp Utf8ToWide——**精度修正（引擎使用者独立复核确认，2026-08-29）**：仅 w.resize(n-1) 不改变 MBWC 写入量 n——越界仍在。推荐两选：
     A) `std::wstring w((size_t)n, L'\0'); MultiByteToWideChar(CP_UTF8,0,s.c_str(),-1,&w[0],n); w.resize(n-1);`
     B) `std::wstring w((size_t)(n-1), L'\0'); MultiByteToWideChar(CP_UTF8,0,s.c_str(),-1,&w[0],n-1); w.push_back(L'\0');`
     （A=缓冲足额含终止符写入后截掉终止符；B=写 n-1 无终止符+末尾手动补 NUL——语义等价均有效）
     ※ 撤回此前『仅 w.resize(n-1)』不完整表述——以 A/B 为准；
  ②（可选）ms_engine_set_title(e, utf8) ABI + Bind GameEngine 重载/延迟 Create（标题=创建时传入——与本 P3 同批）；
  ③Bind Title 语义文档化（创建后 SetTitle 无效——当前行为=窗口标题固定于 create）。
- **影响面**：标题仅显示性（非功能链）；窗口行为子测其他项全绿——P3 非阻塞。游戏层侧无绕过（无 set_title ABI）——等待 B 批。
## 附：同类 pattern 排查记录（eng-coder 只读核实，2026-08-29）

- **同 off-by-one 的工程内 pattern**（非本 P3 修改范围——冻结）：
  - `tools/ms_rhythm/corpus_run.cpp` ToW（类似 Utf8ToWide——tools 非冻结，B 批可同修）；
  - `scripts/acc_run.cpp` ToW（同上）；
  - `chartio.cpp LoadMilFile` wpath（UTF-8 路径→wide——**属 R-5 冻结文件**：需 captain+eng-design 联合通告方可动）；
- **修复归属**：①win32_window.cpp Utf8ToWide=A/B 式（eng-coder B 批；corpus_run/acc_run 同批可修）②ms_engine_set_title ABI=可选加分项（不影响显示性）③chartio wpath=冻结+联合通告流程。
- **结论**：P3 非阻塞 ✓（标题仅显示性——GWC 截断 R + 双因素根因确认属实；本报告 A/B 修复规格最终定稿）。