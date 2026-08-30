using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChartPlayer
{
    public partial class GamePanel
    {

        int SumHits()
        {
            int s = 0;
            foreach (var kv in _hits)
                s += kv.Value;
            return s;
        }


        /// <summary>Cytus 初代 TP：PERFECT=100 / GOOD=70 / BAD=30（含 BAD 分母；D2 修正——此前误用 Cytus II 的 C.PERFECT 口径）。</summary>
        double CytusTp()
        {
            try
            {
                int p = 0, g = 0, b = 0;
                foreach (var kv in _hits)
                {
                    if (kv.Key == "PERFECT")
                        p += kv.Value;
                    else if (kv.Key == "GOOD")
                        g += kv.Value;
                    else if (kv.Key == "BAD")
                        b += kv.Value;
                }

                int total = p + g + b;
                if (total <= 0)
                    return _acc; // 尚无判定时按 ACC 兜底显示
                return 100.0 * (100 * p + 70 * g + 30 * b) / (100.0 * total);
            }
            catch
            {
                return 0;
            }
        }


        /* ================= t63 布局自定义（B1/B2）：当前模式生效值读取 ================= */
        string _lcMode;

    }
}
