# T38 SongCard 覆盖补验报告（曲库→测试格式）

- 复验 bin = Milestone.dll 20:54:02 · 证据：构建产物/obj/diagnose/（t38-songcard / t38-songcard-final / t38-songcard-restart）

## 复验结果

| # | 项 | 结果 | 证据 |
| --- | --- | --- | --- |
| 1 | 曲库指向测试格式（34+ 谱面） | ✅ 生效：曲库管理页显示 **30 个文件│osu! 8│Malody 0│其它格式 22│压缩包 0**，列表条目全文（Mile adofai h.mil / Arc arcaea arccreate test.aff / Mile arcaea_long.mil / Arc arcaea realspec test.aff / Mile arcaea_skyheight test.mil） | t38-songcard.png |
| 2 | **选歌页 SongCard 列表** | ❌ **P1 缺陷：选歌页仍「曲库为空」**（曲库管理 30 文件 + 重启后选歌页仍空态）——**选歌页 SongCard 列表未与曲库目录同步**（使用了另一个未刷新的列表缓存） | t38-songcard-restart.png |
| 3 | SongCardView 谱名/作者 FitFont 全文 | ⚠ 未达成：选歌页空态无法显示 SongCard → 谱名/作者视图**不可达**（需先修选歌页空态同步缺陷） | — |

---

## 详细确认

### 曲库管理页（✅）
- 改 ChartsFolder→测试格式后：曲库管理页立即显示 30 文件（osu! 8 等），列表条目全文清晰。

### 选歌页（❌ P1）
- 选歌页点击📂曲库后**仍显示「曲库为空」**（曲库管理页却 30 文件）；重启（config 生效）后**继续空态**——**选歌页 SongCard 列表使用了与曲库管理 RefreshFolderView 不同的列表源/未刷新**。
- 判定：**选歌页有曲目也显示空态 = 曲库列表同步缺陷（P1）**——用户选中曲库目录后选歌页不显示 SongCard，SongCardView 谱名/作者因此无法覆盖验证。

## 判定汇总

**P0=0 / P1=1**（选歌页 SongCard 列表-曲库不同步缺陷：曲库 30 文件而选歌页空态）

**SongCardView 谱名/作者覆盖未达成**——受选歌页空态缺陷阻塞（修好后即可核验 SongCard 全文）。

## 建议
1. 修选歌页 SongCard 列表：进入选歌页时调用 RefreshFolderView/ShareSongList（与曲库管理同源加载），而非空列表缓存。
2. 修复后复测：选歌页应显示 34+ SongCard（测试格式）——核验谱名（如 adofai_h/arcaea_long）与作者全文（FitFont AutoShrinkFont）。