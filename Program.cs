using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace BossKey
{
    internal static class Program
    {
        private static Mutex _mutex;
        public static readonly uint ShowSettingsMessage = 0x8007;

        [STAThread]
        private static void Main()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => LogFatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogFatal(e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject)));
            };

            try
            {
                RunApp();
            }
            catch (Exception ex)
            {
                LogFatal(ex);
            }
        }

        private static void LogFatal(Exception ex)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "boss-key");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "crash.log"), DateTime.Now + Environment.NewLine + (ex == null ? "unknown" : ex.ToString()));
            }
            catch
            {
            }

            try
            {
                MessageBox.Show(ex == null ? "未知错误" : ex.ToString(), "boss-key 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
            }
        }

        private static void RunApp()
        {
            bool createdNew;
            _mutex = new Mutex(true, "Global\\BossKey_SingleInstance", out createdNew);
            if (!createdNew)
            {
                if (NotifyExistingInstance())
                {
                    return;
                }

                try
                {
                    if (!_mutex.WaitOne(1500))
                    {
                        NotifyExistingInstance();
                        return;
                    }
                }
                catch (AbandonedMutexException)
                {
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var silent = HasSilentArg();
            var config = ConfigStore.Load();
            if (config.TargetExePaths == null)
            {
                config.TargetExePaths = new List<string>();
            }

            if (config.RunAtStartup)
            {
                StartupHelper.SetEnabled(true);
            }

            var hideService = new HideService();
            hideService.UpdateTargets(config.TargetExePaths);
            hideService.RestoreLeftovers();
            hideService.HideLeakedHelpers();

            var windowFx = new WindowFx();
            windowFx.RepairDamagedSurfaces();
            var pump = new PumpForm();
            var mainForm = new MainForm(hideService, windowFx, config);
            var inputHooks = new InputHooks(pump, hideService, () => mainForm.IsVisibleToUser());
            mainForm.AttachInputHooks(inputHooks);
            pump.Hooks = inputHooks;
            pump.ShowSettings = mainForm.ShowSettings;

            inputHooks.HideRequested += hideService.HideAll;
            inputHooks.ShowRequested += hideService.ShowAll;
            inputHooks.TopmostRequested += () => mainForm.SetStatus(windowFx.ToggleTopmost());
            inputHooks.OpacityDownRequested += () => mainForm.SetStatus(windowFx.AdjustOpacity(-25));
            inputHooks.OpacityUpRequested += () => mainForm.SetStatus(windowFx.AdjustOpacity(25));
            inputHooks.OpacityRestoreRequested += () => mainForm.SetStatus(windowFx.RestoreOpacity());
            inputHooks.SettingsRequested += mainForm.ToggleSettings;

            var context = new AppContext(pump, mainForm, hideService, inputHooks, windowFx, !silent);
            Application.Run(context);
        }

        private static bool HasSilentArg()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 1; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--silent", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(args[i], "/silent", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NotifyExistingInstance()
        {
            for (var i = 0; i < 20; i++)
            {
                var existing = FindPumpWindow();
                if (existing != IntPtr.Zero && WinApi.IsWindow(existing))
                {
                    WinApi.AllowSetForegroundWindow(-1);
                    WinApi.PostMessage(existing, ShowSettingsMessage, IntPtr.Zero, IntPtr.Zero);
                    return true;
                }

                Thread.Sleep(50);
            }

            return false;
        }

        private static IntPtr FindPumpWindow()
        {
            var existing = WinApi.FindWindow(null, PumpForm.WindowTitle);
            if (existing != IntPtr.Zero && WinApi.IsWindow(existing))
            {
                return existing;
            }

            try
            {
                var hwndPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "boss-key",
                    "hwnd.txt");
                long raw;
                if (File.Exists(hwndPath) && long.TryParse(File.ReadAllText(hwndPath).Trim(), out raw) && raw != 0)
                {
                    existing = new IntPtr(raw);
                    if (WinApi.IsWindow(existing))
                    {
                        return existing;
                    }
                }
            }
            catch
            {
            }

            return IntPtr.Zero;
        }
    }

    internal sealed class PumpForm : Form
    {
        public const string WindowTitle = "BossKeyPump";

        public InputHooks Hooks;
        public Action ShowSettings;

        public PumpForm()
        {
            Text = WindowTitle;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
            Opacity = 1;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WinApi.WS_EX_TOOLWINDOW | WinApi.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "boss-key");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "hwnd.txt"), Handle.ToInt64().ToString());
            }
            catch
            {
            }

            if (Hooks != null)
            {
                Hooks.Rebind();
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)Program.ShowSettingsMessage)
            {
                if (ShowSettings != null)
                {
                    ShowSettings();
                }
                return;
            }

            if (Hooks != null && Hooks.ProcessHotkeyMessage(m))
            {
                return;
            }

            base.WndProc(ref m);
        }
    }

    internal sealed class AppContext : ApplicationContext
    {
        private readonly HideService _hideService;
        private readonly InputHooks _inputHooks;
        private readonly WindowFx _windowFx;
        private readonly MainForm _mainForm;
        private bool _exiting;

        public AppContext(PumpForm pump, MainForm mainForm, HideService hideService, InputHooks inputHooks, WindowFx windowFx, bool showSettingsOnStart)
        {
            _mainForm = mainForm;
            _hideService = hideService;
            _inputHooks = inputHooks;
            _windowFx = windowFx;
            MainForm = pump;
            _mainForm.ExitRequested += ExitApplication;
            if (showSettingsOnStart)
            {
                pump.Shown += (s, e) => _mainForm.ShowSettings();
            }
        }

        private void ExitApplication()
        {
            if (_exiting)
            {
                return;
            }

            _exiting = true;
            _inputHooks.Dispose();
            _windowFx.RestoreAll();
            _hideService.ShowAll();
            _hideService.Dispose();
            _mainForm.CloseForExit();
            if (MainForm != null)
            {
                MainForm.Close();
            }
            ExitThread();
        }
    }
}
