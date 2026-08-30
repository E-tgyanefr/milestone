using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ChartPlayer
{
    /// <summary>
    /// 曲库管理：纯逻辑类（t53 批2：删 UI 不删逻辑——引擎曲库页运行时委托本类执行数据/命令）。
    /// 保留：目录数据 _items、FilteredItems、RefreshData、PickFolder、ImportFiles、ScanZip、OpenDir、
    ///       DeleteSelected、CopySelectedPath、PathText/StatsText/Items、数据联动事件 FolderChanged。
    /// UI 外观由引擎曲库页（SecondaryPages.BuildFolderPage）承担；本类不再派生 UserControl。
    /// </summary>
    public class FolderPanel
    {
        readonly AppConfig _cfg;

        // 数据
        List<(string Path, string Ext, string Display)> _items = new List<(string, string, string)>();
        string _statsText = "";

        /// <summary>当前选中下标（引擎曲库页列表选择联动：删除/复制路径使用）。</summary>
        public int SelectedIndex = -1;

        // t53 批2：GoSong/GoHome 由引擎曲库页按钮直连页面跳转（本类不再发导航事件）；仅保留数据联动 FolderChanged。
        public event Action<string> FolderChanged;

        public FolderPanel(AppConfig cfg)
        {
            _cfg = cfg;
            RefreshData();
        }

        /* ---------- 数据 ---------- */

        /// <summary>当前曲库路径文案（引擎曲库页信息栏）。</summary>
        public string PathText => "📁 " + _cfg.ChartsFolder;

        /// <summary>统计文案（引擎曲库页信息栏；RefreshData 时更新）。</summary>
        public string StatsText => _statsText;

        /// <summary>全部条目（引擎曲库页列表数据，同 _items 来源）。</summary>
        public IReadOnlyList<(string Path, string Ext, string Display)> Items => _items;

        /// <summary>按筛选下标返回可见条目（与引擎曲库页筛选按钮同语义）。</summary>
        public List<(string Path, string Ext, string Display)> FilteredItems(int filterIndex)
        {
            var fi = Math.Max(0, Math.Min(9, filterIndex));
            var shown = new List<(string Path, string Ext, string Display)>();
            foreach (var it in _items)
            {
                if (fi == 0) { }
                else if (fi == 1 && it.Ext != ".osu") continue;
                else if (fi == 2 && it.Ext != ".mc") continue;
                else if (fi == 3 && it.Ext != ".sm" && it.Ext != ".ssc") continue;
                else if (fi == 4 && it.Ext != ".qua") continue;
                else if (fi == 5 && it.Ext != ".mil") continue;
                else if (fi == 6 && it.Ext != ".aff") continue;
                else if (fi == 7 && it.Ext != ".txt") continue;
                else if (fi == 8 && it.Ext != ".json") continue;
                else if (fi == 9 && Array.IndexOf(ChartParser.ChartZipExts, it.Ext) < 0) continue;
                shown.Add(it);
            }
            return shown;
        }

        /// <summary>刷新数据（公开：引擎曲库页刷新按钮直调同一实现）。</summary>
        public void RefreshData()
        {
            _items.Clear();
            try
            {
                if (!Directory.Exists(_cfg.ChartsFolder)) Directory.CreateDirectory(_cfg.ChartsFolder);
                foreach (var f in Directory.GetFiles(_cfg.ChartsFolder, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(ChartParser.ChartExts, ext) >= 0 || Array.IndexOf(ChartParser.ChartZipExts, ext) >= 0)
                        _items.Add((f, ext, Path.GetFileName(f)));
                }
                _items.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
            }
            catch { }

            // 统计（分隔符用全角竖线，字号加大，信息更清晰）
            int osu = _items.Count(i => i.Ext == ".osu");
            int mc = _items.Count(i => i.Ext == ".mc");
            int sm = _items.Count(i => i.Ext == ".sm" || i.Ext == ".ssc");
            int qua = _items.Count(i => i.Ext == ".qua");
            int other = _items.Count(i => i.Ext == ".mil" || i.Ext == ".aff" || i.Ext == ".txt" || i.Ext == ".json");
            int zip = _items.Count(i => ChartParser.ChartZipExts.Contains(i.Ext));
            _statsText = $"共 {_items.Count} 个文件　｜　osu! {osu}　｜　Malody {mc}　｜　SM/Etterna {sm}　｜　Quaver {qua}　｜　其他模式 {other}　｜　容器 {zip}";
        }

        /* ---------- 操作（公开：引擎曲库页按钮直调同一实现） ---------- */

        /// <summary>选择曲库文件夹。</summary>
        public void PickFolder()
        {
            using var d = new FolderBrowserDialog
            {
                Description = "选择曲库文件夹（作为内嵌选歌的目录）",
                SelectedPath = _cfg.ChartsFolder
            };
            if (d.ShowDialog(null) == DialogResult.OK)
            {
                _cfg.ChartsFolder = d.SelectedPath;
                _cfg.Save();
                RefreshData();
                FolderChanged?.Invoke(d.SelectedPath);
            }
        }

        /// <summary>导入谱面文件。</summary>
        public void ImportFiles()
        {
            using var d = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "谱面/压缩包|*.osu;*.mc;*.sm;*.ssc;*.qua;*.mil;*.aff;*.txt;*.json;*.mcz;*.osz;*.zip|osu!|*.osu|Malody|*.mc|SM/Etterna|*.sm;*.ssc|Quaver|*.qua|Milestone|*.mil|Arcaea|*.aff|Cytus|*.txt|Phigros|*.json"
            };
            if (d.ShowDialog(null) != DialogResult.OK) return;
            int n = 0;
            try
            {
                Directory.CreateDirectory(_cfg.ChartsFolder);
                foreach (var f in d.FileNames)
                {
                    var dest = Path.Combine(_cfg.ChartsFolder, Path.GetFileName(f));
                    File.Copy(f, dest, true);
                    n++;
                }
            }
            catch (Exception ex) { MessageBox.Show("导入失败：" + ex.Message); }
            Logger.Info("导入谱面文件 " + n + " 个到 " + _cfg.ChartsFolder);
            RefreshData();
            FolderChanged?.Invoke(_cfg.ChartsFolder);
        }

        /// <summary>扫描并解压容器（异步解压；完成后经 UiSync 回调回 UI 线程刷新）。</summary>
        public void ScanZip()
        {
            using var d = new FolderBrowserDialog { Description = "选择包含压缩包的文件夹（.mcz/.osz/.zip 将自动解压）", SelectedPath = _cfg.ChartsFolder };
            if (d.ShowDialog(null) != DialogResult.OK) return;
            string src = d.SelectedPath;
            string target = _cfg.ChartsFolder;
            // 压缩包解压在后台线程执行（多核利用），完成后经 UiSync 回调回 UI 线程
            Task.Run(() =>
            {
                int found = 0;
                try
                {
                    foreach (var f in Directory.GetFiles(src, "*.*", SearchOption.AllDirectories))
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (Array.IndexOf(ChartParser.ChartZipExts, ext) >= 0)
                            found += ZipImport.Extract(f, target);
                    }
                }
                catch (Exception ex)
                {
                    UiSync(() => MessageBox.Show("扫描失败：" + ex.Message));
                    return;
                }
                UiSync(() =>
                {
                    MessageBox.Show("扫描完成：共解压 " + found + " 个谱面", "曲库");
                    RefreshData();
                    FolderChanged?.Invoke(target);
                });
            });
        }

        /// <summary>打开所在文件夹。</summary>
        public void OpenDir()
        {
            try
            {
                Directory.CreateDirectory(_cfg.ChartsFolder);
                Process.Start(new ProcessStartInfo(_cfg.ChartsFolder) { UseShellExecute = true });
            }
            catch { }
        }

        /// <summary>删除所选（SelectedIndex 由调用方设置——引擎曲库页列表点击联动）。</summary>
        public void DeleteSelected()
        {
            int i = SelectedIndex;
            if (i < 0 || i >= _items.Count)
            {
                MessageBox.Show("请先选择一个文件", "提示");
                return;
            }
            var it = _items[i];
            if (MessageBox.Show("确定删除该文件？\n\n" + it.Path, "删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            try { File.Delete(it.Path); RefreshData(); }
            catch (Exception ex) { MessageBox.Show("删除失败：" + ex.Message); }
        }

        /// <summary>复制路径（SelectedIndex 语义同 DeleteSelected）。</summary>
        public void CopySelectedPath()
        {
            int i = SelectedIndex;
            if (i < 0 || i >= _items.Count) { MessageBox.Show("请先选择一个文件", "提示"); return; }
            Clipboard.SetText(_items[i].Path);
        }

        /// <summary>UI 线程同步回调（引擎曲库页宿主注入；默认直接执行——调用方保证在 UI 线程时）。</summary>
        public Action<Action> UiSync = a => a();
    }
}
