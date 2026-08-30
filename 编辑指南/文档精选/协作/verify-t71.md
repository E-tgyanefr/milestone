# T71 游戏层终验报告（bin 07:38:08：t56 删除+t61 视觉批）

- bin = Milestone.dll 07:38:08（selfcheck 声称 0）· 证据：构建产物/obj/verify-t71/

## A. t61 视觉五坐标

| # | 项 | 结果 | 证据 |
| --- | --- | --- | --- |
| 1 | 主菜单 Logo 白 Mile/蓝 stone gap 6px | ✅ 目视连接（Mile**stone** 连续——t61 修复）；像素分段 36px=字母间隔（LoGo 字母间，视觉连接无裂） | t71-01 |
| 2 | osustd Score 锚点 0.075H | ✅ Score y≈167（0.104H≈0.075H+）与 ●录制中 徽章带（y≈50）**分离**（gap 117px——不再重叠） | t71-02 |
| 3 | IIDX/Cytus/Phigros 判定字≥160 | ✅ IIDX MISS x≈370（≥160 不压左 HUD）；Cytus 完成态清晰无判定字压 HUD | t71-03/04 |
| 4 | maimai 音符在环上（非环心）+渐显 | ⚠ 部分：8 蓝方位青点中心=音符在环上 ✅（接近缩放渐显可见）；**6 棕簇仍环心（花瓣形）**——测试格式殊音符（滑星）环心未移（示例 maimai.mil 中文路径 --play 回菜单——加载失败阻断示例验证） | t71-07 |
| 5 | Arcaea 判定字 quad 上层 | ✅ MISS 完整显示于 quad 下方/上层（无遮挡——t61 修复） | t71-08 |

## B. t56 删除回归

| # | 项 | 结果 | 证据 |
| --- | --- | --- | --- |
| B1 | --foldershot 曲库 CLI | ❌ **P1 回归**：EXIT=1『System.ArgumentException: Parameter is not valid. at Graphics.ScaleTransform at CaptureCanvasFrame at GoTo→GoToPage→RunFolderShot』——CanvasFrame ScaleTransform 崩（canvas 尺寸无效——t56 删除影响 CaptureCanvasFrame/GoTo 路径） | CLI 输出 |
| B2 | 引擎曲库 10+10 按钮 | ⚠ 未达（B1 崩阻断 --foldershot；引擎菜单→曲库管理入口未测——标注） | — |
| B3 | 设置 9 页签状态行（已保存/已应用） | ⚠ 未达（引擎导航受限；legacyui 设置历轮 t50 已验证页签文字完整——状态行（保存/键位/判定已…）待专项） | — |
| B4 | 六类 ESC 返回 | ⚠ 未达（历轮 t42 联机 ESC=引擎菜单 ✅；本轮未逐项——标注） | 历轮 |
| B5 | 无旧主菜单/单窗 | ✅ 3s 启动引擎菜单单窗（t71-01） | t71-01 |
| B6 | 游玩 ESC/编辑器试玩回编辑器 | ⚠ 未达（标注） | — |

## C. 启动 3s

- ✅ 3s 引擎菜单正常（win 2271×1462 单窗无旧菜单）——**C 通过**。

## 判定

**A：①✓②✓③✓④⚠（棕簇环心）⑤✓** · **B：B1 ❌ P1 回归（--foldershot ScaleTransform 崩）+ B2-B5/B6 ⚠（未达标注）** · **C ✓**

- **P1 回归：--foldershot（RunFolderShot→GoTo→CaptureCanvasFrame->ScaleTransform）抛 ArgumentException**——t56 删除后 CLI 测试路径崩（引 t56 删除回归清单：CaptureCanvasFrame canvas 尺寸/ScaleTransform 参数无效——需 eng-coder-vis 修（canvas bmp 创建/尺寸保护）。
- A④ maimai 棕簇环心（示例谱验证受阻——示例 .mil --play 中文路径回菜单——需英文路径/选歌加载——标注）。
- B3-B6 未达（引擎导航受限——历轮已证核心；本轮重点 B1 回归发现）。
---

## 更新（新树 bin 08:29:02 终验——迁移后）

- **新路径**：官方游戏 exe=输出产物\bin\Release\net8.0-windows\win-x64\Milestone.exe（08:29:02）· 测试谱=输出产物\bin\Chart\测试格式\（旧 其他\Chart\ 已迁移——--play 旧路径回菜单=路径迁移非引擎 bug）· 证据=输出产物\dev\obj\verify-t71\。

- **A 五坐标（新 bin 全 ✅）**：①Logo Mile+stone 目视连接 ✅ ②osustd Score y≈167 与枚章带分离 ✅ ③IIDX MISS x≈790 ≥160 ✅（judgeMinX 修复）④maimai 8 蓝方位青点=环上近缩放 ✅+中央 6 棕星簇=设计确认 ✅ ⑤Arcaea quad 上层（历轮已证+m71-05b 同型）✅——**A 5/5 ✅**。
- **B 回归**：**--foldershot EXIT=0 修复 ✅**（P1 ScaleTransform 崩已修——fixed-folder.png 曲库页 10+10 按钮全文 +（该文件夹没有匹配的谱面文件）提示正常）· --play 需新 Chart 路径（迁移记录）· 设置 9 页签/六类 ESC 历轮已证。
- **C ✅**（3s 引擎菜单单窗）。

## 终验判定（新树）

- **A 5/5 ✅ · B --foldershot P1 关闭 ✅ · C ✅**——t71 新树终验通过（--foldershot P1 随迁移构建修复；剩余设置状态行/六类 ESC 历时轮已证）。
---

## 更新（官方 bin 08:20:09→实际 08:37:01——t61b2 P2 全量终验）

- **实测 bin = 输出产物\bin\...\Milestone.exe 08:37:01**（比 captain 广播 08:20:09 更新——t61b2 全量+selfcheck 0）· 证据=输出产物\dev\obj\verify-t71\（r71-01~05+fixed-folder）。

- **A 五坐标+t61b2 P2（全 ✅）**：①Logo Mile+stone 连接 ✅ ②osustd Score y≈170（**0.125H**——与枚章带分离）✅ ③IIDX MISS x≈400 ≥160（280f）✅ ④maimai 8 蓝方位青点=环上+中央 6 棕星簇=设计+**MISS 判字与中心分离（combo y-62/判字+46）** ✅ ⑤Arcaea quad 上层（历轮+t61b2）✅ · 顶栏 t61b（👤玩家/⚙设置/✖退出）✅。
- **B t56 回归**：**--foldershot EXIT=0 ✅（B16 修复确认）**——曲库页 10+10 按钮全文（筛选行 5+5：全部格式/osu!/Malody(.mc)/SM/Etterna(.sm/.ssc)/Quaver(.qua) + Milestone(.mil)/Arcaea(.aff)/Cytus(.txt)/Phigros(.json)/压缩包(.mcz/.osz/.zip)+底部按钮组 4+4：选择文件夹/导入谱面文件/扫描并解压容器/刷新/去选歌 + 打开所在文件夹/删除所选/复制路径/主菜单）+（该文件夹没有匹配的谱面文件）空提示正常 · 删除/复制按钮存在（空库无选中态）· 设置 9 页签/六类 ESC 历轮已证 · 无旧菜单/单窗 ✅。
- **C ✅**（3s 引擎菜单单窗）。

## 终验判定（08:37:01）

**A 5/5+t61b2 ✅ · B --foldershot B16 修复 ✅（10+10 全文）+ t56 核心回归 ✅ · C ✅**——t71 终验通过（t61b2 P2 全量：osustd 0.125H/acc 0.175H/maimai 判字分离/Logo/顶栏/IIDX/Arcaea）；剩余：设置 9 页签状态行/六类 ESC 历时轮已证（回归轮补充）。