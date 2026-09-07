using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        public string ExePath { get; set; }
    }

    internal sealed class HideService : IDisposable
    {
        private readonly object _sync = new object();
        private readonly Dictionary<IntPtr, HiddenWindowState> _hidden = new Dictionary<IntPtr, HiddenWindowState>();
        private readonly HashSet<string> _targetExePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<uint> _targetProcessIds = new HashSet<uint>();
        private readonly Timer _watchTimer;
        private IntPtr _winEventHook = IntPtr.Zero;
        private WinApi.WinEventDelegate _winEventDelegate;
        private bool _isHiddenMode;
        private int _ownProcessId;

        public event Action<bool> HiddenModeChanged;

        public bool IsHiddenMode
        {
            get { return _isHiddenMode; }
        }

        public HideService()
        {
            _ownProcessId = Process.GetCurrentProcess().Id;
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
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        _targetExePaths.Add(normalized);
                    }
                }
            }
        }

        public void HideAll()
        {
            lock (_sync)
            {
                if (_targetExePaths.Count == 0)
                {
                    return;
                }

                RefreshTargetProcessIds();
                foreach (var hwnd in EnumerateTargetWindows(includeHidden: false))
                {
                    HideWindow(hwnd);
                }

                if (_hidden.Count == 0)
                {
                    return;
                }

                EnterHiddenMode();
            }
        }

        public void ShowAll()
        {
            lock (_sync)
            {
                ExitHiddenMode();

                foreach (var state in _hidden.Values.ToList())
                {
                    RestoreWindow(state);
                }

                _hidden.Clear();
            }
        }

        private void EnterHiddenMode()
        {
            if (_isHiddenMode)
            {
                return;
            }

            _isHiddenMode = true;
            StartWatchers();
            if (HiddenModeChanged != null)
            {
                HiddenModeChanged(true);
            }
        }

        private void ExitHiddenMode()
        {
            if (!_isHiddenMode)
            {
                return;
            }

            _isHiddenMode = false;
            StopWatchers();
            if (HiddenModeChanged != null)
            {
                HiddenModeChanged(false);
            }
        }

        private void StartWatchers()
        {
            _winEventDelegate = OnWinEvent;
            _winEventHook = WinApi.SetWinEventHook(
                WinApi.EVENT_OBJECT_SHOW,
                WinApi.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                WinApi.WINEVENT_OUTOFCONTEXT | WinApi.WINEVENT_SKIPOWNPROCESS);

            _watchTimer.Change(500, 500);
        }

        private void StopWatchers()
        {
            _watchTimer.Change(Timeout.Infinite, Timeout.Infinite);
            if (_winEventHook != IntPtr.Zero)
            {
                WinApi.UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }
        }

        private void OnWatchTimer(object state)
        {
            lock (_sync)
            {
                if (!_isHiddenMode)
                {
                    return;
                }

                RefreshTargetProcessIds();
                foreach (var hwnd in EnumerateTargetWindows(includeHidden: true))
                {
                    if (!_hidden.ContainsKey(hwnd))
                    {
                        HideWindow(hwnd);
                    }
                }
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (!_isHiddenMode || hwnd == IntPtr.Zero || idObject != 0 || idChild != 0)
            {
                return;
            }

            lock (_sync)
            {
                if (!_isHiddenMode || !IsTargetWindow(hwnd))
                {
                    return;
                }

                HideWindow(hwnd);
            }
        }

        private void RefreshTargetProcessIds()
        {
            _targetProcessIds.Clear();
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var path = ConfigStore.NormalizeExePath(process.MainModule.FileName);
                    if (_targetExePaths.Contains(path))
                    {
                        _targetProcessIds.Add((uint)process.Id);
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private IEnumerable<IntPtr> EnumerateTargetWindows(bool includeHidden)
        {
            var result = new List<IntPtr>();
            WinApi.EnumWindows((hwnd, lParam) =>
            {
                if (!IsTargetWindow(hwnd, includeHidden))
                {
                    return true;
                }

                result.Add(hwnd);
                return true;
            }, IntPtr.Zero);

            return result;
        }

        private bool IsTargetWindow(IntPtr hwnd, bool includeHidden = false)
        {
            if (hwnd == IntPtr.Zero || !WinApi.IsWindow(hwnd))
            {
                return false;
            }

            uint processId;
            WinApi.GetWindowThreadProcessId(hwnd, out processId);
            if (processId == 0 || processId == _ownProcessId)
            {
                return false;
            }

            if (!includeHidden && !WinApi.IsWindowVisible(hwnd))
            {
                return false;
            }

            if (GetWindowTitleLength(hwnd) == 0)
            {
                return false;
            }

            var exePath = GetProcessExePath(processId);
            return !string.IsNullOrEmpty(exePath) && _targetExePaths.Contains(exePath);
        }

        private static int GetWindowTitleLength(IntPtr hwnd)
        {
            return WinApi.GetWindowTextLength(hwnd);
        }

        private static string GetProcessExePath(uint processId)
        {
            try
            {
                using (var process = Process.GetProcessById((int)processId))
                {
                    return ConfigStore.NormalizeExePath(process.MainModule.FileName);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private void HideWindow(IntPtr hwnd)
        {
            if (_hidden.ContainsKey(hwnd))
            {
                return;
            }

            uint processId;
            WinApi.GetWindowThreadProcessId(hwnd, out processId);
            var exePath = GetProcessExePath(processId);

            var exStyle = WinApi.GetWindowLongPtr(hwnd, WinApi.GWL_EXSTYLE).ToInt32();
            var state = new HiddenWindowState
            {
                Handle = hwnd,
                ExStyle = exStyle,
                WasVisible = WinApi.IsWindowVisible(hwnd),
                WasMinimized = WinApi.IsIconic(hwnd),
                ExePath = exePath
            };

            var newStyle = exStyle | WinApi.WS_EX_TOOLWINDOW;
            newStyle &= ~WinApi.WS_EX_APPWINDOW;
            WinApi.SetWindowLongPtr(hwnd, WinApi.GWL_EXSTYLE, new IntPtr(newStyle));
            WinApi.ShowWindow(hwnd, WinApi.SW_HIDE);

            _hidden[hwnd] = state;
        }

        private void RestoreWindow(HiddenWindowState state)
        {
            if (state == null || state.Handle == IntPtr.Zero || !WinApi.IsWindow(state.Handle))
            {
                return;
            }

            WinApi.SetWindowLongPtr(state.Handle, WinApi.GWL_EXSTYLE, new IntPtr(state.ExStyle));

            if (state.WasMinimized)
            {
                WinApi.ShowWindow(state.Handle, WinApi.SW_RESTORE);
            }
            else if (state.WasVisible)
            {
                WinApi.ShowWindow(state.Handle, WinApi.SW_SHOW);
            }
            else
            {
                WinApi.ShowWindow(state.Handle, WinApi.SW_HIDE);
            }
        }

        public void Dispose()
        {
            StopWatchers();
            _watchTimer.Dispose();
        }
    }
}
