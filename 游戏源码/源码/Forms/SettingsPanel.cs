using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace ChartPlayer;

public class SettingsPanel : UserControl
{
	private class PresetComboItem
	{
		public string Key;

		public string Text;

		public bool IsHeader;

		public override string ToString()
		{
			return Text;
		}
	}

	private readonly GamePanel _game;

	// t53 批1：状态栏已删除——状态文本改经 Action<string> 回调（MainForm 注入 Logger，引擎壳 toast 可后续接入）。
	private readonly Action<string> _status;

	private readonly Panel _navHost = new Panel
	{
		Dock = DockStyle.Top,
		AutoScroll = true
	};

	private readonly FlowLayoutPanel _navBar = new FlowLayoutPanel
	{
		AutoSize = true,
		AutoSizeMode = AutoSizeMode.GrowAndShrink,
		WrapContents = false,
		Padding = new Padding(10, 14, 10, 12)
	};

	private readonly Panel _contentHost = new Panel
	{
		Dock = DockStyle.Fill,
		Padding = new Padding(0, 0, 0, 0)
	};

	private readonly Button[] _navBtns;

	private static readonly string[] _secNames = new string[9] { "游戏", "判定", "分数", "界面", "评级", "音效", "皮肤", "存档", "键位" };

	private readonly FlowLayoutPanel[] _pages;

	private NumericUpDown _speed;

	private NumericUpDown _offset;

	private NumericUpDown _vol;

	private CheckBox _auto;

	private ComboBox _judgeBase;

	private Label _keyCountText;

	private ComboBox _preset;

	private DataGridView _grid;

	private TextBox _presetName;

	private NumericUpDown _comboMax;

	private CheckBox _chkPanel;

	private CheckBox _chkOsuField;

	private CheckBox _chkMinimal;

	private CheckBox _chkFps;

	private CheckBox _chkFull;

	private CheckBox _chkBorderless;

	private CheckBox _chkShowArt;

	private CheckBox _chkAcc;

	private CheckBox _chkScore;

	private CheckBox _chkCombo;

	private CheckBox _chkHud;

	private CheckBox _chkJudgeText;

	private CheckBox _chkDevText;

	private CheckBox _chkBurst;

	private CheckBox _chkShake;

	private ComboBox _quality;

	private ComboBox _refreshRateSel;

	private CheckBox _chkSlant;

	private CheckBox _chkCamera3D;

	private CheckBox _chkHitFx;

	private CheckBox _chkResult;

	private NumericUpDown _cameraPitch;

	private NumericUpDown _cameraYaw;

	private NumericUpDown _cameraDepth;

	private ComboBox _uiThemeSel;

	private ComboBox _transitionSel;

	private CheckBox _soundChk;

	private NumericUpDown _hitVol;

	private ComboBox _hitsoundStyleSel;

	private CheckBox _hitsoundPerJudge;

	private Button _btnPreviewSound;

	private Button _btnPreviewMiss;

	private NumericUpDown _hitY;

	private NumericUpDown _playScale;

	private NumericUpDown _noteTh;

	private NumericUpDown _judgeFont;

	private NumericUpDown _bgDim;

	private NumericUpDown _hitLineTh;

	private NumericUpDown _hitLineGlow;

	private NumericUpDown _holdAlpha;

	private NumericUpDown _slant;

	private Button _btnBg;

	private Button _btnHl;

	private ComboBox _skinPreset;

	private ComboBox _judgePos;

	private ComboBox _devPos;

	private ComboBox _hitLineStyle;

	private ComboBox _holdStyle;

	private CheckBox _useLane;

	private TextBox _playerName;

	private PictureBox _avatar;

	private ComboBox _keyCount;

	private TextBox _keyText;

	private PlayerProfile _profile;

	public event Action RequestLayoutEdit;

	public event Action GoHome;

	public event Action ThemeChanged;

	private static string PresetGroup(string display)
	{
		if (display.StartsWith("osu!mania"))
		{
			return "osu!mania";
		}
		if (display.StartsWith("Quaver"))
		{
			return "Quaver";
		}
		if (display.StartsWith("SM/Etterna"))
		{
			return "SM/Etterna";
		}
		if (display.StartsWith("Malody"))
		{
			return "Malody";
		}
		return "标准";
	}

	private void FillPresetCombo(ComboBox cb)
	{
		cb.Items.Clear();
		foreach (string grp in new List<string> { "标准", "osu!mania", "Quaver", "SM/Etterna", "Malody" })
		{
			if (!JudgeSettings.Presets.Values.Any(((int n, double[] w, double miss, string[] names, string display) v) => PresetGroup(v.display) == grp))
			{
				continue;
			}
			cb.Items.Add(new PresetComboItem
			{
				Key = null,
				Text = "── " + grp + " ──",
				IsHeader = true
			});
			foreach (KeyValuePair<string, (int, double[], double, string[], string)> preset in JudgeSettings.Presets)
			{
				if (PresetGroup(preset.Value.Item5) == grp)
				{
					cb.Items.Add(new PresetComboItem
					{
						Key = preset.Key,
						Text = preset.Value.Item5,
						IsHeader = false
					});
				}
			}
		}
		foreach (PresetComboItem item in cb.Items)
		{
			if (!item.IsHeader && item.Key == JudgeSettings.PresetKey)
			{
				cb.SelectedItem = item;
				break;
			}
		}
		if (cb.SelectedIndex < 0 && cb.Items.Count > 0)
		{
			cb.SelectedIndex = 0;
		}
	}

	private static void DrawPresetItem(object sender, DrawItemEventArgs e)
	{
		e.DrawBackground();
		if (e.Index < 0 || sender == null)
		{
			return;
		}
		ComboBox comboBox = (ComboBox)sender;
		if (e.Index < comboBox.Items.Count && comboBox.Items[e.Index] is PresetComboItem presetComboItem)
		{
			using (SolidBrush brush = new SolidBrush(presetComboItem.IsHeader ? UiColors.Dim : (((e.State & DrawItemState.Selected) != 0) ? Color.White : UiColors.Fg)))
			{
				Font font = (presetComboItem.IsHeader ? new Font("Microsoft YaHei UI", 9f, FontStyle.Bold) : new Font("Microsoft YaHei UI", 10f));
				e.Graphics.DrawString(presetComboItem.Text, font, brush, e.Bounds.X + Ui.P(4), e.Bounds.Y + (presetComboItem.IsHeader ? Ui.P(3) : Ui.P(4)));
				font.Dispose();
			}
			e.DrawFocusRectangle();
		}
	}

	private string SelectedPresetKey()
	{
		if (_preset.SelectedItem is PresetComboItem { IsHeader: false } presetComboItem)
		{
			return presetComboItem.Key;
		}
		return null;
	}

	private void PreviewPreset()
	{
		string text = SelectedPresetKey();
		if (text == null || text == "custom" || _grid == null || !JudgeSettings.Presets.TryGetValue(text, out (int, double[], double, string[], string) value))
		{
			return;
		}
		List<JudgeLevel> list = new List<JudgeLevel>();
		int item = value.Item1;
		for (int i = 0; i < item; i++)
		{
			double window = ((i < value.Item2.Length) ? value.Item2[i] : value.Item2[Math.Min(i, value.Item2.Length - 1)]);
			double num = ((item <= 1) ? 0.0 : ((double)i / (double)(item - 1)));
			list.Add(new JudgeLevel
			{
				Name = ((value.Item4 != null && i < value.Item4.Length) ? value.Item4[i] : JudgeSettings.DefaultName(i, item)),
				Window = window,
				Score = (int)Math.Round(300.0 - 250.0 * num),
				Weight = Math.Round(1.0 - 0.95 * num, 3)
			});
		}
		_grid.Rows.Clear();
		foreach (JudgeLevel item2 in list)
		{
			_grid.Rows.Add(item2.Name, item2.Window, item2.Score, item2.Weight);
		}
		_grid.Rows.Add("MISS", value.Item3, 0, 0);
		_status("已预览预设：" + value.Item5);
	}

	public SettingsPanel(GamePanel game, Action<string> status)
	{
		_game = game;
		_status = status;
		_profile = PlayerData.LoadProfile();
		Dock = DockStyle.Fill;
		BackColor = UiColors.Bg;
		ForeColor = Color.White;
		_navBtns = new Button[_secNames.Length];
		_pages = new FlowLayoutPanel[_secNames.Length];
		for (int i = 0; i < _secNames.Length; i++)
		{
			int idx = i;
			Button button = new Button();
			button.Text = _secNames[i];
			button.Height = Ui.P(38);
			button.Margin = new Padding(0, 0, Ui.P(6), 0);
			button.MinimumSize = Ui.S(96, 38);
			button.AutoSize = true;
			button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			button.Padding = new Padding(Ui.P(10), 0, Ui.P(10), 0);
			button.Font = new Font("Microsoft YaHei UI", 12f);
			button.FlatStyle = FlatStyle.Flat;
			button.ForeColor = Color.White;
			button.BackColor = UiColors.TabBg;
			button.FlatAppearance.BorderColor = UiColors.TabBorder;
			Button button2 = button;
			Ui.Hover(button2);
			button2.Click += delegate
			{
				SelectTab(idx);
			};
			_navBtns[i] = button2;
			_navBar.Controls.Add(button2);
		}
		_navBar.Location = new Point(0, 0);
		_navBar.Padding = new Padding(Ui.P(10), Ui.P(14), Ui.P(10), Ui.P(12));
		_navHost.Controls.Add(_navBar);
		_navHost.Height = Ui.P(66);
		base.Controls.Add(_navHost);
		base.Controls.Add(_contentHost);
		for (int j = 0; j < _pages.Length; j++)
		{
			_pages[j] = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.TopDown,
				WrapContents = false,
				AutoScroll = true,
				BackColor = UiColors.Bg,
				Padding = Ui.Pad(10, 14, 14, 14)
			};
			_contentHost.Controls.Add(_pages[j]);
			Button button3 = Btn("\ud83c\udfe0 返回主菜单", 150);
			button3.Click += delegate
			{
				this.GoHome?.Invoke();
			};
			FlowLayoutPanel flowLayoutPanel = Row();
			flowLayoutPanel.Controls.Add(button3);
			_pages[j].Controls.Add(flowLayoutPanel);
		}
		BuildGame();
		BuildJudge();
		BuildScore();
		BuildInterface();
		BuildGrade();
		BuildSound();
		BuildSkin();
		BuildSave();
		BuildKeys();
		BuildMilestone();
		SelectTab(0);
	}

	public int GetTabCount()
	{
		return _pages.Length;
	}

	public void SelectTab(int index)
	{
		if (index >= 0 && index < _pages.Length)
		{
			for (int i = 0; i < _pages.Length; i++)
			{
				_pages[i].Visible = i == index;
				_navBtns[i].BackColor = ((i == index) ? UiColors.BlueBtn : UiColors.TabBg);
			}
		}
	}

	public void RefreshAll()
	{
		_speed.Value = (decimal)GameSettings.Speed;
		_offset.Value = (decimal)GameSettings.Offset;
		_vol.Value = GameSettings.Volume;
		_auto.Checked = GameSettings.Autoplay;
		_judgeBase.SelectedIndex = GameSettings.JudgeBase;
		FillPresetCombo(_preset);
		ReloadGrid();
		_btnBg.BackColor = _game.Skin.BgColor;
		_btnHl.BackColor = _game.Skin.HitLineColor;
		_hitLineTh.Value = _game.Skin.HitLineThickness;
		_useLane.Checked = _game.Skin.UseLaneColorForNote;
		_chkAcc.Checked = _game.Skin.ShowAcc;
		_chkScore.Checked = _game.Skin.ShowScore;
		_chkCombo.Checked = _game.Skin.ShowCombo;
		_playScale.Value = (decimal)_game.PlayScale;
		_noteTh.Value = (decimal)_game.NoteThickness;
		_chkPanel.Checked = _game.ShowRightPanel;
		_chkOsuField.Checked = GameSettings.OsuStdPlayfield;
		_chkFps.Checked = GameSettings.ShowFps;
		if (_quality != null)
		{
			_quality.SelectedIndex = Math.Max(0, Math.Min(3, GraphicsQuality.Preset));
		}
		if (_slant != null)
		{
			_slant.Value = (decimal)_game.Skin.Slant;
		}
		if (_hitY != null)
		{
			_hitY.Value = (decimal)(_game.Skin.Layout.TryGetValue("hitline", out var value) ? Math.Max(0.5, Math.Min(0.95, value.Y)) : 0.84);
		}
		if (_judgeFont != null)
		{
			_judgeFont.Value = _game.Skin.JudgeFont;
		}
		if (_judgePos != null)
		{
			_judgePos.SelectedIndex = Math.Max(0, Math.Min(3, _game.Skin.JudgePosMode));
		}
		if (_devPos != null)
		{
			_devPos.SelectedIndex = Math.Max(0, Math.Min(3, _game.Skin.DevPosMode));
		}
		if (_hitLineStyle != null)
		{
			_hitLineStyle.SelectedIndex = Math.Max(0, Math.Min(2, _game.Skin.HitLineStyle));
		}
		if (_hitLineGlow != null)
		{
			_hitLineGlow.Value = _game.Skin.HitLineGlow;
		}
		if (_holdAlpha != null)
		{
			_holdAlpha.Value = (decimal)_game.Skin.HoldAlpha;
		}
		if (_holdStyle != null)
		{
			_holdStyle.SelectedIndex = Math.Max(0, Math.Min(2, _game.Skin.HoldStyle));
		}
		if (_bgDim != null)
		{
			_bgDim.Value = (decimal)_game.Skin.BgDim;
		}
		if (_chkShowArt != null)
		{
			_chkShowArt.Checked = _game.Skin.ShowBackground;
		}
		if (_chkBurst != null)
		{
			_chkBurst.Checked = _game.Skin.BurstEffects;
		}
		if (_chkShake != null)
		{
			_chkShake.Checked = _game.Skin.ScreenShake;
		}
		if (_soundChk != null)
		{
			_soundChk.Checked = _game.Skin.SoundEffects;
		}
		if (_hitVol != null)
		{
			_hitVol.Value = GameSettings.HitsoundVolume;
		}
		if (_chkMinimal != null)
		{
			_chkMinimal.Checked = false;
		}
		if (_chkSlant != null)
		{
			_chkSlant.Checked = GameSettings.SlantEnabled;
		}
		if (_chkCamera3D != null)
		{
			_chkCamera3D.Checked = GameSettings.Camera3D;
		}
		if (_cameraPitch != null)
		{
			_cameraPitch.Value = (decimal)GameSettings.CameraPitch;
			_cameraPitch.Enabled = GameSettings.Camera3D;
		}
		if (_cameraYaw != null)
		{
			_cameraYaw.Value = (decimal)GameSettings.CameraYaw;
			_cameraYaw.Enabled = GameSettings.Camera3D;
		}
		if (_cameraDepth != null)
		{
			_cameraDepth.Value = (decimal)GameSettings.CameraDepth;
			_cameraDepth.Enabled = GameSettings.Camera3D;
		}
		if (_chkHitFx != null)
		{
			_chkHitFx.Checked = GameSettings.ShowHitFx;
		}
		if (_chkResult != null)
		{
			_chkResult.Checked = GameSettings.ResultScreenEnabled;
		}
		if (_uiThemeSel != null)
		{
			_uiThemeSel.SelectedIndex = Math.Max(0, Math.Min(2, GameSettings.UiTheme));
		}
		if (_transitionSel != null)
		{
			_transitionSel.SelectedIndex = Math.Max(0, Math.Min(3, GameSettings.TransitionStyle + 1));
		}
		if (_hitsoundStyleSel != null)
		{
			_hitsoundStyleSel.SelectedIndex = Math.Max(0, Math.Min(2, GameSettings.HitsoundStyle));
		}
		if (_hitsoundPerJudge != null)
		{
			_hitsoundPerJudge.Checked = GameSettings.HitsoundPerJudge;
		}
		string text = (_game.IsLoaded ? (_game.KeyCount + "K") : "4K");
		if (!_keyCount.Items.Contains(text))
		{
			_keyCount.Items.Add(text);
		}
		_keyCount.SelectedItem = text;
		LoadKeyText();
		_profile = PlayerData.LoadProfile();
		_playerName.Text = _profile.Name;
		RefreshAvatar();
	}

	private static FlowLayoutPanel Row(int pad = 8)
	{
		return new FlowLayoutPanel
		{
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = true,
			AutoSize = true,
			Padding = new Padding(0, Ui.P(pad), 0, Ui.P(pad))
		};
	}

	private static Label Lbl(string t)
	{
		return new Label
		{
			Text = t,
			AutoSize = true,
			MinimumSize = Ui.S(90, 28),
			Height = Ui.P(28),
			Padding = new Padding(0, Ui.P(6), 0, 0),
			Font = new Font("Microsoft YaHei UI", 11f),
			ForeColor = UiColors.BodyText
		};
	}

	private static NumericUpDown Num(int w, decimal min, decimal max, decimal val, int dec = 0, decimal inc = 1m)
	{
		return new NumericUpDown
		{
			Width = Ui.P(w),
			Minimum = min,
			Maximum = max,
			Value = val,
			DecimalPlaces = dec,
			Increment = inc,
			BackColor = UiColors.InputBg,
			ForeColor = UiColors.Fg,
			BorderStyle = BorderStyle.FixedSingle,
			Font = new Font("Microsoft YaHei UI", 11f)
		};
	}

	private static ComboBox Cmb(int w)
	{
		return new ComboBox
		{
			Width = Ui.P(w),
			DropDownStyle = ComboBoxStyle.DropDownList,
			BackColor = UiColors.InputBg,
			ForeColor = UiColors.Fg,
			FlatStyle = FlatStyle.Flat,
			Font = new Font("Microsoft YaHei UI", 11f)
		};
	}

	private static TextBox Txt(int w)
	{
		return new TextBox
		{
			Width = Ui.P(w),
			BackColor = UiColors.InputBg,
			ForeColor = UiColors.Fg,
			BorderStyle = BorderStyle.FixedSingle,
			Font = new Font("Microsoft YaHei UI", 11f)
		};
	}

	private static CheckBox Chk(string text, bool chk)
	{
		return new CheckBox
		{
			Text = text,
			Checked = chk,
			ForeColor = UiColors.BodyText,
			Font = new Font("Microsoft YaHei UI", 11f),
			Height = Ui.P(26)
		};
	}

	private static Button Btn(string text, int w)
	{
		return MakeBtn(text, w, UiColors.BtnBg, UiColors.Fg, UiColors.BorderLight);
	}

	private static Button BtnBlue(string text, int w)
	{
		return MakeBtn(text, w, UiColors.BlueBtn, Color.White, UiColors.BlueBtn);
	}

	private static Button MakeBtn(string text, int w, Color bg, Color fg, Color border)
	{
		Button button = new Button();
		button.Text = text;
		button.Height = Ui.P(36);
		button.MinimumSize = Ui.S(w, 36);
		button.AutoSize = true;
		button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
		button.Padding = new Padding(Ui.P(10), 0, Ui.P(10), 0);
		button.Font = new Font("Microsoft YaHei UI", 12f);
		button.FlatStyle = FlatStyle.Flat;
		button.BackColor = bg;
		button.ForeColor = fg;
		button.FlatAppearance.BorderColor = border;
		Ui.Hover(button);
		return button;
	}

	private void AddRow(int page, params Control[] cs)
	{
		FlowLayoutPanel flowLayoutPanel = Row();
		flowLayoutPanel.Controls.AddRange(cs);
		_pages[page].Controls.Add(flowLayoutPanel);
	}

	private void BuildGame()
	{
		_speed = Num(200, 0.4m, 4m, (decimal)GameSettings.Speed, 2, 0.05m);
		_speed.ValueChanged += delegate
		{
			GameSettings.Speed = (double)_speed.Value;
			_game.Invalidate();
		};
		_offset = Num(200, -300m, 300m, (decimal)GameSettings.Offset);
		_offset.ImeMode = ImeMode.Off;
		_offset.Enter += delegate
		{
			_offset.Select(0, _offset.Text.Length);
		};
		_offset.ValueChanged += delegate
		{
			GameSettings.Offset = (double)_offset.Value;
		};
		_vol = Num(160, 0m, 100m, GameSettings.Volume);
		_vol.ValueChanged += delegate
		{
			GameSettings.Volume = (int)_vol.Value;
			_game.ApplyVolume();
		};
		_auto = Chk("自动游玩", GameSettings.Autoplay);
		_auto.CheckedChanged += delegate
		{
			GameSettings.Autoplay = _auto.Checked;
		};
		_judgeBase = Cmb(160);
		_judgeBase.Items.AddRange(new object[3] { "音符下端", "音符中心", "音符上端" });
		_judgeBase.SelectedIndex = GameSettings.JudgeBase;
		_judgeBase.SelectedIndexChanged += delegate
		{
			GameSettings.JudgeBase = _judgeBase.SelectedIndex;
		};
		_keyCountText = new Label
		{
			Text = "--",
			ForeColor = UiColors.Green,
			AutoSize = false,
			Width = 120,
			Height = 28,
			Padding = new Padding(0, 6, 0, 0),
			Font = new Font("Microsoft YaHei UI", 11f)
		};
		Button button = Btn("\ud83c\udfaf 自动调整延迟", 180);
		button.Click += delegate
		{
			using CalibrationForm calibrationForm = new CalibrationForm();
			calibrationForm.ShowDialog(base.ParentForm);
			RefreshAll();
		};
		AddRow(0, Lbl("流速"), _speed);
		AddRow(0, Lbl("延迟(ms)"), _offset);
		AddRow(0, Lbl("音量"), _vol);
		AddRow(0, Lbl(""), button);
		AddRow(0, Lbl("键位(列数)"), _keyCountText);
		AddRow(0, Lbl("判定基准"), _judgeBase);
		AddRow(0, Lbl(""), _auto);
	}

	private void BuildJudge()
	{
		_preset = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = Ui.P(240),
			DropDownWidth = Ui.P(340),
			DrawMode = DrawMode.OwnerDrawFixed,
			ItemHeight = Ui.P(24),
			BackColor = UiColors.InputBg,
			ForeColor = UiColors.Fg,
			FlatStyle = FlatStyle.Flat,
			Font = new Font("Microsoft YaHei UI", 10f)
		};
		FillPresetCombo(_preset);
		_preset.DrawItem += DrawPresetItem;
		_preset.SelectedIndexChanged += delegate
		{
			PreviewPreset();
		};
		_presetName = Txt(160);
		_grid = new DataGridView
		{
			Height = Ui.P(230),
			Width = Ui.P(660),
			Font = new Font("Microsoft YaHei UI", 11f),
			ForeColor = UiColors.Fg,
			BackgroundColor = UiColors.InputBg,
			GridColor = UiColors.InputBorder,
			BorderStyle = BorderStyle.None,
			RowHeadersVisible = false,
			EnableHeadersVisualStyles = false
		};
		_grid.AllowUserToAddRows = false;
		_grid.DefaultCellStyle.BackColor = UiColors.InputBg;
		_grid.DefaultCellStyle.ForeColor = UiColors.Fg;
		_grid.DefaultCellStyle.SelectionBackColor = UiColors.BlueBtn;
		_grid.DefaultCellStyle.SelectionForeColor = Color.White;
		_grid.ColumnHeadersDefaultCellStyle.BackColor = UiColors.TabBg;
		_grid.ColumnHeadersDefaultCellStyle.ForeColor = UiColors.HeadTitle;
		_grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiColors.TabBg;
		_grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiColors.HeadTitle;
		_grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
		_grid.ColumnHeadersHeight = Ui.P(32);
		_grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
		_grid.RowTemplate.Height = Ui.P(30);
		_grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(14, 22, 38);
		_grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = UiColors.BlueBtn;
		_grid.Columns.Add("Name", "名称");
		_grid.Columns.Add("Win", "窗口(ms)");
		_grid.Columns.Add("Score", "得分");
		_grid.Columns.Add("Weight", "权重");
		ReloadGrid();
		Button button = Btn("应用预设", 100);
		button.Click += delegate
		{
			string text2 = SelectedPresetKey();
			if (text2 != null)
			{
				if (text2 == "custom")
				{
					ReloadGrid();
				}
				else
				{
					JudgeSettings.ApplyPreset(text2);
					ReloadGrid();
				}
			}
		};
		Button button2 = BtnBlue("\ud83d\udcbe 保存判定预设", 160);
		button2.Click += delegate
		{
			string text = _presetName.Text.Trim();
			if (text.Length == 0)
			{
				text = "custom";
			}
			JudgeSettings.PresetKey = "custom";
			ApplyJudgeGrid();
			using SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Filter = "JSON|*.json",
				FileName = "judge-" + text + ".json"
			};
			if (saveFileDialog.ShowDialog(base.ParentForm) == DialogResult.OK)
			{
				try
				{
					JudgePresetIO.Export(saveFileDialog.FileName, text);
					_status("已保存判定预设");
					return;
				}
				catch (Exception ex2)
				{
					MessageBox.Show("保存失败：" + ex2.Message);
					return;
				}
			}
		};
		Button button3 = Btn("\ud83d\udce5 导入预设", 130);
		button3.Click += delegate
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "JSON|*.json"
			};
			if (openFileDialog.ShowDialog(base.ParentForm) == DialogResult.OK)
			{
				try
				{
					JudgePresetDto judgePresetDto = JudgePresetIO.Import(openFileDialog.FileName);
					if (judgePresetDto == null || judgePresetDto.Levels == null || judgePresetDto.Levels.Count < 3)
					{
						MessageBox.Show("预设文件无效");
					}
					else
					{
						JudgeSettings.ApplyCustom(judgePresetDto.Levels, judgePresetDto.MissWindow, judgePresetDto.ComboBonusMax);
						RefreshAll();
						_status("已导入判定预设");
					}
					return;
				}
				catch (Exception ex)
				{
					MessageBox.Show("导入失败：" + ex.Message);
					return;
				}
			}
		};
		Label label = new Label
		{
			Text = "表格最后一行 MISS 为判定失败的窗口；选择预设即在表格中预览数值",
			ForeColor = UiColors.Muted,
			AutoSize = true,
			Font = new Font("Microsoft YaHei UI", 9f)
		};
		AddRow(1, _grid);
		AddRow(1, Lbl(""), label);
		AddRow(1, Lbl("预设"), _preset, button);
		AddRow(1, Lbl("预设名称"), _presetName);
		AddRow(1, Lbl(""), button2, button3);
	}

	private void BuildScore()
	{
		_comboMax = Num(160, 10m, 200m, JudgeSettings.ComboBonusMax);
		_comboMax.ValueChanged += delegate
		{
			JudgeSettings.ComboBonusMax = (int)_comboMax.Value;
		};
		AddRow(2, Lbl("连击加成上限"), _comboMax);
	}

	private void BuildInterface()
	{
		_chkPanel = Chk("显示右侧数据面板", _game.ShowRightPanel);
		_chkMinimal = Chk("极简模式", chk: false);
		_chkFps = Chk("显示 FPS", GameSettings.ShowFps);
		_chkFull = Chk("全屏（F11）", chk: false);
		_chkBorderless = Chk("无边框窗口", chk: false);
		_chkShowArt = Chk("背景显示曲绘", _game.Skin.ShowBackground);
		_chkAcc = Chk("ACC", _game.Skin.ShowAcc);
		_chkScore = Chk("得分", _game.Skin.ShowScore);
		_chkCombo = Chk("连击", _game.Skin.ShowCombo);
		_chkHud = Chk("命中统计", chk: true);
		_chkJudgeText = Chk("判定文字", chk: true);
		_chkDevText = Chk("偏差数字", chk: true);
		_chkBurst = Chk("打击特效", _game.Skin.BurstEffects);
		_chkShake = Chk("屏幕震动", _game.Skin.ScreenShake);
		_chkPanel.CheckedChanged += delegate
		{
			_game.ShowRightPanel = _chkPanel.Checked;
			_game.Invalidate();
		};
		_chkOsuField = Chk("osu!std 4:3 框定游玩区", GameSettings.OsuStdPlayfield);
		_chkOsuField.CheckedChanged += delegate
		{
			GameSettings.OsuStdPlayfield = _chkOsuField.Checked;
			_game.Invalidate();
		};
		_chkFps.CheckedChanged += delegate
		{
			GameSettings.ShowFps = _chkFps.Checked;
			_game.Invalidate();
		};
		_chkAcc.CheckedChanged += delegate
		{
			_game.Skin.ShowAcc = _chkAcc.Checked;
			_game.Invalidate();
		};
		_chkScore.CheckedChanged += delegate
		{
			_game.Skin.ShowScore = _chkScore.Checked;
			_game.Invalidate();
		};
		_chkCombo.CheckedChanged += delegate
		{
			_game.Skin.ShowCombo = _chkCombo.Checked;
			_game.Invalidate();
		};
		_chkShowArt.CheckedChanged += delegate
		{
			_game.Skin.ShowBackground = _chkShowArt.Checked;
			_game.Invalidate();
		};
		_chkBurst.CheckedChanged += delegate
		{
			_game.Skin.BurstEffects = _chkBurst.Checked;
			_game.Invalidate();
		};
		_chkShake.CheckedChanged += delegate
		{
			_game.Skin.ScreenShake = _chkShake.Checked;
			_game.Invalidate();
		};
		_chkMinimal.CheckedChanged += delegate
		{
			bool @checked = _chkMinimal.Checked;
			_game.ShowRightPanel = !@checked && _chkPanel.Checked;
			string[] array2 = new string[9] { "title", "score", "acc", "bpm", "kps", "notes", "combo", "judge", "dev" };
			foreach (string key2 in array2)
			{
				if (_game.Skin.Layout.TryGetValue(key2, out var value2))
				{
					value2.Show = !@checked;
				}
			}
			_game.Invalidate();
		};
		_chkHud.CheckedChanged += delegate
		{
			string[] array = new string[9] { "title", "score", "acc", "bpm", "kps", "notes", "combo", "judge", "dev" };
			foreach (string key in array)
			{
				if (_game.Skin.Layout.TryGetValue(key, out var value))
				{
					value.Show = _chkHud.Checked;
				}
			}
			_game.Invalidate();
		};
		_chkJudgeText.CheckedChanged += delegate
		{
			SetHudShow("judge", _chkJudgeText.Checked);
		};
		_chkDevText.CheckedChanged += delegate
		{
			SetHudShow("dev", _chkDevText.Checked);
		};
		_chkFull.CheckedChanged += delegate
		{
			Form parentForm2 = base.ParentForm;
			if (parentForm2 != null)
			{
				parentForm2.FormBorderStyle = ((!_chkFull.Checked) ? FormBorderStyle.Sizable : FormBorderStyle.None);
				parentForm2.WindowState = FormWindowState.Normal;
				parentForm2.WindowState = FormWindowState.Maximized;
			}
		};
		_chkBorderless.CheckedChanged += delegate
		{
			Form parentForm = base.ParentForm;
			if (parentForm != null)
			{
				if (_chkBorderless.Checked)
				{
					parentForm.FormBorderStyle = FormBorderStyle.None;
				}
				else if (!_chkFull.Checked)
				{
					parentForm.FormBorderStyle = FormBorderStyle.Sizable;
				}
			}
		};
		_quality = Cmb(150);
		_quality.Items.AddRange(new object[4] { "低（老设备 ≥60帧）", "中（均衡）", "高（全特效）", "自动（动态调档）" });
		_quality.SelectedIndex = GraphicsQuality.Preset;
		_quality.SelectedIndexChanged += delegate
		{
			GraphicsQuality.ApplyPreset(_quality.SelectedIndex);
			_game.Invalidate();
			string text = GraphicsQuality.Name(GraphicsQuality.Effective);
			string text2 = GraphicsQuality.Preset switch
			{
				0 => "低", 
				1 => "中", 
				2 => "高", 
				_ => "自动", 
			};
			_status("画质：" + text + "（" + text2 + "）");
		};
		Label label = new Label
		{
			Text = "低画质：关闭曲绘/特效/图表，老设备也流畅；自动：掉帧自动降档，流畅自动回升",
			ForeColor = UiColors.Muted,
			AutoSize = true,
			Font = new Font("Microsoft YaHei UI", 9f)
		};
		AddRow(3, Lbl("画质"), _quality, label);
		_refreshRateSel = Cmb(160);
		ComboBox.ObjectCollection items = _refreshRateSel.Items;
		object[] rateNames = FpsGovernor.RateNames;
		items.AddRange(rateNames);
		_refreshRateSel.SelectedIndex = Math.Max(0, Array.IndexOf(FpsGovernor.Rates, GameSettings.RefreshRate));
		_refreshRateSel.SelectedIndexChanged += delegate
		{
			FpsGovernor.Apply(FpsGovernor.Rates[Math.Max(0, _refreshRateSel.SelectedIndex)]);
			_game.Invalidate();
			_status(FpsGovernor.Describe());
		};
		Label label2 = new Label
		{
			Text = "刷新率挡位：高刷新率自动降低画质以保证帧率；无限制=目标 1000+ FPS 跑满（自适应兜底）",
			ForeColor = UiColors.Muted,
			AutoSize = true,
			Font = new Font("Microsoft YaHei UI", 9f)
		};
		AddRow(3, Lbl("刷新率"), _refreshRateSel, label2);
		AddRow(3, Lbl(""), _chkPanel, _chkOsuField);
		AddRow(3, Lbl(""), _chkMinimal, _chkFps, _chkFull, _chkBorderless, _chkShowArt);
		AddRow(3, Lbl("显示"), _chkAcc, _chkScore, _chkCombo, _chkHud);
		AddRow(3, Lbl(""), _chkJudgeText, _chkDevText, _chkBurst, _chkShake);
	}

	private void BuildMilestone()
	{
		_chkSlant = Chk("斜轨（斜向轨道）", GameSettings.SlantEnabled);
		_chkSlant.CheckedChanged += delegate
		{
			GameSettings.SlantEnabled = _chkSlant.Checked;
			_game.Invalidate();
		};
		_chkCamera3D = Chk("3D 渲染", GameSettings.Camera3D);
		_cameraPitch = Num(110, 0m, 60m, (decimal)GameSettings.CameraPitch);
		_cameraYaw = Num(110, -30m, 30m, (decimal)GameSettings.CameraYaw);
		_cameraDepth = Num(110, 0.4m, 2.5m, (decimal)GameSettings.CameraDepth, 1, 0.1m);
		_cameraPitch.ValueChanged += delegate
		{
			GameSettings.CameraPitch = (double)_cameraPitch.Value;
			_game.Invalidate();
		};
		_cameraYaw.ValueChanged += delegate
		{
			GameSettings.CameraYaw = (double)_cameraYaw.Value;
			_game.Invalidate();
		};
		_cameraDepth.ValueChanged += delegate
		{
			GameSettings.CameraDepth = (double)_cameraDepth.Value;
			_game.Invalidate();
		};
		_chkCamera3D.CheckedChanged += delegate
		{
			GameSettings.Camera3D = _chkCamera3D.Checked;
			NumericUpDown cameraPitch2 = _cameraPitch;
			NumericUpDown cameraYaw2 = _cameraYaw;
			bool flag3 = (_cameraDepth.Enabled = _chkCamera3D.Checked);
			bool enabled2 = (cameraYaw2.Enabled = flag3);
			cameraPitch2.Enabled = enabled2;
			_game.Invalidate();
		};
		_chkHitFx = Chk("打击特效", GameSettings.ShowHitFx);
		_chkHitFx.CheckedChanged += delegate
		{
			GameSettings.ShowHitFx = _chkHitFx.Checked;
			_game.Invalidate();
		};
		_chkResult = Chk("结算画面", GameSettings.ResultScreenEnabled);
		_chkResult.CheckedChanged += delegate
		{
			GameSettings.ResultScreenEnabled = _chkResult.Checked;
		};
		_uiThemeSel = Cmb(140);
		_uiThemeSel.Items.AddRange(new object[3] { "深空蓝", "极夜紫", "晨光青" });
		_uiThemeSel.SelectedIndex = Math.Max(0, Math.Min(2, GameSettings.UiTheme));
		_uiThemeSel.SelectedIndexChanged += delegate
		{
			GameSettings.UiTheme = _uiThemeSel.SelectedIndex;
			UiColors.ApplyTheme(GameSettings.UiTheme);
			this.ThemeChanged?.Invoke();
		};
		_transitionSel = Cmb(140);
		_transitionSel.Items.AddRange(new object[4] { "随机", "光束", "圆环", "推拉" });
		_transitionSel.SelectedIndex = Math.Max(0, Math.Min(3, GameSettings.TransitionStyle + 1));
		_transitionSel.SelectedIndexChanged += delegate
		{
			GameSettings.TransitionStyle = _transitionSel.SelectedIndex - 1;
		};
		Label label = new Label
		{
			Text = "相机俯仰 0~60° · 偏航 -30~30° · 深度 0.4~2.5",
			ForeColor = UiColors.Muted,
			AutoSize = true,
			Font = new Font("Microsoft YaHei UI", 9f)
		};
		AddRow(3, Lbl(""), _chkSlant);
		AddRow(3, Lbl(""), _chkCamera3D);
		AddRow(3, Lbl("俯仰"), _cameraPitch, Lbl("偏航"), _cameraYaw, Lbl("深度"), _cameraDepth);
		AddRow(3, Lbl(""), label);
		AddRow(3, Lbl(""), _chkHitFx, _chkResult);
		AddRow(3, Lbl("UI 主题"), _uiThemeSel);
		AddRow(3, Lbl("转场风格"), _transitionSel);
		NumericUpDown cameraPitch = _cameraPitch;
		NumericUpDown cameraYaw = _cameraYaw;
		bool flag = (_cameraDepth.Enabled = GameSettings.Camera3D);
		bool enabled = (cameraYaw.Enabled = flag);
		cameraPitch.Enabled = enabled;
		_hitsoundStyleSel = Cmb(140);
		_hitsoundStyleSel.Items.AddRange(new object[3] { "经典", "电子", "木鱼" });
		_hitsoundStyleSel.SelectedIndex = Math.Max(0, Math.Min(2, GameSettings.HitsoundStyle));
		_hitsoundStyleSel.SelectedIndexChanged += delegate
		{
			GameSettings.HitsoundStyle = _hitsoundStyleSel.SelectedIndex;
			SoundFx.Style = GameSettings.HitsoundStyle;
		};
		_hitsoundPerJudge = Chk("按判定区分音效", GameSettings.HitsoundPerJudge);
		_hitsoundPerJudge.CheckedChanged += delegate
		{
			GameSettings.HitsoundPerJudge = _hitsoundPerJudge.Checked;
		};
		_btnPreviewSound = Btn("\ud83d\udd0a 试听判定音", 130);
		_btnPreviewSound.Click += delegate
		{
			SoundFx.Preview();
		};
		_btnPreviewMiss = Btn("\ud83d\udd0a 试听 MISS", 120);
		_btnPreviewMiss.Click += delegate
		{
			SoundFx.Preview(-1);
		};
		AddRow(5, Lbl("打击音效风格"), _hitsoundStyleSel);
		AddRow(5, Lbl(""), _hitsoundPerJudge);
		AddRow(5, Lbl(""), _btnPreviewSound, _btnPreviewMiss);
	}

	private void SetHudShow(string key, bool show)
	{
		if (_game.Skin.Layout.TryGetValue(key, out var value))
		{
			value.Show = show;
		}
		_game.Invalidate();
	}

	private void BuildGrade()
	{
		Label label = new Label
		{
			Text = "评级按 ACC 固定档位：\nSSS ≥100 · SS ≥98 · S ≥95 · A ≥90\nB ≥80 · C ≥70 · D <70",
			AutoSize = true,
			ForeColor = UiColors.BodyText,
			Font = new Font("Microsoft YaHei UI", 11f),
			Padding = Ui.Pad(4, 4, 4, 4)
		};
		AddRow(4, label);
	}

	private void BuildSound()
	{
		_soundChk = Chk("启用打击音效", _game.Skin.SoundEffects);
		_hitVol = Num(160, 0m, 100m, SoundFx.Volume);
		_soundChk.CheckedChanged += delegate
		{
			_game.Skin.SoundEffects = _soundChk.Checked;
			SoundFx.Enabled = _soundChk.Checked;
		};
		_hitVol.ValueChanged += delegate
		{
			SoundFx.Volume = (int)_hitVol.Value;
			GameSettings.HitsoundVolume = (int)_hitVol.Value;
			SoundFx.Enabled = _hitVol.Value > 0m;
		};
		AddRow(5, Lbl(""), _soundChk);
		AddRow(5, Lbl("音效音量"), _hitVol);
	}

	private void BuildSkin()
	{
		_hitY = Num(140, 0.5m, 0.95m, 0.84m, 2, 0.01m);
		_playScale = Num(140, 0.25m, 1.6m, (decimal)_game.PlayScale, 2, 0.01m);
		_noteTh = Num(140, 8m, 120m, (decimal)_game.NoteThickness);
		_judgeFont = Num(140, 12m, 44m, _game.Skin.JudgeFont);
		_slant = Num(140, 0m, 1m, (decimal)_game.Skin.Slant, 2, 0.05m);
		_judgePos = Cmb(150);
		_judgePos.Items.AddRange(new object[4] { "轨道上方", "判定线中央", "顶部中央", "隐藏" });
		_judgePos.SelectedIndex = 0;
		_devPos = Cmb(150);
		_devPos.Items.AddRange(new object[4] { "右侧面板", "判定线中央", "顶部中央", "隐藏" });
		_devPos.SelectedIndex = 0;
		_btnBg = new Button
		{
			Width = Ui.P(120),
			Height = Ui.P(30)
		};
		_btnBg.Click += delegate
		{
			using ColorDialog colorDialog2 = new ColorDialog
			{
				Color = _game.Skin.BgColor
			};
			if (colorDialog2.ShowDialog(base.ParentForm) == DialogResult.OK)
			{
				_game.Skin.BgColor = colorDialog2.Color;
				_btnBg.BackColor = colorDialog2.Color;
				_game.Invalidate();
			}
		};
		_bgDim = Num(140, 0m, 1m, (decimal)_game.Skin.BgDim, 2, 0.05m);
		_skinPreset = Cmb(150);
		_skinPreset.Items.AddRange(new object[5] { "默认", "霓虹", "糖果", "黑白", "蓝白块（4K）" });
		_skinPreset.SelectedIndex = 0;
		_useLane = Chk("音符=轨道色", _game.Skin.UseLaneColorForNote);
		_btnHl = new Button
		{
			Width = Ui.P(120),
			Height = Ui.P(30)
		};
		_btnHl.Click += delegate
		{
			using ColorDialog colorDialog = new ColorDialog
			{
				Color = _game.Skin.HitLineColor
			};
			if (colorDialog.ShowDialog(base.ParentForm) == DialogResult.OK)
			{
				_game.Skin.HitLineColor = colorDialog.Color;
				_btnHl.BackColor = colorDialog.Color;
				_game.Invalidate();
			}
		};
		_hitLineTh = Num(140, 1m, 10m, _game.Skin.HitLineThickness);
		_hitLineStyle = Cmb(150);
		_hitLineStyle.Items.AddRange(new object[3] { "实线", "虚线", "点线" });
		_hitLineStyle.SelectedIndex = 0;
		_hitLineGlow = Num(140, 0m, 30m, _game.Skin.HitLineGlow);
		_holdAlpha = Num(140, 0.1m, 1m, (decimal)_game.Skin.HoldAlpha, 2, 0.05m);
		_holdStyle = Cmb(150);
		_holdStyle.Items.AddRange(new object[3] { "实心", "辉光", "描边" });
		_holdStyle.SelectedIndex = 0;
		_hitY.ValueChanged += delegate
		{
			if (_game.Skin.Layout.TryGetValue("hitline", out var value))
			{
				value.Y = Math.Max(0.05, Math.Min(0.95, (double)_hitY.Value));
				_game.Invalidate();
			}
		};
		_playScale.ValueChanged += delegate
		{
			_game.PlayScale = (double)_playScale.Value;
			_game.Invalidate();
		};
		_noteTh.ValueChanged += delegate
		{
			_game.NoteThickness = (double)_noteTh.Value;
			_game.Invalidate();
		};
		_judgeFont.ValueChanged += delegate
		{
			_game.Skin.JudgeFont = (int)_judgeFont.Value;
			_game.Invalidate();
		};
		_slant.ValueChanged += delegate
		{
			_game.Skin.Slant = (double)_slant.Value;
			_game.Invalidate();
		};
		_judgePos.SelectedIndexChanged += delegate
		{
			_game.Skin.JudgePosMode = _judgePos.SelectedIndex;
			_game.Invalidate();
		};
		_devPos.SelectedIndexChanged += delegate
		{
			_game.Skin.DevPosMode = _devPos.SelectedIndex;
			_game.Invalidate();
		};
		_bgDim.ValueChanged += delegate
		{
			_game.Skin.BgDim = (double)_bgDim.Value;
			_game.Invalidate();
		};
		_hitLineTh.ValueChanged += delegate
		{
			_game.Skin.HitLineThickness = (int)_hitLineTh.Value;
			_game.Invalidate();
		};
		_hitLineStyle.SelectedIndexChanged += delegate
		{
			_game.Skin.HitLineStyle = _hitLineStyle.SelectedIndex;
			_game.Invalidate();
		};
		_hitLineGlow.ValueChanged += delegate
		{
			_game.Skin.HitLineGlow = (int)_hitLineGlow.Value;
			_game.Invalidate();
		};
		_holdAlpha.ValueChanged += delegate
		{
			_game.Skin.HoldAlpha = (double)_holdAlpha.Value;
			_game.Invalidate();
		};
		_holdStyle.SelectedIndexChanged += delegate
		{
			_game.Skin.HoldStyle = _holdStyle.SelectedIndex;
			_game.Invalidate();
		};
		_useLane.CheckedChanged += delegate
		{
			_game.Skin.UseLaneColorForNote = _useLane.Checked;
			_game.Invalidate();
		};
		_skinPreset.SelectedIndexChanged += delegate
		{
			switch (_skinPreset.SelectedIndex)
			{
			case 1:
				_game.Skin.LaneColors = new List<Color>
				{
					Color.FromArgb(0, 255, 170),
					Color.FromArgb(255, 0, 170),
					Color.FromArgb(255, 240, 0),
					Color.FromArgb(0, 200, 255),
					Color.FromArgb(170, 0, 255),
					Color.FromArgb(255, 90, 90),
					Color.FromArgb(90, 255, 120),
					Color.FromArgb(255, 140, 0)
				};
				_game.Skin.BgColor = Color.FromArgb(8, 6, 22);
				break;
			case 2:
				_game.Skin.LaneColors = new List<Color>
				{
					Color.FromArgb(255, 150, 200),
					Color.FromArgb(255, 200, 120),
					Color.FromArgb(255, 240, 150),
					Color.FromArgb(150, 255, 200),
					Color.FromArgb(150, 210, 255),
					Color.FromArgb(220, 180, 255),
					Color.FromArgb(255, 170, 220),
					Color.FromArgb(180, 255, 220)
				};
				_game.Skin.BgColor = Color.FromArgb(26, 14, 26);
				break;
			case 3:
				_game.Skin.LaneColors = new List<Color>
				{
					Color.White,
					Color.FromArgb(200, 200, 200),
					Color.FromArgb(160, 160, 160),
					Color.FromArgb(120, 120, 120),
					Color.White,
					Color.FromArgb(200, 200, 200),
					Color.FromArgb(160, 160, 160),
					Color.FromArgb(120, 120, 120)
				};
				_game.Skin.BgColor = Color.FromArgb(6, 6, 8);
				break;
			case 4:
				_game.Skin.LaneColors = new List<Color>
				{
					Color.White,
					Color.FromArgb(120, 190, 255),
					Color.White,
					Color.FromArgb(120, 190, 255)
				};
				_game.Skin.BgColor = Color.FromArgb(10, 14, 26);
				break;
			}
			_game.Skin.NoteColors = null;
			_btnBg.BackColor = _game.Skin.BgColor;
			_game.Invalidate();
		};
		Button button = Btn("\ud83d\udcd0 在皮肤中编辑布局", 190);
		button.Click += delegate
		{
			ApplySkin();
			this.RequestLayoutEdit?.Invoke();
		};
		Button button2 = Btn("\ud83c\udfb2 随机配色", 130);
		button2.Click += delegate
		{
			_game.Skin.RandomizeLanes();
			_game.Invalidate();
		};
		Button button3 = BtnBlue("\ud83d\udcbe 应用并保存皮肤", 180);
		button3.Click += delegate
		{
			ApplySkin();
			_game.Skin.Save();
			_status("皮肤已保存");
		};
		Button button4 = Btn("\ud83d\udce4 导出 JSON", 120);
		Button button5 = Btn("\ud83d\udce5 导入 JSON", 120);
		Button button6 = Btn("恢复默认", 110);
		button4.Click += delegate
		{
			using SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Filter = "JSON|*.json",
				FileName = "my-skin.json"
			};
			if (saveFileDialog.ShowDialog(base.ParentForm) == DialogResult.OK)
			{
				ApplySkin();
				_game.Skin.ExportToFile(saveFileDialog.FileName);
				_status("已导出皮肤");
			}
		};
		button5.Click += delegate
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "JSON|*.json"
			};
			if (openFileDialog.ShowDialog(base.ParentForm) == DialogResult.OK)
			{
				try
				{
					_game.Skin = SkinSettings.ImportFromFile(openFileDialog.FileName);
					RefreshAll();
					_game.Invalidate();
					_status("已导入皮肤");
					return;
				}
				catch (Exception ex)
				{
					MessageBox.Show("导入失败：" + ex.Message);
					return;
				}
			}
		};
		button6.Click += delegate
		{
			_game.Skin = new SkinSettings();
			RefreshAll();
			_game.Invalidate();
			_status("皮肤已恢复默认");
		};
		AddRow(6, Lbl(""), button);
		AddRow(6, Lbl("判定线位置"), _hitY);
		AddRow(6, Lbl("斜轨强度"), _slant);
		AddRow(6, Lbl("界面缩放"), _playScale);
		AddRow(6, Lbl("音符厚度"), _noteTh);
		AddRow(6, Lbl("判定字大小"), _judgeFont);
		AddRow(6, Lbl("判定位置"), _judgePos);
		AddRow(6, Lbl("延迟位置"), _devPos);
		AddRow(6, Lbl("背景色"), _btnBg);
		AddRow(6, Lbl("背景亮度"), _bgDim);
		AddRow(6, Lbl("配色方案"), _skinPreset);
		AddRow(6, Lbl(""), button2, _useLane);
		AddRow(6, Lbl("判定线颜色"), _btnHl);
		AddRow(6, Lbl("判定线粗细"), _hitLineTh);
		AddRow(6, Lbl("判定线样式"), _hitLineStyle);
		AddRow(6, Lbl("判定线辉光"), _hitLineGlow);
		AddRow(6, Lbl("长条透明度"), _holdAlpha);
		AddRow(6, Lbl("长条样式"), _holdStyle);
		AddRow(6, Lbl(""), button3, button4, button5, button6);
	}

	private void ApplySkin()
	{
		_game.PlayScale = (double)_playScale.Value;
		_game.NoteThickness = (double)_noteTh.Value;
		_game.Skin.HitLineThickness = (int)_hitLineTh.Value;
		_game.Skin.UseLaneColorForNote = _useLane.Checked;
		_game.Skin.ShowAcc = _chkAcc.Checked;
		_game.Skin.ShowScore = _chkScore.Checked;
		_game.Skin.ShowCombo = _chkCombo.Checked;
		_game.Skin.Slant = (double)_slant.Value;
		_game.Skin.JudgeFont = (int)_judgeFont.Value;
		_game.Skin.JudgePosMode = _judgePos.SelectedIndex;
		_game.Skin.DevPosMode = _devPos.SelectedIndex;
		_game.Skin.BgDim = (double)_bgDim.Value;
		_game.Skin.HitLineStyle = _hitLineStyle.SelectedIndex;
		_game.Skin.HitLineGlow = (int)_hitLineGlow.Value;
		_game.Skin.HoldAlpha = (double)_holdAlpha.Value;
		_game.Skin.HoldStyle = _holdStyle.SelectedIndex;
		_game.Skin.ShowBackground = _chkShowArt.Checked;
		_game.Skin.BurstEffects = _chkBurst.Checked;
		_game.Skin.ScreenShake = _chkShake.Checked;
		_game.Skin.SoundEffects = _soundChk.Checked;
		SoundFx.Enabled = _soundChk.Checked;
		if (_game.Skin.Layout.TryGetValue("hitline", out var value))
		{
			value.Y = Math.Max(0.05, Math.Min(0.95, (double)_hitY.Value));
		}
		_game.Invalidate();
	}

	private void BuildSave()
	{
		_playerName = Txt(200);
		_avatar = new PictureBox
		{
			Size = new Size(54, 54),
			BackColor = UiColors.InputBg,
			SizeMode = PictureBoxSizeMode.Zoom,
			BorderStyle = BorderStyle.FixedSingle
		};
		Button button = Btn("上传头像", 100);
		Button button2 = Btn("清除", 80);
		Button button3 = BtnBlue("\ud83d\udcbe 保存玩家信息", 160);
		Button button4 = Btn("\ud83d\udcbe 导出玩家数据", 160);
		Button button5 = Btn("\ud83d\udcc2 导入玩家数据", 160);
		button.Click += delegate
		{
			using OpenFileDialog openFileDialog2 = new OpenFileDialog
			{
				Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.gif"
			};
			if (openFileDialog2.ShowDialog(base.ParentForm) == DialogResult.OK)
			{
				_profile.SetAvatarFromFile(openFileDialog2.FileName);
				RefreshAvatar();
			}
		};
		button2.Click += delegate
		{
			_profile.AvatarBase64 = "";
			RefreshAvatar();
		};
		button3.Click += delegate
		{
			string text2 = _playerName.Text.Trim();
			if (text2.Length == 0)
			{
				text2 = "玩家";
			}
			_profile.Name = text2;
			PlayerData.SaveProfile(_profile);
			MpManager.PlayerName = text2;
			_status("玩家信息已保存：" + _profile.Name);
			MessageBox.Show("玩家信息已保存", "玩家");
		};
		button4.Click += delegate
		{
			using SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Filter = "JSON|*.json",
				FileName = "playerdata.json"
			};
			if (saveFileDialog.ShowDialog(base.ParentForm) == DialogResult.OK)
			{
				try
				{
					File.WriteAllText(saveFileDialog.FileName, JsonSerializer.Serialize(PlayerData.Load()));
					_status("已导出玩家数据");
					return;
				}
				catch (Exception ex2)
				{
					MessageBox.Show("导出失败：" + ex2.Message);
					return;
				}
			}
		};
		button5.Click += delegate
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "JSON|*.json"
			};
			if (openFileDialog.ShowDialog(base.ParentForm) == DialogResult.OK)
			{
				try
				{
					PlayerData playerData = JsonSerializer.Deserialize<PlayerData>(File.ReadAllText(openFileDialog.FileName));
					if (playerData == null)
					{
						MessageBox.Show("玩家数据文件无效");
					}
					else
					{
						string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PlayerData");
						Directory.CreateDirectory(text);
						File.WriteAllText(Path.Combine(text, "playerdata.json"), JsonSerializer.Serialize(playerData, new JsonSerializerOptions
						{
							WriteIndented = true
						}));
						_status("已导入玩家数据");
					}
					return;
				}
				catch (Exception ex)
				{
					MessageBox.Show("导入失败：" + ex.Message);
					return;
				}
			}
		};
		Label label = new Label
		{
			Text = "最近通关记录将自动保存",
			ForeColor = UiColors.Muted,
			AutoSize = true,
			Font = new Font("Microsoft YaHei UI", 11f)
		};
		AddRow(7, Lbl("玩家名称"), _playerName);
		AddRow(7, Lbl("玩家头像"), _avatar, button, button2);
		AddRow(7, Lbl(""), button3);
		AddRow(7, Lbl(""), button4, button5);
		AddRow(7, Lbl(""), label);
		RefreshAvatar();
	}

	private void BuildKeys()
	{
		_keyCount = Cmb(110);
		for (int i = 4; i <= 10; i++)
		{
			_keyCount.Items.Add(i + "K");
		}
		_keyCount.SelectedIndex = 0;
		_keyCount.SelectedIndexChanged += delegate
		{
			LoadKeyText();
		};
		_keyText = Txt(380);
		_keyText.ImeMode = ImeMode.Off;
		_keyText.ReadOnly = true;
		_keyText.KeyDown += OnKeyCapture;
		Button button = BtnBlue("\ud83d\udcbe 应用键位", 130);
		button.Click += delegate
		{
			ApplyKeys();
		};
		Label label = new Label
		{
			Text = "点击输入框后依次按下要绑定的按键（退格删除最后一个，Del 清空）",
			ForeColor = UiColors.Muted,
			AutoSize = true,
			Font = new Font("Microsoft YaHei UI", 9f)
		};
		AddRow(8, Lbl("轨道数"), _keyCount);
		AddRow(8, Lbl("按键(按键录入)"), _keyText);
		AddRow(8, Lbl(""), button);
		AddRow(8, Lbl(""), label);
	}

	private void OnKeyCapture(object sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Back)
		{
			List<string> list = _keyText.Text.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
			if (list.Count > 0)
			{
				list.RemoveAt(list.Count - 1);
				_keyText.Text = string.Join(" ", list);
			}
			e.SuppressKeyPress = true;
			e.Handled = true;
		}
		else if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Escape)
		{
			_keyText.Text = "";
			e.SuppressKeyPress = true;
			e.Handled = true;
		}
		else if (!e.Control && !e.Alt && e.KeyCode != Keys.ShiftKey && e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.Menu && e.KeyCode != Keys.LShiftKey && e.KeyCode != Keys.RShiftKey && e.KeyCode != Keys.LControlKey && e.KeyCode != Keys.RControlKey && e.KeyCode != Keys.LMenu && e.KeyCode != Keys.RMenu)
		{
			string text = KeyName(e.KeyCode);
			if (!string.IsNullOrEmpty(text))
			{
				List<string> list2 = _keyText.Text.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
				list2.Add(text);
				_keyText.Text = string.Join(" ", list2);
				e.SuppressKeyPress = true;
				e.Handled = true;
			}
		}
	}

	private void ReloadGrid()
	{
		if (_grid == null)
		{
			return;
		}
		_grid.Rows.Clear();
		foreach (JudgeLevel level in JudgeSettings.Levels)
		{
			_grid.Rows.Add(level.Name, level.Window, level.Score, level.Weight);
		}
		_grid.Rows.Add("MISS", JudgeSettings.MissWindow, 0, 0);
	}

	private void ApplyJudgeGrid()
	{
		try
		{
			List<JudgeLevel> list = new List<JudgeLevel>();
			double missWindow = JudgeSettings.MissWindow;
			foreach (DataGridViewRow item in (IEnumerable)_grid.Rows)
			{
				if (item.Cells[0].Value != null)
				{
					string text = item.Cells[0].Value.ToString();
					if (text == "MISS")
					{
						missWindow = Convert.ToDouble(item.Cells[1].Value);
						continue;
					}
					list.Add(new JudgeLevel
					{
						Name = text,
						Window = Convert.ToDouble(item.Cells[1].Value),
						Score = Convert.ToInt32(item.Cells[2].Value),
						Weight = Convert.ToDouble(item.Cells[3].Value)
					});
				}
			}
			if (list.Count < 3)
			{
				MessageBox.Show("至少需要 3 级判定");
				return;
			}
			JudgeSettings.Levels = list;
			JudgeSettings.MissWindow = missWindow;
			JudgeSettings.PresetKey = "custom";
			_status("判定已应用（含 MISS 窗口）");
		}
		catch (Exception ex)
		{
			MessageBox.Show("数值格式无效：" + ex.Message);
		}
	}

	private void LoadKeyText()
	{
		if (_keyCount.SelectedItem != null)
		{
			int kc = int.Parse(_keyCount.SelectedItem.ToString().Replace("K", ""));
			Keys[] keys = GameSettings.GetKeys(kc);
			string[] array = new string[keys.Length];
			for (int i = 0; i < keys.Length; i++)
			{
				array[i] = KeyName(keys[i]);
			}
			_keyText.Text = string.Join(" ", array);
			if (_keyCountText != null)
			{
				_keyCountText.Text = kc + "K";
			}
		}
	}

	private void ApplyKeys()
	{
		if (_keyCount.SelectedItem == null)
		{
			return;
		}
		int num = int.Parse(_keyCount.SelectedItem.ToString().Replace("K", ""));
		string[] array = _keyText.Text.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		List<Keys> list = new List<Keys>();
		string[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			Keys keys = ParseKey(array2[i]);
			if (keys != 0)
			{
				list.Add(keys);
			}
		}
		if (list.Count == num)
		{
			GameSettings.KeyMaps[num] = list.ToArray();
			_status(num + "K 键位已保存");
		}
		else
		{
			MessageBox.Show("按键数量应为 " + num + " 个");
		}
	}

	private static string KeyName(Keys k)
	{
		return k switch
		{
			Keys.Space => "Space", 
			Keys.OemSemicolon => ";", 
			Keys.Oemcomma => ",", 
			Keys.OemPeriod => ".", 
			Keys.OemMinus => "-", 
			Keys.Oemplus => "=", 
			Keys.OemOpenBrackets => "[", 
			Keys.OemCloseBrackets => "]", 
			Keys.OemQuestion => "/", 
			Keys.OemQuotes => "'", 
			Keys.Oemtilde => "`", 
			Keys.OemPipe => "\\", 
			Keys.D0 => "0", 
			Keys.D1 => "1", 
			Keys.D2 => "2", 
			Keys.D3 => "3", 
			Keys.D4 => "4", 
			Keys.D5 => "5", 
			Keys.D6 => "6", 
			Keys.D7 => "7", 
			Keys.D8 => "8", 
			Keys.D9 => "9", 
			Keys.NumPad0 => "Num0", 
			Keys.NumPad1 => "Num1", 
			Keys.NumPad2 => "Num2", 
			Keys.NumPad3 => "Num3", 
			Keys.NumPad4 => "Num4", 
			Keys.NumPad5 => "Num5", 
			Keys.NumPad6 => "Num6", 
			Keys.NumPad7 => "Num7", 
			Keys.NumPad8 => "Num8", 
			Keys.NumPad9 => "Num9", 
			Keys.Up => "Up", 
			Keys.Down => "Down", 
			Keys.Left => "Left", 
			Keys.Right => "Right", 
			Keys.F1 => "F1", 
			Keys.F2 => "F2", 
			Keys.F3 => "F3", 
			Keys.F4 => "F4", 
			Keys.F5 => "F5", 
			Keys.F6 => "F6", 
			Keys.F7 => "F7", 
			Keys.F8 => "F8", 
			Keys.F9 => "F9", 
			Keys.F10 => "F10", 
			Keys.F11 => "F11", 
			Keys.F12 => "F12", 
			_ => k.ToString(), 
		};
	}

	private static Keys ParseKey(string s)
	{
		switch (s.ToLowerInvariant())
		{
		case "space":
			return Keys.Space;
		case ";":
			return Keys.OemSemicolon;
		case ",":
			return Keys.Oemcomma;
		case ".":
			return Keys.OemPeriod;
		case "-":
			return Keys.OemMinus;
		case "=":
			return Keys.Oemplus;
		case "[":
			return Keys.OemOpenBrackets;
		case "]":
			return Keys.OemCloseBrackets;
		case "/":
			return Keys.OemQuestion;
		case "'":
			return Keys.OemQuotes;
		case "`":
			return Keys.Oemtilde;
		case "\\":
			return Keys.OemPipe;
		case "0":
			return Keys.D0;
		case "1":
			return Keys.D1;
		case "2":
			return Keys.D2;
		case "3":
			return Keys.D3;
		case "4":
			return Keys.D4;
		case "5":
			return Keys.D5;
		case "6":
			return Keys.D6;
		case "7":
			return Keys.D7;
		case "8":
			return Keys.D8;
		case "9":
			return Keys.D9;
		case "num0":
			return Keys.NumPad0;
		case "num1":
			return Keys.NumPad1;
		case "num2":
			return Keys.NumPad2;
		case "num3":
			return Keys.NumPad3;
		case "num4":
			return Keys.NumPad4;
		case "num5":
			return Keys.NumPad5;
		case "num6":
			return Keys.NumPad6;
		case "num7":
			return Keys.NumPad7;
		case "num8":
			return Keys.NumPad8;
		case "num9":
			return Keys.NumPad9;
		case "up":
			return Keys.Up;
		case "down":
			return Keys.Down;
		case "left":
			return Keys.Left;
		case "right":
			return Keys.Right;
		case "f1":
			return Keys.F1;
		case "f2":
			return Keys.F2;
		case "f3":
			return Keys.F3;
		case "f4":
			return Keys.F4;
		case "f5":
			return Keys.F5;
		case "f6":
			return Keys.F6;
		case "f7":
			return Keys.F7;
		case "f8":
			return Keys.F8;
		case "f9":
			return Keys.F9;
		case "f10":
			return Keys.F10;
		case "f11":
			return Keys.F11;
		case "f12":
			return Keys.F12;
		default:
		{
			if (!Enum.TryParse<Keys>(s, ignoreCase: true, out var result))
			{
				return Keys.None;
			}
			return result;
		}
		}
	}

	private void RefreshAvatar()
	{
		if (_avatar != null)
		{
			Image avatarImage = _profile.AvatarImage;
			if (_avatar.Image != null)
			{
				Image image = _avatar.Image;
				_avatar.Image = null;
				image.Dispose();
			}
			_avatar.Image = avatarImage;
		}
	}
}
