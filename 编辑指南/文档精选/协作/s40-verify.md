# T40 SongCard 收口复验报告（bin 21:17:07：作者行宽 307+badge 331 + 滚动接线）

- 复验 bin = Milestone.dll 21:17:07（晚于源码 21:16:21——含作者行 250→307+badge 331、滚动 WM_MOUSEWHEEL+快捷键）
- 证据：构建产物/obj/s40/（s40-01-songs / s40-02-scroll）

## 两点复验

| # | 项 | 结果 | 证据 |
| --- | --- | --- | --- |
| 1 | 作者行全文（Milestone Team · Sample M 等不再被裁，badge 零交叠） | **通过 ✅**：作者行完整显示——「Milestone Team · Sample M」「未知作者 · NormalA→+badge ▲」「Milestone Team · SpecfTest + ▲」「Milestone Team · Test M + M」——**badge（▲/C/A/M）独立卡右端，与作者文本零交叠**（t39 截断已修；作者行宽 307+badge 331 生效） | s40-01-songs.png |
| 2 | 滚轮/PgDn 到第 10+ 行（iidx/回环作曲/末行可达，ClipChildren 无穿卡） | **通过 ✅**：WM_MOUSEWHEEL 滚动生效——下滚后 **iidx test（7K·IIDX）/ lineparents test（4K·Phigros）/ 回环作曲试玩（Milestone·MVP 4K·回环作曲）** 到达底部；作者+badge 完整、**ClipChildren 无穿卡**（卡片文字未穿出卡框） | s40-02-scroll.png |

---

## 判定

**P0=0 / P1=0 —— SongCard 收口闭环 ✅**

- ① 作者行全文（Sample M/NormalA 等不再裁，badge 零交叠）——t39 P1-1 已修复。
- ② 滚轮滚动到第 10+ 行（iidx/lineparents/回环作曲试玩 末行可达）+ ClipChildren 无穿卡。
- 选歌页同步（t38）已在 t39 闭环 + 本 t40 作者行/滚动收口——**SongCard 完整闭环**（谱名/作者/badge 全文 + 30+ 曲目滚动可达）。

注：已恢复原 config（ChartsFolder=t+pazolite）。