# EnginePresets 10 预设数据块（t54 附录 · eng-design-vis 提供，供批1 直接引用）

> 用途：批1 实现 EnginePresets 时可直接拷贝的预设数据（值=本文件为准；与 engine-tool-design.md §B2 一致）。
> 约定：ProfileKey 必须能被 RulesetFactory.BuildProfile 解析（基名精确小写；OsuMania/OsuStandard 数字后缀=OD；Adofai 无数字默认 120）。
> 布局/背景为「推荐引擎侧规范值」；若与四族构造有出入以引擎构造为准（eng-coder-vis 已核对无出入）。

## 1. 预设数据（C# 字面量风格，10 条）

```csharp
// EnginePresets.cs 内数据（字段顺序 = EngineModePreset 属性顺序）
new EngineModePreset {
  Id = "mania4", Display = "Mania 4K", Field = RulesetFieldType.Lane, Keys = 4,
  Speed = 1.0, ScrollDir = 1, ProfileKey = "OsuMania8", KeyBase = 90,
  KeyHint = "D F J K", SampleBpm = 120, SampleNotes = 32,
  DocLine = "固定下落式：音符落在判定线时按对应键（点击/键盘）。",
  Layout = new PresetLayout { HitY = 0.82, Lines = 0, RingRadius = 0, PathLen = 0 },
  Background = new PresetBackground { Primary = "#2C6CFF", Secondary = "#0F1526", Track = "#1B2438" }
},
new EngineModePreset {
  Id = "mania6", Display = "Mania 6K", Field = RulesetFieldType.Lane, Keys = 6,
  Speed = 1.0, ScrollDir = 1, ProfileKey = "OsuMania8", KeyBase = 90,
  KeyHint = "S D F J K L", SampleBpm = 120, SampleNotes = 36, DocLine = "同上（6 轨）。",
  Layout = new PresetLayout { HitY = 0.82 }, Background = new PresetBackground { Primary = "#2C6CFF", Secondary = "#0F1526", Track = "#1B2438" }
},
new EngineModePreset {
  Id = "mania8", Display = "Mania 8K", Field = RulesetFieldType.Lane, Keys = 8,
  Speed = 1.0, ScrollDir = 1, ProfileKey = "OsuMania8", KeyBase = 90,
  KeyHint = "S D F SPACE J K L ; — 用 OemSemicolon 键", SampleBpm = 120, SampleNotes = 40,
  DocLine = "同上（8 轨，含空格）。",
  Layout = new PresetLayout { HitY = 0.82 }, Background = new PresetBackground { Primary = "#2C6CFF", Secondary = "#0F1526", Track = "#1B2438" }
},
new EngineModePreset {
  Id = "phigros", Display = "Phigros", Field = RulesetFieldType.Line, Keys = 2,
  Speed = 1.0, ScrollDir = 1, ProfileKey = "Phigros", KeyBase = 90,
  KeyHint = "D F J K（判定线上自由位置；鼠标点音符所在位置）", SampleBpm = 120, SampleNotes = 6,
  DocLine = "自由判定线：音符到线时点击音符所在位置（位置判定）。",
  Layout = new PresetLayout { HitY = 0.5, Lines = 2 }, Background = new PresetBackground { Primary = "#7F5AFF", Secondary = "#0B0E1A", Track = "#2A2F4A" }
},
new EngineModePreset {
  Id = "arcaea", Display = "Arcaea", Field = RulesetFieldType.Lane, Keys = 6,
  Speed = 1.0, ScrollDir = -1, ProfileKey = "Arcaea", KeyBase = 90,
  KeyHint = "S D F J K L（天 2 + 地 4）", SampleBpm = 120, SampleNotes = 24,
  DocLine = "天地双轨+Arc：地面轨道按键，天空轨道反向（近似族：Lane+ScrollDir=-1）。",
  Layout = new PresetLayout { HitY = 0.78 }, Background = new PresetBackground { Primary = "#00B8D4", Secondary = "#0A0E18", Track = "#1F2A44" }
},
new EngineModePreset {
  Id = "cytus", Display = "Cytus", Field = RulesetFieldType.Line, Keys = 1,
  Speed = 1.0, ScrollDir = 1, ProfileKey = "Cytus", KeyBase = 90,
  KeyHint = "D F J K（4 分位；鼠标点音符位置）", SampleBpm = 120, SampleNotes = 16,
  DocLine = "扫描线：线扫过音符时点击该音符（4 分位网格）。",
  Layout = new PresetLayout { HitY = 0.5, Lines = 1 }, Background = new PresetBackground { Primary = "#FF6A9E", Secondary = "#0F0B14", Track = "#2A2230" }
},
new EngineModePreset {
  Id = "osustd", Display = "osu!standard", Field = RulesetFieldType.Line, Keys = 1,
  Speed = 1.0, ScrollDir = 1, ProfileKey = "OsuStandard8", KeyBase = 90,
  KeyHint = "鼠标 + Z X 空格（滑条/转盘）", SampleBpm = 120, SampleNotes = 12,
  DocLine = "自由场：点击圆圈/滑条/转盘（鼠标）。",
  Layout = new PresetLayout { HitY = 0.5, Lines = 1 }, Background = new PresetBackground { Primary = "#FF7A45", Secondary = "#101018", Track = "#303040" }
},
new EngineModePreset {
  Id = "iidx", Display = "IIDX", Field = RulesetFieldType.Lane, Keys = 8,
  Speed = 1.0, ScrollDir = 1, ProfileKey = "Iidx", KeyBase = 90,
  KeyHint = "S=转盘（按住旋转）· D F SPACE J K L ;", SampleBpm = 120, SampleNotes = 32,
  DocLine = "下落式 7 键+转盘：转盘列记录旋转（近似族：Lane 8 键）。",
  Layout = new PresetLayout { HitY = 0.82 }, Background = new PresetBackground { Primary = "#00C8A0", Secondary = "#0B1214", Track = "#1C2A28" }
},
new EngineModePreset {
  Id = "maimai", Display = "maimai", Field = RulesetFieldType.Ring, Keys = 8,
  Speed = 1.0, ScrollDir = 1, ProfileKey = "Maimai", KeyBase = 0,
  KeyHint = "8 分区（鼠标位置判定；键盘 8 键=8 方位）", SampleBpm = 120, SampleNotes = 10,
  DocLine = "环形：音符到达外环时点击/触碰对应方位（含滑星）。",
  Layout = new PresetLayout { HitY = 0.5, RingRadius = 400 }, Background = new PresetBackground { Primary = "#FFC63F", Secondary = "#140F08", Track = "#3A2E14" }
},
new EngineModePreset {
  Id = "adofai", Display = "ADOFAI", Field = RulesetFieldType.Path, Keys = 2,
  Speed = 1.0, ScrollDir = 1, ProfileKey = "Adofai120", KeyBase = 90,
  KeyHint = "SPACE / D（每拍输入；错键掉轨重开）", SampleBpm = 120, SampleNotes = 24,
  DocLine = "双球路径：每拍按一次空格，双球沿砖块路径前进，错键/漏键掉轨重开。",
  Layout = new PresetLayout { HitY = 0.5, PathLen = 120.0 }, Background = new PresetBackground { Primary = "#FF5A5A", Secondary = "#120A0A", Track = "#331818" }
},
```

## 2. 实施提示

1. **PresetLayout/PresetBackground** 若无既有类型，按上表 6 字段内联或定义最小 struct（HitY/Lines/RingRadius/PathLen + Primary/Secondary/Track 十六进制 → RgbaColor.FromHex）。字段语义由 RulesetRenderer（批2）消费；批1 只需存值。
2. **BuildSampleChart 增强**：mania* 用 Lane 列 + hold（MinGap 500ms→HoldGap 900ms 触发）；phigros/cytus 用 X 分布；arcaea 用 Lane（部分 ScrollDir 逆向仅渲染层用）；maimai 用 RingNote（2 个滑星 StartAngle→EndAngle）；adofai 用 Path 双球（BeatMs 网格）；osustd 用圆点+1 滑条+1 转盘（Line 族 X/Y）。默认输出（无参调用）必须与现 BuildChart 逐字一致。
3. **RunHeadless 断言**：预期全 100%（Lane/Line/Ring 按 RunAuto 语义）；若 Path/Ring 不达 → 改 HitCount==TotalNotes 自检 + output 标注（引擎使用者 t55 已认可该口径）。
4. **颜色口径**：RgbaColor.FromHex 已存在于引擎（Particles.cs）——直接用。

## 3. 修订记录

- 2026-08（t54 附）：初版 10 预设数据块（与设计 §B2/eng-coder-vis 核对结论一致）。
