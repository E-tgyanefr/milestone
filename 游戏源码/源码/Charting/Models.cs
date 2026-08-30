using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ChartPlayer
{
    /// <summary>
    /// 游戏模式（Milestone 全音游支持，注册表保留全部条目；已移除玩法 Removed=true）：
    /// Mania 垂直下落 · Phigros 动态判定线 · Arcaea 天地双轨+Arc · Cytus 扫描线 ·
    /// OsuStandard 圆点/滑条/转盘 · Adofai 冰与火之舞 · IIDX
    /// </summary>
    public enum GameMode
    {
        Mania = 0,
        Maimai = 1,
        Wacca = 2,
        Phigros = 3,
        Arcaea = 4,
        Cytus = 5,
        Deemo = 6,
        OsuStandard = 7,
        Taiko = 8,
        Catch = 9,
        Sus = 10,
        Sdvx = 11,
        MuseDash = 12,
        Adofai = 13,
        Rotaeno = 14,
        Dynamix = 15,
        Lanota = 16,
        ToneSphere = 17,
        Iidx = 18,
        Pump = 19,
        AdofaiReal = 20,   // 真实 ADOFAI（冰与火之舞）：双球轨道+每拍输入+角度判定+掉轨重开（Routlock=Adofai 13 保留）
        LoopComposer = 21  // 回环作曲：编玩一体化（录→奏→扩）——谱面是玩家的演奏，非预置谱面
    }

    public class Note
    {
        public double Time;      // ms
        public double End;       // ms（tap 时 == Time）
        public int Col;          // 轨道索引（键盘类模式）
        public double X, Y;      // 归一化位置 0~1（触摸类模式）
        public double EndX, EndY;// 滑动 / arc 终点位置
        public int EndCol = -1;  // arc 终点轨道（-1 表示与 Col 相同）
        public string Type;      // "tap" | "hold" | "arc" | "slide" | "flick" | "drag"
        public int Kind;         // 类型扩展：adofai 1=转角
        public double Bpm;       // 音符时刻当前 BPM（ADOFAI 变速段 SetSpeed 换算用；0=用谱面全局 BPM）
        public int Line;         // phigros 判定线索引 / arcaea 天空键高度（Y 另存）
        public bool Decor;       // Arcaea arc 装饰态（不可接触，渲染更细更暗）；Phigros 假音符（RPE Fake）
        // ===== RPE 音符编辑面板字段（D2r 复刻；实机判据 rpe_f3_60/f3_360/f3_420）=====
        public int Side = 0;         // 下落朝向：0=Up（上）1=Down（下）
        public double Width = 1.0;   // 音符宽度（1.0=默认）
        public double Alpha = 1.0;   // 透明度（1.0=255 全亮；0=完全透明）
        public double VisMs = 999999; // 可视时间（RPE visibleTime，秒→ms 换算前为 999999）
        public char SliderType;  // osu!standard 滑条类型：'L' 直线 'P' 完美圆 'B' 贝塞尔 'C' Catmull（'\0'=非滑条）
        public int Repeats = 1;  // osu!standard 滑条往返次数
        public List<(double X, double Y)> Curve = null;   // osu!standard 滑条控制点（含起点；终点=EndX/EndY）
        public List<(double X, double Y, double Z)> Arc3 = null;  // Arcaea arc 3D 中间控制点（X=轨, Y=时间比例, Z=天地间高度 0..1）
        public int Field;        // 多场同屏：归属场 id（默认 0=主舞台；部件容器内按索引隐含）
        public bool Judged;
        public bool Held;
        public bool Completed;
        public string Judgment;
        public double Dev;

        // AI 计划（陪玩 / 演示用）
        public bool AiPlanned;
        public bool AiMiss;
        public double AiDev;
        public double AiTime;
    }

    /// <summary>谱面事件（Phigros 判定线动效、Cytus 页码等）。</summary>
    public class ChartEvent
    {
        public double Time;             // ms
        public double End = double.NaN; // NaN = 瞬间事件
        public string Type = "moveY";   // moveX / moveY / rotate / alpha / speed
        public double Value;            // 起始值
        public double EndValue;         // 结束值
        public int Line = -1;           // Phigros 判定线索引（-1 = 作用于全部判定线）
        public string Ease;             // 缓动曲线：null/""=线性、"in"/"out"/"inout"/"back"/"bounce"（非线性动画）
        public int Group;               // RPE 绑定组（0=无组；同组同类型事件数值联动，T59 D2j）
        public bool Next;               // RPE next：true=结束后衔接下一事件 start（曲线连续）；false=保持自身 EndValue
        public double[] Bezier;         // RPE beziers 缓动控制点（4×2，x 单调不减）；null=非贝塞尔
        public int Field;               // 多场同屏：事件归属场 id（默认 0=主舞台）
    }

    /// <summary>谱面部件（单一谱面多模式）：一个谱面文件可含多个玩法层，各层独立音符/事件/键数。</summary>
    public class ChartPart
    {
        public string Name = "";                    // 部件名，如 "4K" / "Phigros"
        public GameMode Mode = GameMode.Mania;      // 该部件玩法
        public int KeyCount;                        // 该部件键数
        public List<Note> Notes = new List<Note>();
        public List<ChartEvent> Events = new List<ChartEvent>();
        public List<int> LineParents = new List<int>();   // Phigros 判定线父线（-1=无）
        public List<PhigrosLineMeta> LineMeta = new List<PhigrosLineMeta>();   // D2o：判定线元数据（名称/分组/Z/Cover）
    }

    /// <summary>同屏舞台（多场同屏：stage i ⇔ parts[i]；屏幕矩形归一化 0..1）。</summary>
    public class Stage
    {
        public int Id;                     // 场 id（0=主舞台；= parts 索引）
        public string Name = "";           // 显示名
        public GameMode Mode;              // 与部件一致（保存前同步）
        public int KeyCount;               // 与部件一致（保存前同步）
        public double X, Y, W = 1, H = 1;  // 归一化 0..1（相对 GamePanel 客户区）
        public Keys[] KeyMap;              // 可选：本场键位覆盖（null = 默认 GetKeys(kc)）
    }

    /// <summary>Phigros 判定线元数据（D2o 复刻，RPE line-management）：名称/分组/Z 渲染顺序/Cover 遮罩。</summary>
    public class PhigrosLineMeta
    {
        public string Name = "";        // 线名（RPE 左上 "Untitled Default"）
        public int Group = 0;           // 分组
        public int Z = 0;               // Z 轴渲染顺序（大者在上）
        public bool Cover = false;      // Cover 遮罩（跨线音符判定前隐藏）
    }

    public class Chart
    {
        public GameMode Mode = GameMode.Mania;
        public int KeyCount;
        public List<Note> Notes = new List<Note>();
        public List<ChartEvent> Events = new List<ChartEvent>();
        public List<ChartPart> Parts = new List<ChartPart>();   // 单一谱面多模式：空 = 单模式（根字段生效）
        public List<Stage> Stages = null;                          // 多场同屏：null/空 = 旧谱单主场（隐式 stage0=根字段）
        public List<int> LineParents = new List<int>();        // Phigros 判定线父线索引（-1=无父线；phimakor 父子线）
        public List<PhigrosLineMeta> LineMeta = new List<PhigrosLineMeta>();   // D2o：判定线元数据（根；parts 级见 ChartPart.LineMeta）
        public string Title = "";
        public string Artist = "";
        public string Version = "";
        public string ModeName = "";
        public double Bpm;
        public double Offset;            // ms（全局时间偏移）
        public double Od;                // osu! Overall Difficulty（判定窗口）
        public double Dr;                // osu! HP Drain Rate（段位 HP 扣血模型）
        public double Ar = 5;            // osu!standard Approach Rate（圆圈收缩速度，0~10）
        public double Cs = 4;            // osu! Circle Size（物件尺寸，0~10；catch 果径/接盘判定宽依赖）
        public double SliderTickRate = 1; // osu!standard 滑条 tick 率（[General] SliderTickRate；tick 间隔=1拍/tick率）
        public string AudioFile = "";
        public string SourcePath = "";
        public string DanName = "";      // 段位名，如 "1st Dan"
        public string DanSet = "";       // 段位集，如 "osu!mania 4K Dan"
        public bool IsDan => !string.IsNullOrEmpty(DanName);
        public double EndTime => Notes.Count == 0 ? 1000 : Notes[Notes.Count - 1].End + 1000;

        /// <summary>该谱面实际可玩的部件列表（无 Parts 时 = 根谱面一个部件）。</summary>
        public List<ChartPart> EffectiveParts()
        {
            if (Parts != null && Parts.Count > 0) return Parts;
            return new List<ChartPart> { new ChartPart { Name = ModeName, Mode = Mode, KeyCount = KeyCount, Notes = Notes, Events = Events } };
        }
    }

    public class GameResult
    {
        public string Title = "";
        public string Artist = "";
        public string ModeName = "";
        public int Score;
        public double Acc;
        public int MaxCombo;
        public Dictionary<string, int> Hits = new Dictionary<string, int>();
        public string Grade = "";
        public bool IsDan;
        public bool DanPass;
        public double HpEnd;
        public int TotalNotes;
        public bool NewBest;            // 是否刷新个人最佳
        public double PrevBestAcc;      // 历史最佳 ACC（用于结算对比）
    }
}
