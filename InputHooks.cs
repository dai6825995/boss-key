using System;
using System.Windows.Forms;

namespace BossKey
{
    internal sealed class InputHooks : IDisposable
    {
        public const int HotkeyHideId = 1;
        public const int HotkeyShowId = 2;

        private readonly Form _messageWindow;
        private readonly HideService _hideService;
        private readonly Func<bool> _isSettingsVisible;
        private IntPtr _mouseHook = IntPtr.Zero;
        private WinApi.LowLevelMouseProc _mouseProc;
        private AppConfig _config;

        public event Action HideRequested;
        public event Action ShowRequested;

        public InputHooks(Form messageWindow, HideService hideService, Func<bool> isSettingsVisible)
        {
            _messageWindow = messageWindow;
            _hideService = hideService;
            _isSettingsVisible = isSettingsVisible;
            _config = new AppConfig();
        }

        public void ApplyConfig(AppConfig config)
        {
            _config = config ?? new AppConfig();
            UnregisterHotkeys();
            RegisterHotkeys();
            UpdateMouseHook();
        }

        private void RegisterHotkeys()
        {
            if (_messageWindow.Handle == IntPtr.Zero)
            {
                return;
            }

            WinApi.RegisterHotKey(
                _messageWindow.Handle,
                HotkeyHideId,
                _config.HideHotkey.Modifiers,
                _config.HideHotkey.VirtualKey);

            WinApi.RegisterHotKey(
                _messageWindow.Handle,
                HotkeyShowId,
                _config.ShowHotkey.Modifiers,
                _config.ShowHotkey.VirtualKey);
        }

        private void UnregisterHotkeys()
        {
            if (_messageWindow.Handle == IntPtr.Zero)
            {
                return;
            }

            WinApi.UnregisterHotKey(_messageWindow.Handle, HotkeyHideId);
            WinApi.UnregisterHotKey(_messageWindow.Handle, HotkeyShowId);
        }

        private void UpdateMouseHook()
        {
            if (_config.MouseMiddle || _config.MouseX1 || _config.MouseX2)
            {
                InstallMouseHook();
            }
            else
            {
                RemoveMouseHook();
            }
        }

        private void InstallMouseHook()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                return;
            }

            _mouseProc = MouseHookCallback;
            _mouseHook = WinApi.SetWindowsHookEx(
                WinApi.WH_MOUSE_LL,
                _mouseProc,
                WinApi.GetModuleHandle(null),
                0);
        }

        private void RemoveMouseHook()
        {
            if (_mouseHook == IntPtr.Zero)
            {
                return;
            }

            WinApi.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
            _mouseProc = null;
        }

        public bool ProcessHotkeyMessage(Message m)
        {
            if (m.Msg != WinApi.WM_HOTKEY)
            {
                return false;
            }

            var id = m.WParam.ToInt32();
            if (id == HotkeyHideId)
            {
                TriggerHide();
                return true;
            }

            if (id == HotkeyShowId)
            {
                TriggerShow();
                return true;
            }

            return false;
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !IsSettingsVisible())
            {
                var message = wParam.ToInt32();
                if (message == WinApi.WM_MBUTTONDOWN && _config.MouseMiddle)
                {
                    TriggerHide();
                }
                else if (message == WinApi.WM_XBUTTONDOWN)
                {
                    var hookStruct = (WinApi.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                        lParam,
                        typeof(WinApi.MSLLHOOKSTRUCT));
                    var button = hookStruct.mouseData >> 16;
                    if (_config.MouseX1 && button == WinApi.XBUTTON1)
                    {
                        TriggerHide();
                    }
                    else if (_config.MouseX2 && button == WinApi.XBUTTON2)
                    {
                        TriggerHide();
                    }
                }
            }

            return WinApi.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private bool IsSettingsVisible()
        {
            return _isSettingsVisible != null && _isSettingsVisible();
        }

        private void TriggerHide()
        {
            if (HideRequested != null)
            {
                HideRequested();
            }
        }

        private void TriggerShow()
        {
            if (ShowRequested != null)
            {
                ShowRequested();
            }
        }

        public void Dispose()
        {
            UnregisterHotkeys();
            RemoveMouseHook();
        }
    }
}
