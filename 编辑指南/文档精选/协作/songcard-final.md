# T38 SongCard 覆盖核验报告（有曲目选歌态）

- 核验 bin = Milestone.dll 20:54:02 · 证据：构建产物/obj/anim-fixed/songcard/（t38-songcard-v2/v3/restart2）

## 核验结果

| # | 项 | 结果 | 证据 |
| --- | --- | --- | --- |
| 1 | 曲库指向测试格式（34+ 谱面） | ✅ 生效：曲库管理页 **30 个文件│osu! 8│Malody 0│其它格式 22│压缩包 0**，列表条目全文（Mile adofai h.mil / Arc arcaea arccreate test.aff / Mile arcaea_long.mil / Arc arcaea realspec test.aff / Mile arcaea_skyheight test.mil + 点击回显完整路径 D:\Users\etgya\Desktop\milestone\其他\Chart\测试格式\arcaea_realspec test.aff）——**曲目列表文本全文无省略/截断 ✅** | t38-songcard-v2.png |
| 2 | SongCardView 谱名/作者（选歌页有曲目态） | ❌ **不可达（P1 缺陷阻塞）**：选歌页（重启/config 生效后）仍「曲库为空」——**选歌页 SongCard 列表未与曲库目录同步**（曲库管理 30 文件 vs 选歌页空态；点击【📂曲库】/【去选歌】/重启均不修复） | t38-songcard-restart2.png |
| 3 | SongCard 谱名（如「Dense Stress 4K」「Oshama Scramble!(Uncut)」）全文 | ⚠ 未达成（选歌页空态无 SongCard——需先修同步缺陷） | — |

---

## 详细确认

### 曲库管理页（✅ 全文）
- 改 ChartsFolder→测试格式后曲库管理页 30 文件：路径行「D:\Users\etgya\Desktop\milestone\其他\Chart\测试格式」+ 统计「30 个文件│osu! 8│Malody 0│其它格式 22│压缩包 0」+ 谱面文件（30）列表 5 行（前段）+ 点击行回显完整路径——**全部全文清晰无省略/截断**。

### 选歌页（❌ P1）
- 选歌页（点击开始游戏）**始终「曲库为空」**——即使：①点选歌页【📂曲库】按钮 ②曲库管理页【去选歌】按钮 ③重启应用（config 生效）——**选歌页 SongCard 列表未与曲库管理 RefreshFolderView 同源**（独立空列表缓存）。
- 判定：**P1 产品缺陷**（曲库有 30 曲目但选歌页显示空态——用户核心路径「选歌」不可用曲目）。

---

## 判定汇总

**P0=0 / P1=1（选歌页 SongCard 列表-曲库同步缺陷）**——SongCardView 谱名/作者核验**未达成**（缺陷阻塞）。

**修复建议**：①选歌页进入/曲库目录变更时调用 RefreshFolderView/ShareSongList（与曲库管理同源加载 SongCard 列表）②修复后复测：选歌页应显示 34+ SongCard——核验谱名（adofai_h/arcaea_long/mania_dense_test 等）与作者 FitFont 全文。

注：测试完成后已恢复原 config（ChartsFolder=t+pazolite）。