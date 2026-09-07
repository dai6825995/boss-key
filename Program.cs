using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace BossKey
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            _mutex = new Mutex(true, "Global\\BossKey_SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("boss-key 已经在运行。", "boss-key", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var config = ConfigStore.Load();
            var hideService = new HideService();
            hideService.UpdateTargets(config.TargetExePaths);

            MainForm mainForm = null;
            InputHooks inputHooks = null;

            mainForm = new MainForm(hideService, config);
            inputHooks = new InputHooks(mainForm, hideService, () => mainForm.IsVisibleToUser());
            mainForm.AttachInputHooks(inputHooks);
            inputHooks.HideRequested += hideService.HideAll;
            inputHooks.ShowRequested += hideService.ShowAll;

            var tray = new TrayContext(mainForm, hideService, inputHooks);
            Application.Run(tray);
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly MainForm _mainForm;
        private readonly HideService _hideService;
        private readonly InputHooks _inputHooks;

        public TrayContext(MainForm mainForm, HideService hideService, InputHooks inputHooks)
        {
            _mainForm = mainForm;
            _hideService = hideService;
            _inputHooks = inputHooks;

            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "boss-key",
                Visible = true
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("打开设置", null, (s, e) => _mainForm.ShowFromTray());
            menu.Items.Add("立即隐藏", null, (s, e) => _hideService.HideAll());
            menu.Items.Add("立即显示", null, (s, e) => _hideService.ShowAll());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => ExitApplication());

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (s, e) => _mainForm.ShowFromTray();

            _mainForm.FormClosed += (s, e) => ExitApplication();
        }

        private void ExitApplication()
        {
            _notifyIcon.Visible = false;
            _inputHooks.Dispose();
            _hideService.Dispose();
            _mainForm.Close();
            ExitThread();
        }
    }
}
