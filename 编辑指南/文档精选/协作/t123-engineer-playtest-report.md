# t123 引擎项目编辑验证+找错（RhygeMakerEditor 22:46:35）

- exe = D:\Users\etgya\Desktop\rhygemaker\build\tools\RhygeMakerEditor.exe（22:46:35 2.24MB --editor）· PATH=mingw64/bin+rhygemaker/build/src/bind · 证据=输出产物/dev/obj/verify-m3/（m3-01 全幅/m3-02 small/m3-03 large）

## ① 编辑项目验证

| 项 | 结果 | 证据 |
| --- | --- | --- |
| 启动 --editor | ✅ 窗口 1944×1259（RhygeMaker Editor）六区：顶 Play/STOP/Pause/SaveScene/Load · Hierarchy · Scene 网格 · Inspector · Console（『RhygeMaker 编辑器 v0.1.0 版...』） | m3-01 |
| Hierarchy 选择→Inspector 编辑 | ⚠ 未达——**空场景 Hierarchy 无对象**（无 New/Add 对象入口按钮可见（顶栏仅 Play/STOP/Pause/SaveScene/Load）——**候选 P2：空 Hierarchy 无创建对象入口**（用户无法从空场景开始编辑链路——需查：创建对象入口（右键/快捷键/按钮）） | m3-01 |
---

## 更新（编辑链路验证——自动交互受限标注）

- **New（新建对象）按钮存在 ✅**（m3-12 顶栏——六区 UI 齐全：播放/停止/暂停/保存/加载/新建对象+层级/检查器/资产项目/控制台——编辑入口齐备）；
- **自动化点击 New 未触发**（m3-14/15——鼠标点击 Hierarchy/New 无反应——**自绘 UI 输入限制**（同 t42/B2 观察——引擎自绘窗 mouse_event 不可达——UI 元素齐全但自动化交互不可达）；
- **编辑链路（New→选中→Inspector 改→保存==重开→Prefab→Play-Stop）= 人工/键导验证项**（功能 UI 齐全——交互需真用户/键导（Tab 焦点导航）——**建议人工 30 秒**（New→选中→改字段→SaveScene→Load==→Play→改→Stop 快照））。
- **③ 继续找错**（长名/连击/场景切换/DPI/Inspector 焦点编辑）——待键导/人工时同步（自动交互受限——下列人为验证流：新建超长名对象/连击 New/DPI 切换后布局/焦点编辑——人工清单）。