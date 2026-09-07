using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BossKey
{
    internal sealed class WindowFxState
    {
        public int OriginalExStyle { get; set; }
        public byte OriginalAlpha { get; set; }
        public bool AddedLayered { get; set; }
        public bool UsedComposition { get; set; }
        public byte CurrentAlpha { get; set; }
        public bool ChangedTopmost { get; set; }
        public bool WasTopmost { get; set; }
    }

    internal sealed class WindowFx
    {
        private readonly Dictionary<IntPtr, WindowFxState> _states = new Dictionary<IntPtr, WindowFxState>();
        private readonly uint _selfPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        public string ToggleTopmost()
        {
            var hwnd = GetTargetWindow();
            if (hwnd == IntPtr.Zero)
            {
                return "没有可操作的窗口";
            }

            var state = EnsureState(hwnd);
            var exStyle = WinApi.GetWindowLongInt(hwnd, WinApi.GWL_EXSTYLE);
            var isTop = (exStyle & WinApi.WS_EX_TOPMOST) != 0;
            var insertAfter = isTop ? WinApi.HWND_NOTOPMOST : WinApi.HWND_TOPMOST;

            WinApi.SetWindowPos(
                hwnd,
                insertAfter,
                0,
                0,
                0,
                0,
                WinApi.SWP_NOMOVE | WinApi.SWP_NOSIZE | WinApi.SWP_NOACTIVATE);

            state.ChangedTopmost = true;
            return isTop ? "已取消置顶" : "已置顶当前窗口";
        }

        public string AdjustOpacity(int delta)
        {
            var hwnd = GetTargetWindow();
            if (hwnd == IntPtr.Zero)
            {
                return "没有可操作的窗口";
            }

            uint processId;
            WinApi.GetWindowThreadProcessId(hwnd, out processId);
            var exe = GetProcessName(processId);
            if (IsUuFamily(exe))
            {
                return "UU远程不支持外部调透明度";
            }

            if (ShouldSkipOpacity(hwnd, exe))
            {
                return "当前窗口不支持透明度";
            }

            var windows = new List<IntPtr>();
            AddUnique(windows, hwnd);
            if (IsCameraApp(exe))
            {
                CollectLargeChildren(hwnd, processId, windows);
            }

            byte current = 255;
            for (var i = 0; i < windows.Count; i++)
            {
                if (_states.ContainsKey(windows[i]))
                {
                    current = _states[windows[i]].CurrentAlpha;
                    break;
                }
            }

            var next = current + delta;
            if (next < 40)
            {
                next = 40;
            }
            if (next > 255)
            {
                next = 255;
            }

            for (var i = 0; i < windows.Count; i++)
            {
                var state = EnsureState(windows[i]);
                ApplyAlpha(windows[i], state, (byte)next);
                state.CurrentAlpha = (byte)next;
            }

            return "透明度 " + (int)Math.Round((255 - next) * 100.0 / 255.0) + "%";
        }

        public string RestoreOpacity()
        {
            var hwnd = GetTargetWindow();
            if (hwnd == IntPtr.Zero)
            {
                return "没有可操作的窗口";
            }

            uint processId;
            WinApi.GetWindowThreadProcessId(hwnd, out processId);
            RestoreOpacity(hwnd);
            CollectLargeChildren(hwnd, processId, new List<IntPtr>());
            var children = new List<IntPtr>();
            CollectLargeChildren(hwnd, processId, children);
            for (var i = 0; i < children.Count; i++)
            {
                RestoreOpacity(children[i]);
            }

            return "已恢复当前窗口透明度";
        }

        public void RestoreAll()
        {
            var handles = new List<IntPtr>(_states.Keys);
            foreach (var hwnd in handles)
            {
                RestoreAll(hwnd);
            }
            _states.Clear();
            RepairDamagedSurfaces();
            ClearUuOpacity();
        }

        public void RepairDamagedSurfaces()
        {
            WinApi.EnumWindows((hwnd, lParam) =>
            {
                if (!WinApi.IsWindow(hwnd))
                {
                    return true;
                }

                uint processId;
                WinApi.GetWindowThreadProcessId(hwnd, out processId);
                var exe = GetProcessName(processId);
                if (ShouldSkipOpacity(hwnd, exe) && !IsUuFamily(exe))
                {
                    RepairOneSurface(hwnd);
                    WinApi.EnumChildWindows(hwnd, (child, lp) =>
                    {
                        RepairOneSurface(child);
                        return true;
                    }, IntPtr.Zero);
                }

                return true;
            }, IntPtr.Zero);
        }

        public void ClearUuOpacity()
        {
            WinApi.EnumWindows((hwnd, lParam) =>
            {
                uint processId;
                WinApi.GetWindowThreadProcessId(hwnd, out processId);
                if (!IsUuFamily(GetProcessName(processId)))
                {
                    return true;
                }

                ResetGpuOpacity(hwnd);
                WinApi.EnumChildWindows(hwnd, (child, lp) =>
                {
                    ResetGpuOpacity(child);
                    return true;
                }, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }

        private static void ResetGpuOpacity(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !WinApi.IsWindow(hwnd))
            {
                return;
            }

            var dummy = new WindowFxState();
            ApplyComposition(hwnd, dummy, 255);
            var exStyle = WinApi.GetWindowLongInt(hwnd, WinApi.GWL_EXSTYLE);
            if ((exStyle & WinApi.WS_EX_LAYERED) != 0)
            {
                WinApi.SetLayeredWindowAttributes(hwnd, 0, 255, WinApi.LWA_ALPHA);
            }
        }

        private void RestoreOpacity(IntPtr hwnd)
        {
            if (!WinApi.IsWindow(hwnd))
            {
                return;
            }

            WindowFxState state;
            if (!_states.TryGetValue(hwnd, out state))
            {
                ApplyAlpha(hwnd, EnsureState(hwnd), 255);
                return;
            }

            ApplyAlpha(hwnd, state, 255);
            state.CurrentAlpha = 255;

            if (state.AddedLayered)
            {
                WinApi.SetWindowLongPtr(hwnd, WinApi.GWL_EXSTYLE, new IntPtr(state.OriginalExStyle));
                WinApi.SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    0,
                    0,
                    0,
                    0,
                    WinApi.SWP_NOMOVE | WinApi.SWP_NOSIZE | WinApi.SWP_NOZORDER | WinApi.SWP_NOACTIVATE);
                state.AddedLayered = false;
            }
        }

        private void RestoreAll(IntPtr hwnd)
        {
            if (!WinApi.IsWindow(hwnd) || !_states.ContainsKey(hwnd))
            {
                return;
            }

            var state = _states[hwnd];
            RestoreOpacity(hwnd);

            if (state.ChangedTopmost)
            {
                WinApi.SetWindowPos(
                    hwnd,
                    state.WasTopmost ? WinApi.HWND_TOPMOST : WinApi.HWND_NOTOPMOST,
                    0,
                    0,
                    0,
                    0,
                    WinApi.SWP_NOMOVE | WinApi.SWP_NOSIZE | WinApi.SWP_NOACTIVATE);
            }
        }

        private WindowFxState EnsureState(IntPtr hwnd)
        {
            if (_states.ContainsKey(hwnd))
            {
                return _states[hwnd];
            }

            var exStyle = WinApi.GetWindowLongInt(hwnd, WinApi.GWL_EXSTYLE);
            byte alpha = 255;
            if ((exStyle & WinApi.WS_EX_LAYERED) != 0)
            {
                uint key;
                uint flags;
                if (!WinApi.GetLayeredWindowAttributes(hwnd, out key, out alpha, out flags) || alpha == 0)
                {
                    alpha = 255;
                }
            }

            var state = new WindowFxState
            {
                OriginalExStyle = exStyle,
                OriginalAlpha = alpha,
                CurrentAlpha = 255,
                WasTopmost = (exStyle & WinApi.WS_EX_TOPMOST) != 0
            };
            _states[hwnd] = state;
            return state;
        }

        private static void ApplyAlpha(IntPtr hwnd, WindowFxState state, byte alpha)
        {
            uint processId;
            WinApi.GetWindowThreadProcessId(hwnd, out processId);
            var exe = GetProcessName(processId);
            if (ShouldSkipOpacity(hwnd, exe))
            {
                return;
            }

            var exStyle = WinApi.GetWindowLongInt(hwnd, WinApi.GWL_EXSTYLE);
            if ((exStyle & WinApi.WS_EX_NOREDIRECTIONBITMAP) != 0)
            {
                return;
            }

            EnsureLayered(hwnd, state);
            WinApi.SetLayeredWindowAttributes(hwnd, 0, alpha, WinApi.LWA_ALPHA);

            if (alpha >= 255 && state.UsedComposition)
            {
                ApplyComposition(hwnd, state, 255);
            }
        }

        private static void EnsureLayered(IntPtr hwnd, WindowFxState state)
        {
            var exStyle = WinApi.GetWindowLongInt(hwnd, WinApi.GWL_EXSTYLE);
            if ((exStyle & WinApi.WS_EX_LAYERED) == 0)
            {
                WinApi.SetWindowLongPtr(hwnd, WinApi.GWL_EXSTYLE, new IntPtr(exStyle | WinApi.WS_EX_LAYERED));
                state.AddedLayered = true;
                WinApi.SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    0,
                    0,
                    0,
                    0,
                    WinApi.SWP_NOMOVE | WinApi.SWP_NOSIZE | WinApi.SWP_NOZORDER | WinApi.SWP_FRAMECHANGED | WinApi.SWP_NOACTIVATE);
            }
        }

        private static void ApplyComposition(IntPtr hwnd, WindowFxState state, byte alpha)
        {
            var policy = new WinApi.AccentPolicy();
            if (alpha >= 255)
            {
                policy.AccentState = WinApi.ACCENT_DISABLED;
                policy.AccentFlags = 0;
                policy.GradientColor = 0;
            }
            else
            {
                policy.AccentState = WinApi.ACCENT_ENABLE_TRANSPARENTGRADIENT;
                policy.AccentFlags = 2;
                policy.GradientColor = unchecked((int)((uint)alpha << 24));
            }

            var size = Marshal.SizeOf(typeof(WinApi.AccentPolicy));
            var policyPtr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, policyPtr, false);
                var data = new WinApi.WindowCompositionAttributeData();
                data.Attribute = WinApi.WCA_ACCENT_POLICY;
                data.Data = policyPtr;
                data.SizeOfData = size;
                WinApi.SetWindowCompositionAttribute(hwnd, ref data);
                state.UsedComposition = alpha < 255;
            }
            catch
            {
            }
            finally
            {
                Marshal.FreeHGlobal(policyPtr);
            }
        }

        private static void RepairOneSurface(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !WinApi.IsWindow(hwnd))
            {
                return;
            }

            var className = new StringBuilder(256);
            WinApi.GetClassName(hwnd, className, className.Capacity);
            var name = className.ToString();
            var chrome = name.IndexOf("Chrome_", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("CEF", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Electron", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!chrome)
            {
                return;
            }

            var exStyle = WinApi.GetWindowLongInt(hwnd, WinApi.GWL_EXSTYLE);
            var repaired = exStyle;
            repaired |= WinApi.WS_EX_NOREDIRECTIONBITMAP;
            repaired &= ~WinApi.WS_EX_LAYERED;
            if (repaired != exStyle)
            {
                WinApi.SetWindowLongPtr(hwnd, WinApi.GWL_EXSTYLE, new IntPtr(repaired));
                WinApi.SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    0,
                    0,
                    0,
                    0,
                    WinApi.SWP_NOMOVE | WinApi.SWP_NOSIZE | WinApi.SWP_NOZORDER | WinApi.SWP_NOACTIVATE);
            }

            var margins = new WinApi.MARGINS();
            try
            {
                WinApi.DwmExtendFrameIntoClientArea(hwnd, ref margins);
            }
            catch
            {
            }

            var dummy = new WindowFxState();
            ApplyComposition(hwnd, dummy, 255);
        }

        private IntPtr GetTargetWindow()
        {
            var hwnd = GetWindowUnderCursor();
            if (!IsAdjustableWindow(hwnd))
            {
                hwnd = WinApi.GetForegroundWindow();
            }

            if (!IsAdjustableWindow(hwnd))
            {
                return IntPtr.Zero;
            }

            return hwnd;
        }

        private static IntPtr GetWindowUnderCursor()
        {
            WinApi.POINT pt;
            if (!WinApi.GetCursorPos(out pt))
            {
                return IntPtr.Zero;
            }

            var hit = WinApi.WindowFromPoint(pt);
            if (hit == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var root = WinApi.GetAncestor(hit, WinApi.GA_ROOT);
            return root != IntPtr.Zero ? root : hit;
        }

        private static bool IsSizableWindow(IntPtr hwnd)
        {
            WinApi.RECT rect;
            if (!WinApi.GetWindowRect(hwnd, out rect))
            {
                return false;
            }

            return rect.Right - rect.Left >= 80 && rect.Bottom - rect.Top >= 80;
        }

        private static void AddUnique(List<IntPtr> list, IntPtr hwnd)
        {
            if (!list.Contains(hwnd))
            {
                list.Add(hwnd);
            }
        }

        private static void CollectLargeChildren(IntPtr parent, uint processId, List<IntPtr> result)
        {
            WinApi.RECT parentRect;
            if (!WinApi.GetWindowRect(parent, out parentRect))
            {
                return;
            }

            var parentW = parentRect.Right - parentRect.Left;
            var parentH = parentRect.Bottom - parentRect.Top;
            WinApi.EnumChildWindows(parent, (hwnd, lParam) =>
            {
                uint pid;
                WinApi.GetWindowThreadProcessId(hwnd, out pid);
                if (pid != processId || !IsSizableWindow(hwnd))
                {
                    return true;
                }

                WinApi.RECT rect;
                if (!WinApi.GetWindowRect(hwnd, out rect))
                {
                    return true;
                }

                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;
                if (width >= parentW / 2 && height >= parentH * 2 / 5)
                {
                    AddUnique(result, hwnd);
                }

                return true;
            }, IntPtr.Zero);
        }

        private static bool IsUuFamily(string exeName)
        {
            if (string.IsNullOrEmpty(exeName))
            {
                return false;
            }

            return string.Equals(exeName, "gameviewer.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(exeName, "uu.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(exeName, "gameviewerserver.exe", StringComparison.OrdinalIgnoreCase)
                || exeName.IndexOf("gameviewer", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldSkipOpacity(IntPtr hwnd, string exeName)
        {
            if (IsCameraApp(exeName))
            {
                return false;
            }

            if (IsUuFamily(exeName) || IsFragileGpuApp(exeName))
            {
                return true;
            }

            var exStyle = WinApi.GetWindowLongInt(hwnd, WinApi.GWL_EXSTYLE);
            return (exStyle & WinApi.WS_EX_NOREDIRECTIONBITMAP) != 0;
        }

        private static bool IsCameraApp(string exeName)
        {
            return !string.IsNullOrEmpty(exeName)
                && exeName.IndexOf("摄像头", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsFragileGpuApp(string exeName)
        {
            if (string.IsNullOrEmpty(exeName))
            {
                return false;
            }

            return string.Equals(exeName, "Cursor.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(exeName, "Code.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(exeName, "chrome.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(exeName, "msedge.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(exeName, "devenv.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetProcessName(uint processId)
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

                return System.IO.Path.GetFileName(buffer.ToString());
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

        private bool IsAdjustableWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !WinApi.IsWindow(hwnd))
            {
                return false;
            }

            if (hwnd == WinApi.GetShellWindow() || hwnd == WinApi.GetDesktopWindow())
            {
                return false;
            }

            var className = new StringBuilder(256);
            WinApi.GetClassName(hwnd, className, className.Capacity);
            var name = className.ToString();
            if (name == "Progman" || name == "WorkerW" || name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd")
            {
                return false;
            }

            uint processId;
            WinApi.GetWindowThreadProcessId(hwnd, out processId);
            if (processId == 0 || processId == _selfPid)
            {
                return false;
            }

            int cloaked;
            if (WinApi.DwmGetWindowAttribute(hwnd, WinApi.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0)
            {
                return false;
            }

            return true;
        }
    }
}
