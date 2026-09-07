using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BossKey
{
    internal sealed class HiddenWindowState
    {
        public IntPtr Handle { get; set; }
        public int ExStyle { get; set; }
        public bool WasVisible { get; set; }
        public bool WasMinimized { get; set; }
        public bool UsedCloak { get; set; }
        public bool SkipExStyle { get; set; }
        public string ExePath { get; set; }
    }

    internal sealed class HideService : IDisposable
    {
        private readonly object _sync = new object();
        private readonly Dictionary<IntPtr, HiddenWindowState> _hidden = new Dictionary<IntPtr, HiddenWindowState>();
        private readonly HashSet<string> _targetExePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _watchTimer;
        private readonly List<IntPtr> _winEventHooks = new List<IntPtr>();
        private WinApi.WinEventDelegate _winEventDelegate;
        private bool _isHiddenMode;
        private readonly int _ownProcessId;
        private static readonly uint WindowRestoredMessage = 0x8008;

        public event Action<bool> HiddenModeChanged;

        public bool IsHiddenMode
        {
            get { return _isHiddenMode; }
        }

        public HideService()
        {
            _ownProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
            _watchTimer = new Timer(OnWatchTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void UpdateTargets(IEnumerable<string> exePaths)
        {
            lock (_sync)
            {
                _targetExePaths.Clear();
                foreach (var path in exePaths ?? Enumerable.Empty<string>())
                {
                    var normalized = ConfigStore.NormalizeExePath(path);
                    if (!string.IsNullOrEmpty(normalized) && !ConfigStore.IsProtectedExe(normalized))
                    {
                        _targetExePaths.Add(normalized);
                    }
                }
            }
        }

        public void HideAll()
        {
            bool enteredHiddenMode = false;

            lock (_sync)
            {
                if (_targetExePaths.Count == 0)
                {
                    return;
                }

                ReleaseInputFromTargets();

                foreach (var hwnd in EnumerateTargetWindows(false))
                {
                    HideWindow(hwnd);
                }

                if (!_isHiddenMode)
                {
                    _isHiddenMode = true;
                    StartWatchers();
                    enteredHiddenMode = true;
                }
            }

            if (enteredHiddenMode)
            {
                RaiseHiddenModeChanged(true);
            }
        }

        public void ShowAll()
        {
            bool leftHiddenMode = false;
            List<HiddenWindowState> toRestore;

            lock (_sync)
            {
                if (_isHiddenMode)
                {
                    _isHiddenMode = false;
                    StopWatchers();
                    leftHiddenMode = true;
                }

                toRestore = _hidden.Values.ToList();
                _hidden.Clear();
            }

            foreach (var state in toRestore)
            {
                RestoreWindow(state);
            }

            if (leftHiddenMode)
            {
                RaiseHiddenModeChanged(false);
            }
        }

        public void RestoreLeftovers()
        {
            lock (_sync)
            {
                _isHiddenMode = false;
                StopWatchers();
                _hidden.Clear();
            }

            foreach (var hwnd in EnumerateTargetWindows(true))
            {
                if (IsMainAppWindow(hwnd) && IsCloaked(hwnd))
                {
                    CloakWindow(hwnd, false);
                    ForceRepaint(hwnd);
                }
            }
        }

        public void HideLeakedHelpers()
        {
            WinApi.EnumWindows((hwnd, lParam) =>
            {
                if (!WinApi.IsWindow(hwnd) || !WinApi.IsWindowVisible(hwnd) || !IsHelperWindow(hwnd))
                {
                    return true;
                }

                uint processId;
                WinApi.GetWindowThreadProcessId(hwnd, out processId);
                var exePath = GetProcessExePath(processId);
                if (string.IsNullOrEmpty(exePath) || !_targetExePaths.Contains(exePath))
                {
                    return true;
                }

                var exStyle = WinApi.GetWindowLongInt(hwnd, WinApi.GWL_EXSTYLE);
                exStyle |= WinApi.WS_EX_TOOLWINDOW;
                exStyle &= ~WinApi.WS_EX_APPWINDOW;
                WinApi.SetWindowLongPtr(hwnd, WinApi.GWL_EXSTYLE, new IntPtr(exStyle));
                WinApi.ShowWindow(hwnd, WinApi.SW_HIDE);
                return true;
            }, IntPtr.Zero);
        }

        private void RaiseHiddenModeChanged(bool hidden)
        {
            var handler = HiddenModeChanged;
            if (handler != null)
            {
                handler(hidden);
            }
        }

        private void StartWatchers()
        {
            _winEventDelegate = OnWinEvent;
            AddWinEventHook(WinApi.EVENT_OBJECT_SHOW, WinApi.EVENT_OBJECT_SHOW);
            AddWinEventHook(WinApi.EVENT_OBJECT_UNCLOAKED, WinApi.EVENT_OBJECT_UNCLOAKED);
            AddWinEventHook(WinApi.EVENT_SYSTEM_FOREGROUND, WinApi.EVENT_SYSTEM_FOREGROUND);
            _watchTimer.Change(400, 400);
        }

        private void AddWinEventHook(uint eventMin, uint eventMax)
        {
            var hook = WinApi.SetWinEventHook(
                eventMin,
                eventMax,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                WinApi.WINEVENT_OUTOFCONTEXT | WinApi.WINEVENT_SKIPOWNPROCESS);

            if (hook != IntPtr.Zero)
            {
                _winEventHooks.Add(hook);
            }
        }

        private void StopWatchers()
        {
            _watchTimer.Change(Timeout.Infinite, Timeout.Infinite);
            foreach (var hook in _winEventHooks)
            {
                WinApi.UnhookWinEvent(hook);
            }
            _winEventHooks.Clear();
        }

        private void OnWatchTimer(object state)
        {
            lock (_sync)
            {
                if (!_isHiddenMode)
                {
                    return;
                }

                foreach (var hwnd in EnumerateTargetWindows(false))
                {
                    HideWindow(hwnd);
                }
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd == IntPtr.Zero || idObject != 0)
            {
                return;
            }

            lock (_sync)
            {
                if (!_isHiddenMode || !IsTargetWindow(hwnd))
                {
                    return;
                }

                ReleaseInputIfNeeded(hwnd);
                HideWindow(hwnd);
            }
        }

        private List<IntPtr> EnumerateTargetWindows(bool includeHidden)
        {
            var result = new List<IntPtr>();
            WinApi.EnumWindows((hwnd, lParam) =>
            {
                if (IsTargetWindow(hwnd, includeHidden))
                {
                    result.Add(hwnd);
                }

                return true;
            }, IntPtr.Zero);

            return result;
        }

        private bool IsTargetWindow(IntPtr hwnd)
        {
            return IsTargetWindow(hwnd, false);
        }

        private bool IsTargetWindow(IntPtr hwnd, bool includeHidden)
        {
            if (hwnd == IntPtr.Zero || !WinApi.IsWindow(hwnd))
            {
                return false;
            }

            if (!includeHidden && !WinApi.IsWindowVisible(hwnd))
            {
                return false;
            }

            uint processId;
            WinApi.GetWindowThreadProcessId(hwnd, out processId);
            if (processId == 0 || processId == (uint)_ownProcessId)
            {
                return false;
            }

            var exePath = GetProcessExePath(processId);
            if (string.IsNullOrEmpty(exePath)
                || ConfigStore.IsProtectedExe(exePath)
                || !_targetExePaths.Contains(exePath))
            {
                return false;
            }

            return IsMainAppWindow(hwnd, exePath);
        }

        private bool IsMainAppWindow(IntPtr hwnd)
        {
            uint processId;
            WinApi.GetWindowThreadProcessId(hwnd, out processId);
            return IsMainAppWindow(hwnd, GetProcessExePath(processId));
        }

        private bool IsMainAppWindow(IntPtr hwnd, string exePath)
        {
            try
            {
                if (WinApi.GetWindow(hwnd, WinApi.GW_OWNER) != IntPtr.Zero)
                {
                    return false;
                }

                var exStyle = WinApi.GetWindowLongInt(hwnd, WinApi.GWL_EXSTYLE);
                if ((exStyle & WinApi.WS_EX_NOACTIVATE) != 0)
                {
                    return false;
                }

                if (IsHelperWindow(hwnd))
                {
                    return false;
                }

                WinApi.RECT rect;
                if (WinApi.GetWindowRect(hwnd, out rect))
                {
                    var width = rect.Right - rect.Left;
                    var height = rect.Bottom - rect.Top;
                    if (width < 80 || height < 80)
                    {
                        return false;
                    }
                }

                var style = WinApi.GetWindowLongInt(hwnd, WinApi.GWL_STYLE);
                var hasCaption = (style & WinApi.WS_CAPTION) == WinApi.WS_CAPTION;
                if (!hasCaption && !IsFramelessAllowed(exePath))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsFramelessAllowed(string exePath)
        {
            return !string.IsNullOrEmpty(exePath) && _targetExePaths.Contains(exePath);
        }

        private static bool IsStyleSensitiveExe(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
            {
                return false;
            }

            var name = Path.GetFileName(exePath);
            return string.Equals(name, "摄像头.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCloaked(IntPtr hwnd)
        {
            int cloaked;
            if (WinApi.DwmGetWindowAttribute(hwnd, WinApi.DWMWA_CLOAKED, out cloaked, 4) != 0)
            {
                return false;
            }

            return cloaked != 0;
        }

        private static bool IsHelperWindow(IntPtr hwnd)
        {
            var title = GetWindowTitle(hwnd);
            var className = GetWindowClass(hwnd);

            if (!string.IsNullOrEmpty(title)
                && (title.IndexOf("MSCTFIME", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("Default IME", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("HintWnd", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("Sogou", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("预览屏幕", StringComparison.OrdinalIgnoreCase) >= 0
                || title == "关闭"
                || LooksLikeGuid(title)))
            {
                return true;
            }

            if (className.IndexOf("IME", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("Sogou", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("Hint", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("tooltip", StringComparison.OrdinalIgnoreCase) >= 0
                || className == "#32768"
                || className == "SysShadow")
            {
                return true;
            }

            return false;
        }

        private static bool LooksLikeGuid(string title)
        {
            return title.Length >= 32 && title.IndexOf('-') >= 0 && title.IndexOf(' ') < 0;
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            var length = WinApi.GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return string.Empty;
            }

            var buffer = new StringBuilder(length + 1);
            WinApi.GetWindowText(hwnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        private static string GetWindowClass(IntPtr hwnd)
        {
            var buffer = new StringBuilder(256);
            WinApi.GetClassName(hwnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        private static string GetProcessExePath(uint processId)
        {
            var handle = WinApi.OpenProcess(WinApi.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                var size = 1024;
                var buffer = new StringBuilder(size);
                if (!WinApi.QueryFullProcessImageName(handle, 0, buffer, ref size))
                {
                    return string.Empty;
                }

                return ConfigStore.NormalizeExePath(buffer.ToString());
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                WinApi.CloseHandle(handle);
            }
        }

        private void ReleaseInputFromTargets()
        {
            var foreground = WinApi.GetForegroundWindow();
            if (IsTargetWindow(foreground))
            {
                ReleaseInputIfNeeded(foreground);
            }
        }

        private static void ReleaseInputIfNeeded(IntPtr hwnd)
        {
            var capture = WinApi.GetCapture();
            if (capture == hwnd || capture != IntPtr.Zero)
            {
                WinApi.ReleaseCapture();
            }

            var shell = WinApi.GetShellWindow();
            if (shell == IntPtr.Zero)
            {
                shell = WinApi.GetDesktopWindow();
            }

            if (WinApi.GetForegroundWindow() == hwnd && shell != IntPtr.Zero)
            {
                WinApi.SetForegroundWindow(shell);
            }
        }

        private void HideWindow(IntPtr hwnd)
        {
            if (_hidden.ContainsKey(hwnd))
            {
                CloakWindow(hwnd, true);
                return;
            }

            uint processId;
            WinApi.GetWindowThreadProcessId(hwnd, out processId);
            var exePath = GetProcessExePath(processId);

            var exStyle = WinApi.GetWindowLongInt(hwnd, WinApi.GWL_EXSTYLE);
            var skipExStyle = IsStyleSensitiveExe(exePath);
            var state = new HiddenWindowState
            {
                Handle = hwnd,
                ExStyle = exStyle,
                WasVisible = true,
                WasMinimized = WinApi.IsIconic(hwnd),
                ExePath = exePath,
                SkipExStyle = skipExStyle
            };

            if (!skipExStyle)
            {
                var newStyle = exStyle | WinApi.WS_EX_TOOLWINDOW;
                newStyle &= ~WinApi.WS_EX_APPWINDOW;
                WinApi.SetWindowLongPtr(hwnd, WinApi.GWL_EXSTYLE, new IntPtr(newStyle));
                WinApi.SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    0,
                    0,
                    0,
                    0,
                    WinApi.SWP_NOMOVE | WinApi.SWP_NOSIZE | WinApi.SWP_NOZORDER | WinApi.SWP_FRAMECHANGED | WinApi.SWP_NOACTIVATE);
            }

            TaskbarHelper.HideTab(hwnd);
            state.UsedCloak = CloakWindow(hwnd, true);
            if (!state.UsedCloak)
            {
                WinApi.ShowWindow(hwnd, WinApi.SW_HIDE);
            }

            _hidden[hwnd] = state;
        }

        private static bool CloakWindow(IntPtr hwnd, bool cloak)
        {
            var value = cloak ? 1 : 0;
            return WinApi.DwmSetWindowAttribute(hwnd, WinApi.DWMWA_CLOAK, ref value, 4) == 0;
        }

        private static void RestoreWindow(HiddenWindowState state)
        {
            if (state == null || state.Handle == IntPtr.Zero || !WinApi.IsWindow(state.Handle))
            {
                return;
            }

            CloakWindow(state.Handle, false);
            if (!state.SkipExStyle)
            {
                WinApi.SetWindowLongPtr(state.Handle, WinApi.GWL_EXSTYLE, new IntPtr(state.ExStyle));
                WinApi.SetWindowPos(
                    state.Handle,
                    IntPtr.Zero,
                    0,
                    0,
                    0,
                    0,
                    WinApi.SWP_NOMOVE | WinApi.SWP_NOSIZE | WinApi.SWP_NOZORDER | WinApi.SWP_FRAMECHANGED | WinApi.SWP_NOACTIVATE);
            }

            TaskbarHelper.ShowTab(state.Handle);

            if (state.WasMinimized)
            {
                WinApi.ShowWindow(state.Handle, WinApi.SW_RESTORE);
            }
            else if (!state.UsedCloak)
            {
                WinApi.ShowWindow(state.Handle, WinApi.SW_SHOWNOACTIVATE);
            }

            if (state.SkipExStyle)
            {
                WinApi.InvalidateRect(state.Handle, IntPtr.Zero, false);
            }
            else
            {
                ForceRepaint(state.Handle);
            }
            WinApi.PostMessage(state.Handle, WindowRestoredMessage, IntPtr.Zero, IntPtr.Zero);
        }

        private static void RestoreWindowStyle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !WinApi.IsWindow(hwnd))
            {
                return;
            }

            CloakWindow(hwnd, false);
            var exStyle = WinApi.GetWindowLongInt(hwnd, WinApi.GWL_EXSTYLE);
            exStyle &= ~WinApi.WS_EX_TOOLWINDOW;
            WinApi.SetWindowLongPtr(hwnd, WinApi.GWL_EXSTYLE, new IntPtr(exStyle));
            WinApi.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                WinApi.SWP_NOMOVE | WinApi.SWP_NOSIZE | WinApi.SWP_NOZORDER | WinApi.SWP_FRAMECHANGED | WinApi.SWP_NOACTIVATE);

            if (!WinApi.IsWindowVisible(hwnd))
            {
                WinApi.ShowWindow(hwnd, WinApi.SW_SHOWNOACTIVATE);
            }

            ForceRepaint(hwnd);
        }

        private static void ForceRepaint(IntPtr hwnd)
        {
            var off = 0;
            WinApi.DwmSetWindowAttribute(hwnd, WinApi.DWMWA_DISALLOW_PEEK, ref off, 4);
            WinApi.DwmSetWindowAttribute(hwnd, WinApi.DWMWA_EXCLUDED_FROM_PEEK, ref off, 4);
            WinApi.DwmSetWindowAttribute(hwnd, WinApi.DWMWA_FORCE_ICONIC_REPRESENTATION, ref off, 4);
            WinApi.DwmInvalidateIconicBitmaps(hwnd);
            WinApi.InvalidateRect(hwnd, IntPtr.Zero, true);
            WinApi.RedrawWindow(
                hwnd,
                IntPtr.Zero,
                IntPtr.Zero,
                WinApi.RDW_INVALIDATE | WinApi.RDW_ERASE | WinApi.RDW_FRAME | WinApi.RDW_ALLCHILDREN | WinApi.RDW_UPDATENOW | WinApi.RDW_INTERNALPAINT);
            WinApi.UpdateWindow(hwnd);
            WinApi.SendMessage(hwnd, WinApi.WM_PAINT, IntPtr.Zero, IntPtr.Zero);
        }

        public void Dispose()
        {
            StopWatchers();
            _watchTimer.Dispose();
        }
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    internal class TaskbarList
    {
    }

    [ComImport]
    [Guid("56FDF342-FD6D-11d0-958A-006097C9A090")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITaskbarList
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
    }

    internal static class TaskbarHelper
    {
        private static ITaskbarList _list;
        private static bool _ready;

        private static ITaskbarList GetList()
        {
            if (_ready)
            {
                return _list;
            }

            _ready = true;
            try
            {
                var com = (ITaskbarList)new TaskbarList();
                com.HrInit();
                _list = com;
            }
            catch
            {
                _list = null;
            }

            return _list;
        }

        public static void HideTab(IntPtr hwnd)
        {
            try
            {
                var list = GetList();
                if (list != null)
                {
                    list.DeleteTab(hwnd);
                }
            }
            catch
            {
            }
        }

        public static void ShowTab(IntPtr hwnd)
        {
            try
            {
                var list = GetList();
                if (list != null)
                {
                    list.AddTab(hwnd);
                }
            }
            catch
            {
            }
        }
    }
}
