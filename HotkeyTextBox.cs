using System;
using System.Text;
using System.Windows.Forms;

namespace BossKey
{
    internal sealed class HotkeyTextBox : TextBox
    {
        private HotkeyConfig _hotkey;

        public HotkeyConfig Hotkey
        {
            get { return _hotkey; }
            private set { _hotkey = value; }
        }

        public HotkeyTextBox()
        {
            _hotkey = new HotkeyConfig();
            ReadOnly = true;
            Cursor = Cursors.Hand;
            Text = "点击后按下快捷键";
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Text = "请按键…";
            Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;

            if (e.KeyCode == Keys.Escape)
            {
                UpdateDisplay();
                return;
            }

            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu)
            {
                return;
            }

            uint modifiers = 0;
            if (e.Control) modifiers |= 0x0002;
            if (e.Shift) modifiers |= 0x0004;
            if (e.Alt) modifiers |= 0x0001;

            Hotkey = new HotkeyConfig(modifiers, (uint)e.KeyValue);
            UpdateDisplay();
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            UpdateDisplay();
        }

        public void SetHotkey(HotkeyConfig hotkey)
        {
            Hotkey = hotkey ?? new HotkeyConfig();
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            Text = FormatHotkey(Hotkey);
        }

        public static string FormatHotkey(HotkeyConfig hotkey)
        {
            if (hotkey == null)
            {
                return "未设置";
            }

            var sb = new StringBuilder();
            if ((hotkey.Modifiers & 0x0002) != 0) sb.Append("Ctrl+");
            if ((hotkey.Modifiers & 0x0004) != 0) sb.Append("Shift+");
            if ((hotkey.Modifiers & 0x0001) != 0) sb.Append("Alt+");

            var keyName = ((Keys)hotkey.VirtualKey).ToString();
            if (hotkey.VirtualKey == 0xC0) keyName = "`";
            sb.Append(keyName);
            return sb.ToString();
        }
    }
}
