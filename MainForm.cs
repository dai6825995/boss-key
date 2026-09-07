using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BossKey
{
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(18, 20, 24);
        public static readonly Color Card = Color.FromArgb(28, 32, 38);
        public static readonly Color CardAlt = Color.FromArgb(36, 41, 48);
        public static readonly Color Line = Color.FromArgb(52, 58, 68);
        public static readonly Color Text = Color.FromArgb(236, 239, 244);
        public static readonly Color Muted = Color.FromArgb(148, 156, 168);
        public static readonly Color Accent = Color.FromArgb(79, 140, 255);
        public static readonly Color AccentDark = Color.FromArgb(50, 102, 210);
        public static readonly Color Danger = Color.FromArgb(214, 72, 72);
        public static readonly Color Ok = Color.FromArgb(56, 176, 120);
    }

    internal sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            BackColor = Theme.Card;
            Padding = new Padding(16);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Line))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    internal sealed class AccentButton : Button
    {
        private readonly Color _back;
        private readonly Color _hover;

        public AccentButton(string text, Color back, Color hover)
        {
            Text = text;
            _back = back;
            _hover = hover;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = back;
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            BackColor = _hover;
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            BackColor = _back;
            base.OnMouseLeave(e);
        }
    }

    internal sealed class RunningAppItem
    {
        public string Name { get; set; }
        public string Path { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly HideService _hideService;
        private readonly WindowFx _windowFx;
        private InputHooks _inputHooks;
        private readonly AppConfig _config;

        private ListView _listView;
        private HotkeyTextBox _hideHotkeyBox;
        private HotkeyTextBox _showHotkeyBox;
        private HotkeyTextBox _topmostHotkeyBox;
        private HotkeyTextBox _opacityDownBox;
        private HotkeyTextBox _opacityUpBox;
        private HotkeyTextBox _opacityRestoreBox;
        private HotkeyTextBox _settingsHotkeyBox;
        private CheckBox _mouseMiddleBox;
        private CheckBox _mouseX1Box;
        private CheckBox _mouseX2Box;
        private CheckBox _startupBox;
        private Label _statusLabel;
        private Label _stateChip;
        private bool _allowClose;
        private bool _allowVisible;
        private static readonly uint ShowSettingsMessage = WinApi.RegisterWindowMessage("BossKey.ShowSettings");

        public event Action ExitRequested;

        public MainForm(HideService hideService, WindowFx windowFx, AppConfig config)
        {
            _hideService = hideService;
            _windowFx = windowFx;
            _config = config;

            Text = "boss-key";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 800);
            MinimumSize = new Size(900, 800);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(BuildHeader());
            Controls.Add(BuildListCard());
            Controls.Add(BuildHotkeyCard());
            Controls.Add(BuildOptionCard());
            Controls.Add(BuildFooter());

            _listView = (ListView)Controls.Find("appList", true)[0];
            _hideHotkeyBox = (HotkeyTextBox)Controls.Find("hideHotkey", true)[0];
            _showHotkeyBox = (HotkeyTextBox)Controls.Find("showHotkey", true)[0];
            _topmostHotkeyBox = (HotkeyTextBox)Controls.Find("topmostHotkey", true)[0];
            _opacityDownBox = (HotkeyTextBox)Controls.Find("opacityDown", true)[0];
            _opacityUpBox = (HotkeyTextBox)Controls.Find("opacityUp", true)[0];
            _opacityRestoreBox = (HotkeyTextBox)Controls.Find("opacityRestore", true)[0];
            _settingsHotkeyBox = (HotkeyTextBox)Controls.Find("settingsHotkey", true)[0];
            _mouseMiddleBox = (CheckBox)Controls.Find("mouseMiddle", true)[0];
            _mouseX1Box = (CheckBox)Controls.Find("mouseX1", true)[0];
            _mouseX2Box = (CheckBox)Controls.Find("mouseX2", true)[0];
            _startupBox = (CheckBox)Controls.Find("startup", true)[0];
            _statusLabel = (Label)Controls.Find("statusLabel", true)[0];
            _stateChip = (Label)Controls.Find("stateChip", true)[0];

            _hideHotkeyBox.SetHotkey(_config.HideHotkey);
            _showHotkeyBox.SetHotkey(_config.ShowHotkey);
            _topmostHotkeyBox.SetHotkey(_config.TopmostHotkey);
            _opacityDownBox.SetHotkey(_config.OpacityDownHotkey);
            _opacityUpBox.SetHotkey(_config.OpacityUpHotkey);
            _opacityRestoreBox.SetHotkey(_config.OpacityRestoreHotkey);
            _settingsHotkeyBox.SetHotkey(_config.SettingsHotkey);

            LoadTargets();
            _hideService.HiddenModeChanged += OnHiddenModeChanged;
            FormClosing += OnFormClosing;
        }

        private Control BuildHeader()
        {
            var header = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(900, 78),
                BackColor = Theme.Card
            };

            var title = new Label
            {
                Text = "boss-key",
                Location = new Point(24, 14),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
                ForeColor = Theme.Text
            };

            var subtitle = new Label
            {
                Text = "摸鱼助手  ·  隐藏 / 置顶 / 透明度",
                Location = new Point(26, 46),
                AutoSize = true,
                ForeColor = Theme.Muted
            };

            _stateChip = new Label
            {
                Name = "stateChip",
                Text = "待命",
                Location = new Point(760, 26),
                Size = new Size(116, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Theme.CardAlt,
                ForeColor = Theme.Ok
            };

            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(_stateChip);
            return header;
        }

        private Control BuildListCard()
        {
            var card = new CardPanel
            {
                Location = new Point(20, 94),
                Size = new Size(860, 248)
            };

            var title = MakeMuted("摸鱼名单", new Point(16, 12));
            var list = new ListView
            {
                Name = "appList",
                Location = new Point(16, 40),
                Size = new Size(828, 154),
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = Theme.CardAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None,
                HideSelection = false
            };
            list.Columns.Add("软件", 180);
            list.Columns.Add("路径", 620);

            var addRunning = MakeButton("添加运行中的程序", new Point(16, 204), new Size(156, 30), Theme.Accent, Theme.AccentDark);
            addRunning.Click += OnAddRunningClick;
            var browse = MakeButton("浏览添加", new Point(180, 204), new Size(88, 30), Theme.CardAlt, Theme.Line);
            browse.Click += OnBrowseClick;
            var remove = MakeButton("删除选中", new Point(276, 204), new Size(88, 30), Theme.Danger, Color.FromArgb(176, 52, 52));
            remove.Click += OnRemoveClick;

            card.Controls.Add(title);
            card.Controls.Add(list);
            card.Controls.Add(addRunning);
            card.Controls.Add(browse);
            card.Controls.Add(remove);
            return card;
        }

        private Control BuildHotkeyCard()
        {
            var card = new CardPanel
            {
                Location = new Point(20, 354),
                Size = new Size(860, 264)
            };

            card.Controls.Add(MakeMuted("全局快捷键", new Point(16, 12)));
            card.Controls.Add(MakeHint("鼠标对准窗口，按住 Ctrl+Alt 滚轮调透明度。UU 远程画面除外。点中键可置顶。", new Point(110, 14)));

            AddHotkeyRow(card, "隐藏软件", "hideHotkey", 40);
            AddHotkeyRow(card, "显示软件", "showHotkey", 72);
            AddHotkeyRow(card, "置顶 / 取消置顶", "topmostHotkey", 104);
            AddHotkeyRow(card, "降低透明度", "opacityDown", 136);
            AddHotkeyRow(card, "提高透明度", "opacityUp", 168);
            AddHotkeyRow(card, "恢复透明度", "opacityRestore", 200);
            AddHotkeyRow(card, "打开设置", "settingsHotkey", 232);

            return card;
        }

        private void AddHotkeyRow(Control parent, string label, string name, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = label,
                Location = new Point(16, y + 4),
                Size = new Size(140, 22),
                ForeColor = Theme.Text
            });

            var box = CreateHotkeyBox(new Point(168, y), new Size(220, 28));
            box.Name = name;
            parent.Controls.Add(box);
        }

        private Control BuildOptionCard()
        {
            var card = new CardPanel
            {
                Location = new Point(20, 630),
                Size = new Size(860, 72)
            };

            card.Controls.Add(MakeMuted("其他", new Point(16, 10)));
            card.Controls.Add(MakeHint("远程/游戏里请用键盘热键隐藏（按着 Shift 也行）。显示只能用键盘。", new Point(60, 10)));
            _mouseMiddleBox = CreateCheckBox("鼠标中键隐藏", new Point(16, 36), _config.MouseMiddle);
            _mouseMiddleBox.Name = "mouseMiddle";
            _mouseX1Box = CreateCheckBox("侧键1 隐藏", new Point(160, 36), _config.MouseX1);
            _mouseX1Box.Name = "mouseX1";
            _mouseX2Box = CreateCheckBox("侧键2 隐藏", new Point(280, 36), _config.MouseX2);
            _mouseX2Box.Name = "mouseX2";
            _startupBox = CreateCheckBox("开机启动", new Point(400, 36), _config.RunAtStartup);
            _startupBox.Name = "startup";

            card.Controls.Add(_mouseMiddleBox);
            card.Controls.Add(_mouseX1Box);
            card.Controls.Add(_mouseX2Box);
            card.Controls.Add(_startupBox);
            return card;
        }

        private Control BuildFooter()
        {
            var footer = new Panel
            {
                Location = new Point(20, 714),
                Size = new Size(860, 62),
                BackColor = Theme.Bg
            };

            var save = MakeButton("保存设置", new Point(0, 10), new Size(100, 34), Theme.Accent, Theme.AccentDark);
            save.Click += OnSaveClick;
            var hideNow = MakeButton("立即隐藏", new Point(110, 10), new Size(100, 34), Theme.CardAlt, Theme.Line);
            hideNow.Click += (s, e) => _hideService.HideAll();
            var showNow = MakeButton("立即显示", new Point(220, 10), new Size(100, 34), Theme.CardAlt, Theme.Line);
            showNow.Click += (s, e) => _hideService.ShowAll();
            var restoreFx = MakeButton("恢复外观", new Point(330, 10), new Size(100, 34), Theme.Ok, Color.FromArgb(40, 140, 96));
            restoreFx.Click += (s, e) =>
            {
                _windowFx.RestoreAll();
                SetStatus("已恢复置顶和透明度");
            };
            var exit = MakeButton("退出", new Point(440, 10), new Size(88, 34), Theme.Danger, Color.FromArgb(176, 52, 52));
            exit.Click += (s, e) =>
            {
                if (ExitRequested != null)
                {
                    ExitRequested();
                }
            };

            _statusLabel = new Label
            {
                Name = "statusLabel",
                Text = "后台运行，不占任务栏和托盘。Ctrl+Shift+9 打开设置。",
                Location = new Point(540, 16),
                Size = new Size(320, 24),
                ForeColor = Theme.Muted
            };

            footer.Controls.Add(save);
            footer.Controls.Add(hideNow);
            footer.Controls.Add(showNow);
            footer.Controls.Add(restoreFx);
            footer.Controls.Add(exit);
            footer.Controls.Add(_statusLabel);
            return footer;
        }

        public void SetStatus(string text)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetStatus), text);
                return;
            }

            _statusLabel.Text = text;
        }

        public bool IsVisibleToUser()
        {
            return Visible && WindowState != FormWindowState.Minimized;
        }

        public void AttachInputHooks(InputHooks inputHooks)
        {
            _inputHooks = inputHooks;
            if (IsHandleCreated)
            {
                _inputHooks.ApplyConfig(_config);
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!IsHandleCreated)
            {
                CreateHandle();
            }

            if (!_allowVisible)
            {
                value = false;
            }

            base.SetVisibleCore(value);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WinApi.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)ShowSettingsMessage)
            {
                ToggleSettings();
                return;
            }

            if (_inputHooks != null && _inputHooks.ProcessHotkeyMessage(m))
            {
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_inputHooks != null)
            {
                _inputHooks.ApplyConfig(_config);
            }
        }

        private void LoadTargets()
        {
            _listView.Items.Clear();
            foreach (var path in _config.TargetExePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var item = new ListViewItem(ConfigStore.GetDisplayName(path));
                item.SubItems.Add(path);
                item.Tag = path;
                _listView.Items.Add(item);
            }

            _hideService.UpdateTargets(_config.TargetExePaths);
        }

        private void OnAddRunningClick(object sender, EventArgs e)
        {
            var processes = Process.GetProcesses()
                .Select(p =>
                {
                    try
                    {
                        var path = p.MainModule.FileName;
                        var title = p.MainWindowTitle;
                        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(path) || ConfigStore.IsProtectedExe(path))
                        {
                            return null;
                        }

                        return new RunningAppItem
                        {
                            Name = title,
                            Path = ConfigStore.NormalizeExePath(path)
                        };
                    }
                    catch
                    {
                        return null;
                    }
                    finally
                    {
                        p.Dispose();
                    }
                })
                .Where(x => x != null)
                .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.Name)
                .ToList();

            if (processes.Count == 0)
            {
                MessageBox.Show(this, "没有找到带窗口的正在运行程序。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new Form())
            {
                dialog.Text = "选择正在运行的程序";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ClientSize = new Size(520, 380);
                dialog.BackColor = Theme.Bg;
                dialog.ForeColor = Theme.Text;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;

                var list = new ListBox
                {
                    Location = new Point(16, 16),
                    Size = new Size(488, 310),
                    BackColor = Theme.Card,
                    ForeColor = Theme.Text,
                    BorderStyle = BorderStyle.None
                };

                foreach (var process in processes)
                {
                    list.Items.Add(process);
                }

                var okBtn = MakeButton("添加", new Point(404, 336), new Size(100, 32), Theme.Accent, Theme.AccentDark);
                okBtn.Click += (s, ev) =>
                {
                    var selected = list.SelectedItem as RunningAppItem;
                    if (selected != null)
                    {
                        AddTarget(selected.Path);
                    }
                    dialog.DialogResult = DialogResult.OK;
                };

                dialog.Controls.Add(list);
                dialog.Controls.Add(okBtn);
                dialog.ShowDialog(this);
            }
        }

        private void OnBrowseClick(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "可执行文件 (*.exe)|*.exe";
                dialog.Title = "选择要隐藏的软件";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddTarget(ConfigStore.NormalizeExePath(dialog.FileName));
                }
            }
        }

        private void AddTarget(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (ConfigStore.IsProtectedExe(path))
            {
                MessageBox.Show(this, "系统进程不能添加，隐藏后会导致桌面点不动。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_config.TargetExePaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _config.TargetExePaths.Add(path);
            LoadTargets();
            PersistList();
        }

        private void OnRemoveClick(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count == 0)
            {
                return;
            }

            var path = _listView.SelectedItems[0].Tag as string;
            _config.TargetExePaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            LoadTargets();
            PersistList();
        }

        private void PersistList()
        {
            ConfigStore.Save(_config);
            _hideService.UpdateTargets(_config.TargetExePaths);
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            _config.HideHotkey = _hideHotkeyBox.Hotkey;
            _config.ShowHotkey = _showHotkeyBox.Hotkey;
            _config.TopmostHotkey = _topmostHotkeyBox.Hotkey;
            _config.OpacityDownHotkey = _opacityDownBox.Hotkey;
            _config.OpacityUpHotkey = _opacityUpBox.Hotkey;
            _config.OpacityRestoreHotkey = _opacityRestoreBox.Hotkey;
            _config.SettingsHotkey = _settingsHotkeyBox.Hotkey;
            _config.MouseMiddle = _mouseMiddleBox.Checked;
            _config.MouseX1 = _mouseX1Box.Checked;
            _config.MouseX2 = _mouseX2Box.Checked;
            _config.RunAtStartup = _startupBox.Checked;

            ConfigStore.Save(_config);
            StartupHelper.SetEnabled(_config.RunAtStartup);
            _hideService.UpdateTargets(_config.TargetExePaths);
            if (_inputHooks != null)
            {
                _inputHooks.ApplyConfig(_config);
            }
            SetStatus("设置已保存");
        }

        private void OnHiddenModeChanged(bool hidden)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(OnHiddenModeChanged), hidden);
                return;
            }

            _stateChip.Text = hidden ? "已隐藏" : "待命";
            _stateChip.ForeColor = hidden ? Theme.Danger : Theme.Ok;
            SetStatus(hidden ? "当前处于隐藏状态" : "当前处于显示状态");
        }

        public void CloseForExit()
        {
            _allowClose = true;
            Close();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                _allowVisible = false;
                TopMost = false;
                ShowInTaskbar = false;
                Hide();
            }
        }

        public void ToggleSettings()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ToggleSettings));
                return;
            }

            if (IsVisibleToUser())
            {
                _allowVisible = false;
                TopMost = false;
                ShowInTaskbar = false;
                Hide();
                return;
            }

            ShowSettings();
        }

        public void ShowSettings()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ShowSettings));
                return;
            }

            _allowVisible = true;
            ShowInTaskbar = false;
            WindowState = FormWindowState.Normal;
            TopMost = true;
            Show();
            BringToFront();
            WinApi.SetForegroundWindow(Handle);
            Activate();
            BeginInvoke(new Action(delegate { TopMost = false; }));
        }

        public void ShowFromTray()
        {
            ShowSettings();
        }

        private static Label MakeMuted(string text, Point location)
        {
            return new Label
            {
                Text = text,
                Location = location,
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
        }

        private static Label MakeHint(string text, Point location)
        {
            return new Label
            {
                Text = text,
                Location = location,
                AutoSize = true,
                ForeColor = Theme.Muted
            };
        }

        private static AccentButton MakeButton(string text, Point location, Size size, Color back, Color hover)
        {
            var button = new AccentButton(text, back, hover);
            button.Location = location;
            button.Size = size;
            return button;
        }

        private CheckBox CreateCheckBox(string text, Point location, bool isChecked)
        {
            return new CheckBox
            {
                Text = text,
                Location = location,
                AutoSize = true,
                Checked = isChecked,
                ForeColor = Theme.Text,
                BackColor = Theme.Card
            };
        }

        private HotkeyTextBox CreateHotkeyBox(Point location, Size size)
        {
            return new HotkeyTextBox
            {
                Location = location,
                Size = size,
                BackColor = Theme.CardAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center
            };
        }
    }

    internal static class StartupHelper
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "boss-key";

        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null)
                {
                    return;
                }

                if (enabled)
                {
                    key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" --silent");
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
    }
}
