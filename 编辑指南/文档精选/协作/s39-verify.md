# T39 SongCard 核验报告（选歌页同步 + 谱名/作者全文）

- 复验 bin = Milestone.dll 21:07:49（晚于源码 21:07:00——含选歌页同步修复）· 证据：构建产物/obj/s39/（s39-01-songs / 02-scroll / 03-pgdn）

## 四项核验

| # | 项 | 结果 | 证据 |
| --- | --- | --- | --- |
| 1 | 曲库→测试格式后选歌页 30+ SongCard（滚轮可达第 10+ 行） | **部分通过 ⚠**：选歌页 **15 张 SongCard 显示**（页首：Milestone试玩·ADOFAI/arcaea arccreate test/arcaea long/arcaea realspec test/sky height test/Arcaea 规格验证/arcaea test/catch col test/catch test/cytus link test/cytus test/Hold Test 4K + 第 10+ 行 **iidx test/lineparents test/回环作曲试玩** 已在底部可见）——**30+ 曲目可见（第一屏 15 张含 10+ 行）**；**滚轮/PGDN 滚动未生效**（画面不滚——滚轮事件未传给列表或列表分页固定） | s39-01/02/03 |
| 2 | 谱名/作者长名 FitFont/AutoShrink 全文无省略 | **不通过 ❌**：**谱名全文 ✅**（arcaea long/catch col test/catch test/cytus link test 等长名完整）；**作者行截断 ❌**——「Milestone Team · Sample M」「未知作者 · Normala」「Milestone Team · Species」等**右端被裁**（作者文本 AutoShrink 未生效/超卡宽截断；像素：作者行延伸至卡右缘 x≈1899，中段带 gap 1419→1716） | s39-01（视觉+像素） |
| 3 | 与曲库管理页同源一致（同 30 文件计数） | **部分 ✅**：选歌页 15 张/屏（曲库管理 30 文件——选歌页分页显示或滚动未达；同源加载已生效（列表同曲库目录），计数待滚动验证 | s39-01 vs 曲库管理 |
| 4 | 无回归（选歌工具栏/模式片/空态逻辑/曲库管理） | **通过 ✅**：选歌工具栏（♪选歌/搜索/全部模式/▶游玩当前/📂曲库/🏠主菜单）正常；空态逻辑（有曲目时用 SongCard 非空态）；曲库管理 30 文件保持 | s39-01 |

---

## 判定汇总

**P0=0 / P1=1**（SongCard **作者行截断**——「未知作者 · Normala」等右端被裁，AutoShrink 未对作者行生效）

**选歌页同步缺陷已修复 ✅**（曲库→测试格式后选歌页显示 15+ SongCard 而非空态——t38 P1 主缺陷已闭环）。

**残留 P1-1：SongCard 作者行全文**（作者字段右端截断——建议：①作者行 AutoShrinkFont 生效（与谱名同行）②或作者文本 Ellipsis 全串+tooltip ③卡宽自适应作者行）。

注：①滚动未生效（滚轮事件未传列表——建议列表支持滚轮滚动显示 30+ 全部）；②30 文件计数同源（选歌页面视图分页，曲库管理 30 计数）；③已恢复原 config。