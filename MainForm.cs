using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BossKey
{
    internal sealed class MainForm : Form
    {
        private readonly HideService _hideService;
        private InputHooks _inputHooks;
        private readonly AppConfig _config;

        private readonly ListView _listView;
        private readonly HotkeyTextBox _hideHotkeyBox;
        private readonly HotkeyTextBox _showHotkeyBox;
        private readonly CheckBox _mouseMiddleBox;
        private readonly CheckBox _mouseX1Box;
        private readonly CheckBox _mouseX2Box;
        private readonly CheckBox _startupBox;
        private readonly Label _statusLabel;

        public MainForm(HideService hideService, AppConfig config)
        {
            _hideService = hideService;
            _config = config;

            Text = "boss-key 设置";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 560);
            MinimumSize = new Size(680, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.FromArgb(240, 240, 240);
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = CreateLabel("要隐藏的软件", new Point(16, 16), new Size(300, 24), 10F, FontStyle.Bold);
            _listView = new ListView
            {
                Location = new Point(16, 44),
                Size = new Size(728, 220),
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                HideSelection = false
            };
            _listView.Columns.Add("名称", 160);
            _listView.Columns.Add("程序路径", 540);

            var addRunningBtn = CreateButton("添加正在运行的程序", new Point(16, 274), new Size(170, 32));
            addRunningBtn.Click += OnAddRunningClick;
            var browseBtn = CreateButton("浏览添加", new Point(196, 274), new Size(100, 32));
            browseBtn.Click += OnBrowseClick;
            var removeBtn = CreateButton("删除选中", new Point(306, 274), new Size(100, 32));
            removeBtn.Click += OnRemoveClick;

            var hotkeyLabel = CreateLabel("快捷键", new Point(16, 324), new Size(120, 24), 10F, FontStyle.Bold);
            var hideLabel = CreateLabel("隐藏", new Point(16, 356), new Size(60, 24));
            _hideHotkeyBox = CreateHotkeyBox(new Point(72, 352), new Size(180, 28));
            _hideHotkeyBox.SetHotkey(_config.HideHotkey);

            var showLabel = CreateLabel("显示", new Point(280, 356), new Size(60, 24));
            _showHotkeyBox = CreateHotkeyBox(new Point(336, 352), new Size(180, 28));
            _showHotkeyBox.SetHotkey(_config.ShowHotkey);

            var mouseLabel = CreateLabel("鼠标隐藏", new Point(16, 396), new Size(120, 24), 10F, FontStyle.Bold);
            _mouseMiddleBox = CreateCheckBox("中键", new Point(16, 428), _config.MouseMiddle);
            _mouseX1Box = CreateCheckBox("侧键1 (X1)", new Point(120, 428), _config.MouseX1);
            _mouseX2Box = CreateCheckBox("侧键2 (X2)", new Point(260, 428), _config.MouseX2);

            _startupBox = CreateCheckBox("开机自动启动", new Point(16, 468), _config.RunAtStartup);

            var saveBtn = CreateButton("保存设置", new Point(16, 508), new Size(100, 32));
            saveBtn.Click += OnSaveClick;
            var hideNowBtn = CreateButton("立即隐藏", new Point(126, 508), new Size(100, 32));
            hideNowBtn.Click += (s, e) => _hideService.HideAll();
            var showNowBtn = CreateButton("立即显示", new Point(236, 508), new Size(100, 32));
            showNowBtn.Click += (s, e) => _hideService.ShowAll();

            _statusLabel = CreateLabel("关闭窗口后会缩到托盘。显示只能用键盘快捷键。", new Point(360, 514), new Size(380, 24));
            _statusLabel.ForeColor = Color.FromArgb(170, 170, 170);

            var note = CreateLabel(
                "说明：隐藏后窗口会从任务栏和 Alt+Tab 消失；再次打开同名程序会立刻被藏住。部分管理员/UWP/全屏程序可能无效。",
                new Point(16, 492),
                new Size(728, 0));
            note.MaximumSize = new Size(728, 0);
            note.AutoSize = true;
            note.ForeColor = Color.FromArgb(140, 140, 140);

            Controls.AddRange(new Control[]
            {
                title, _listView, addRunningBtn, browseBtn, removeBtn,
                hotkeyLabel, hideLabel, _hideHotkeyBox, showLabel, _showHotkeyBox,
                mouseLabel, _mouseMiddleBox, _mouseX1Box, _mouseX2Box,
                _startupBox, saveBtn, hideNowBtn, showNowBtn, _statusLabel, note
            });

            LoadTargets();
            _hideService.HiddenModeChanged += OnHiddenModeChanged;
            FormClosing += OnFormClosing;
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

        protected override void WndProc(ref Message m)
        {
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
                        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(path))
                        {
                            return null;
                        }

                        return new
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
                dialog.ClientSize = new Size(520, 360);
                dialog.BackColor = BackColor;
                dialog.ForeColor = ForeColor;

                var list = new ListBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(24, 24, 24),
                    ForeColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle
                };

                foreach (var process in processes)
                {
                    list.Items.Add(process);
                }

                list.DisplayMember = "Name";

                var okBtn = new Button { Text = "添加", Dock = DockStyle.Bottom, Height = 36 };
                okBtn.Click += (s, ev) =>
                {
                    var selected = list.SelectedItem;
                    if (selected != null)
                    {
                        var path = ((dynamic)selected).Path;
                        AddTarget(path);
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

            if (_config.TargetExePaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _config.TargetExePaths.Add(path);
            LoadTargets();
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
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            _config.HideHotkey = _hideHotkeyBox.Hotkey;
            _config.ShowHotkey = _showHotkeyBox.Hotkey;
            _config.MouseMiddle = _mouseMiddleBox.Checked;
            _config.MouseX1 = _mouseX1Box.Checked;
            _config.MouseX2 = _mouseX2Box.Checked;
            _config.RunAtStartup = _startupBox.Checked;

            ConfigStore.Save(_config);
            StartupHelper.SetEnabled(_config.RunAtStartup);
            _hideService.UpdateTargets(_config.TargetExePaths);
            _inputHooks.ApplyConfig(_config);
            _statusLabel.Text = "设置已保存。";
        }

        private void OnHiddenModeChanged(bool hidden)
        {
            _statusLabel.Text = hidden ? "当前处于隐藏状态。" : "当前处于显示状态。";
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        public void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private Label CreateLabel(string text, Point location, Size size, float? sizePt = null, FontStyle style = FontStyle.Regular)
        {
            return new Label
            {
                Text = text,
                Location = location,
                Size = size,
                AutoSize = size.Height == 0,
                Font = sizePt.HasValue ? new Font("Microsoft YaHei UI", sizePt.Value, style) : Font,
                ForeColor = ForeColor
            };
        }

        private Button CreateButton(string text, Point location, Size size)
        {
            return new Button
            {
                Text = text,
                Location = location,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(58, 58, 58),
                ForeColor = Color.White
            };
        }

        private CheckBox CreateCheckBox(string text, Point location, bool isChecked)
        {
            return new CheckBox
            {
                Text = text,
                Location = location,
                AutoSize = true,
                Checked = isChecked,
                ForeColor = ForeColor
            };
        }

        private HotkeyTextBox CreateHotkeyBox(Point location, Size size)
        {
            return new HotkeyTextBox
            {
                Location = location,
                Size = size,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
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
                    key.SetValue(ValueName, Application.ExecutablePath);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
    }
}
