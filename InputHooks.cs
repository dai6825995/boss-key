using System;
using System.Threading;
using System.Windows.Forms;

namespace BossKey
{
    internal sealed class InputHooks : IDisposable
    {
        public const int HotkeyHideId = 1;
        public const int HotkeyShowId = 2;
        public const int HotkeyTopmostId = 3;
        public const int HotkeyOpacityDownId = 4;
        public const int HotkeyOpacityUpId = 5;
        public const int HotkeyOpacityRestoreId = 6;
        public const int HotkeySettingsId = 8;

        private readonly Form _messageWindow;
        private readonly HideService _hideService;
        private readonly Func<bool> _isSettingsVisible;
        private readonly System.Windows.Forms.Timer _pollTimer;
        private readonly Thread _hookThread;
        private IntPtr _mouseHook = IntPtr.Zero;
        private IntPtr _keyboardHook = IntPtr.Zero;
        private WinApi.LowLevelMouseProc _mouseProc;
        private WinApi.LowLevelKeyboardProc _keyboardProc;
        private AppConfig _config;
        private bool _hideHeld;
        private bool _showHeld;
        private bool _topmostHeld;
        private bool _opacityDownHeld;
        private bool _opacityUpHeld;
        private bool _opacityRestoreHeld;
        private bool _settingsHeld;
        private bool _middleHeld;
        private bool _x1Held;
        private bool _x2Held;
        private readonly RawInputSink _rawInput;
        private volatile bool _hookThreadStop;
        private int _hookThreadId;
        private IntPtr _hookPumpWnd = IntPtr.Zero;

        public event Action HideRequested;
        public event Action ShowRequested;
        public event Action TopmostRequested;
        public event Action OpacityDownRequested;
        public event Action OpacityUpRequested;
        public event Action OpacityRestoreRequested;
        public event Action SettingsRequested;

        public InputHooks(Form messageWindow, HideService hideService, Func<bool> isSettingsVisible)
        {
            _messageWindow = messageWindow;
            _hideService = hideService;
            _isSettingsVisible = isSettingsVisible;
            _config = new AppConfig();
            _rawInput = new RawInputSink();
            _rawInput.KeyDown += OnRawKeyDown;
            _rawInput.MouseButtonDown += OnRawMouseButton;

            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = 30;
            _pollTimer.Tick += OnPollTick;
            _pollTimer.Start();

            _hookThread = new Thread(HookThreadMain);
            _hookThread.Name = "BossKeyHooks";
            _hookThread.IsBackground = true;
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();
        }

        public void ApplyConfig(AppConfig config)
        {
            _config = config ?? new AppConfig();
            UnregisterHotkeys();
            RegisterHotkeys();
            _rawInput.Attach(_messageWindow, _config);
        }

        public void Rebind()
        {
            ApplyConfig(_config);
        }

        private void RegisterHotkeys()
        {
            if (_messageWindow.Handle == IntPtr.Zero)
            {
                return;
            }

            RegisterOne(HotkeyHideId, _config.HideHotkey);
            RegisterOne(HotkeyShowId, _config.ShowHotkey);
            RegisterOne(HotkeyTopmostId, _config.TopmostHotkey);
            RegisterOne(HotkeyOpacityDownId, _config.OpacityDownHotkey);
            RegisterOne(HotkeyOpacityUpId, _config.OpacityUpHotkey);
            RegisterOne(HotkeyOpacityRestoreId, _config.OpacityRestoreHotkey);
            RegisterOne(HotkeySettingsId, _config.SettingsHotkey);
        }

        private void RegisterOne(int id, HotkeyConfig hotkey)
        {
            if (hotkey == null || hotkey.VirtualKey == 0)
            {
                return;
            }

            if (!WinApi.RegisterHotKey(_messageWindow.Handle, id, hotkey.Modifiers | WinApi.MOD_NOREPEAT, hotkey.VirtualKey))
            {
                WinApi.RegisterHotKey(_messageWindow.Handle, id, hotkey.Modifiers, hotkey.VirtualKey);
            }
        }

        private void UnregisterHotkeys()
        {
            if (_messageWindow.Handle == IntPtr.Zero)
            {
                return;
            }

            WinApi.UnregisterHotKey(_messageWindow.Handle, HotkeyHideId);
            WinApi.UnregisterHotKey(_messageWindow.Handle, HotkeyShowId);
            WinApi.UnregisterHotKey(_messageWindow.Handle, HotkeyTopmostId);
            WinApi.UnregisterHotKey(_messageWindow.Handle, HotkeyOpacityDownId);
            WinApi.UnregisterHotKey(_messageWindow.Handle, HotkeyOpacityUpId);
            WinApi.UnregisterHotKey(_messageWindow.Handle, HotkeyOpacityRestoreId);
            WinApi.UnregisterHotKey(_messageWindow.Handle, HotkeySettingsId);
        }

        private void HookThreadMain()
        {
            _hookThreadId = WinApi.GetCurrentThreadId();
            _keyboardProc = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;

            _hookPumpWnd = WinApi.CreateWindowEx(
                0,
                "STATIC",
                "BossKeyHookPump",
                0,
                0,
                0,
                0,
                0,
                WinApi.HWND_MESSAGE,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            InstallKeyboardHook();
            InstallMouseHook();
            if (_hookPumpWnd != IntPtr.Zero)
            {
                WinApi.SetTimer(_hookPumpWnd, new UIntPtr(1), 180, IntPtr.Zero);
                _rawInput.AttachHandle(_hookPumpWnd, _config);
            }

            WinApi.MSG msg;
            while (!_hookThreadStop && WinApi.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WinApi.WM_TIMER)
                {
                    ReinstallKeyboardHook();
                }
                else if (msg.message == WinApi.WM_INPUT)
                {
                    _rawInput.Process(msg.lParam);
                }

                WinApi.TranslateMessage(ref msg);
                WinApi.DispatchMessage(ref msg);
            }

            if (_hookPumpWnd != IntPtr.Zero)
            {
                WinApi.KillTimer(_hookPumpWnd, new UIntPtr(1));
                WinApi.DestroyWindow(_hookPumpWnd);
                _hookPumpWnd = IntPtr.Zero;
            }

            RemoveMouseHook();
            RemoveKeyboardHook();
        }

        private void ReinstallKeyboardHook()
        {
            RemoveKeyboardHook();
            InstallKeyboardHook();
        }

        private void InstallMouseHook()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                return;
            }

            _mouseHook = WinApi.SetWindowsHookEx(
                WinApi.WH_MOUSE_LL,
                _mouseProc,
                IntPtr.Zero,
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
        }

        private void InstallKeyboardHook()
        {
            if (_keyboardHook != IntPtr.Zero)
            {
                return;
            }

            _keyboardHook = WinApi.SetWindowsHookEx(
                WinApi.WH_KEYBOARD_LL,
                _keyboardProc,
                IntPtr.Zero,
                0);
        }

        private void RemoveKeyboardHook()
        {
            if (_keyboardHook == IntPtr.Zero)
            {
                return;
            }

            WinApi.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        public bool ProcessHotkeyMessage(Message m)
        {
            if (m.Msg == WinApi.WM_INPUT)
            {
                _rawInput.Process(m.LParam);
                return false;
            }

            if (m.Msg != WinApi.WM_HOTKEY)
            {
                return false;
            }

            return DispatchHotkeyId(m.WParam.ToInt32());
        }

        private bool DispatchHotkeyId(int id)
        {
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

            if (id == HotkeyTopmostId)
            {
                Raise(TopmostRequested);
                return true;
            }

            if (id == HotkeyOpacityDownId)
            {
                Raise(OpacityDownRequested);
                return true;
            }

            if (id == HotkeyOpacityUpId)
            {
                Raise(OpacityUpRequested);
                return true;
            }

            if (id == HotkeyOpacityRestoreId)
            {
                Raise(OpacityRestoreRequested);
                return true;
            }

            if (id == HotkeySettingsId)
            {
                Raise(SettingsRequested);
                return true;
            }

            return false;
        }

        private static void Raise(Action handler)
        {
            if (handler != null)
            {
                handler();
            }
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var message = wParam.ToInt32();
                if (message == WinApi.WM_KEYDOWN || message == WinApi.WM_SYSKEYDOWN)
                {
                    var info = (WinApi.KBDLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                        lParam,
                        typeof(WinApi.KBDLLHOOKSTRUCT));
                    if (MatchesHotkey(_config.HideHotkey, info.vkCode))
                    {
                        PostHide();
                        return (IntPtr)1;
                    }
                    if (MatchesHotkey(_config.ShowHotkey, info.vkCode))
                    {
                        PostShow();
                        return (IntPtr)1;
                    }
                    if (MatchesHotkey(_config.TopmostHotkey, info.vkCode))
                    {
                        PostAction(TopmostRequested);
                        return (IntPtr)1;
                    }
                    if (MatchesHotkey(_config.OpacityDownHotkey, info.vkCode))
                    {
                        PostAction(OpacityDownRequested);
                        return (IntPtr)1;
                    }
                    if (MatchesHotkey(_config.OpacityUpHotkey, info.vkCode))
                    {
                        PostAction(OpacityUpRequested);
                        return (IntPtr)1;
                    }
                    if (MatchesHotkey(_config.OpacityRestoreHotkey, info.vkCode))
                    {
                        PostAction(OpacityRestoreRequested);
                        return (IntPtr)1;
                    }
                    if (MatchesHotkey(_config.SettingsHotkey, info.vkCode))
                    {
                        PostAction(SettingsRequested);
                        return (IntPtr)1;
                    }
                }
            }

            return WinApi.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var message = wParam.ToInt32();
                var ctrlAlt = IsCtrlDown() && IsAltDown();
                if (ctrlAlt && message == WinApi.WM_MOUSEWHEEL)
                {
                    var hookStruct = (WinApi.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                        lParam,
                        typeof(WinApi.MSLLHOOKSTRUCT));
                    var delta = (short)((hookStruct.mouseData >> 16) & 0xFFFF);
                    if (delta < 0)
                    {
                        PostAction(OpacityDownRequested);
                    }
                    else if (delta > 0)
                    {
                        PostAction(OpacityUpRequested);
                    }

                    return (IntPtr)1;
                }

                if (ctrlAlt && message == WinApi.WM_MBUTTONDOWN)
                {
                    PostAction(TopmostRequested);
                    return (IntPtr)1;
                }

                if (!ShouldIgnoreMouseHide())
                {
                    if (message == WinApi.WM_MBUTTONDOWN && _config.MouseMiddle)
                    {
                        PostHide();
                        return (IntPtr)1;
                    }

                    if (message == WinApi.WM_XBUTTONDOWN)
                    {
                        var hookStruct = (WinApi.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                            lParam,
                            typeof(WinApi.MSLLHOOKSTRUCT));
                        var button = hookStruct.mouseData >> 16;
                        if (_config.MouseX1 && button == WinApi.XBUTTON1)
                        {
                            PostHide();
                            return (IntPtr)1;
                        }
                        if (_config.MouseX2 && button == WinApi.XBUTTON2)
                        {
                            PostHide();
                            return (IntPtr)1;
                        }
                    }
                }
            }

            return WinApi.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private void OnPollTick(object sender, EventArgs e)
        {
            PollHotkey(_config.HideHotkey, ref _hideHeld, new Action(PostHide));
            PollHotkey(_config.ShowHotkey, ref _showHeld, new Action(PostShow));
            PollHotkey(_config.TopmostHotkey, ref _topmostHeld, new Action(delegate { PostAction(TopmostRequested); }));
            PollHotkey(_config.OpacityDownHotkey, ref _opacityDownHeld, new Action(delegate { PostAction(OpacityDownRequested); }));
            PollHotkey(_config.OpacityUpHotkey, ref _opacityUpHeld, new Action(delegate { PostAction(OpacityUpRequested); }));
            PollHotkey(_config.OpacityRestoreHotkey, ref _opacityRestoreHeld, new Action(delegate { PostAction(OpacityRestoreRequested); }));
            PollHotkey(_config.SettingsHotkey, ref _settingsHeld, new Action(delegate { PostAction(SettingsRequested); }));

            if (!ShouldIgnoreMouseHide())
            {
                PollMouseButton(WinApi.VK_MBUTTON, _config.MouseMiddle, ref _middleHeld);
                PollMouseButton(WinApi.VK_XBUTTON1_KEY, _config.MouseX1, ref _x1Held);
                PollMouseButton(WinApi.VK_XBUTTON2_KEY, _config.MouseX2, ref _x2Held);
            }
        }

        private void PollHotkey(HotkeyConfig hotkey, ref bool held, Action action)
        {
            var down = IsHotkeyDown(hotkey);
            if (down && !held)
            {
                action();
            }
            held = down;
        }

        private void PollMouseButton(int vk, bool enabled, ref bool held)
        {
            if (!enabled)
            {
                held = false;
                return;
            }

            var down = IsKeyDown(vk);
            if (down && !held)
            {
                PostHide();
            }
            held = down;
        }

        private static bool IsHotkeyDown(HotkeyConfig hotkey)
        {
            if (hotkey == null || hotkey.VirtualKey == 0)
            {
                return false;
            }

            return IsKeyDown((int)hotkey.VirtualKey) && RequiredModifiersDown(hotkey);
        }

        private static bool MatchesHotkey(HotkeyConfig hotkey, uint vkCode)
        {
            if (hotkey == null || hotkey.VirtualKey == 0 || vkCode != hotkey.VirtualKey)
            {
                return false;
            }

            return RequiredModifiersDown(hotkey);
        }

        private static bool RequiredModifiersDown(HotkeyConfig hotkey)
        {
            if ((hotkey.Modifiers & 0x0002) != 0 && !IsCtrlDown())
            {
                return false;
            }

            if ((hotkey.Modifiers & 0x0004) != 0 && !IsShiftDown())
            {
                return false;
            }

            if ((hotkey.Modifiers & 0x0001) != 0 && !IsAltDown())
            {
                return false;
            }

            if ((hotkey.Modifiers & 0x0008) != 0 && !IsWinDown())
            {
                return false;
            }

            return true;
        }

        private static bool IsCtrlDown()
        {
            return IsKeyDown(WinApi.VK_CONTROL) || IsKeyDown(WinApi.VK_LCONTROL) || IsKeyDown(WinApi.VK_RCONTROL);
        }

        private static bool IsShiftDown()
        {
            return IsKeyDown(WinApi.VK_SHIFT) || IsKeyDown(WinApi.VK_LSHIFT) || IsKeyDown(WinApi.VK_RSHIFT);
        }

        private static bool IsAltDown()
        {
            return IsKeyDown(WinApi.VK_MENU) || IsKeyDown(WinApi.VK_LMENU) || IsKeyDown(WinApi.VK_RMENU);
        }

        private static bool IsWinDown()
        {
            return IsKeyDown(WinApi.VK_LWIN) || IsKeyDown(WinApi.VK_RWIN);
        }

        private static bool IsKeyDown(int vk)
        {
            return (WinApi.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        private void OnRawKeyDown(uint vk)
        {
            if (MatchesHotkey(_config.HideHotkey, vk))
            {
                PostHide();
            }
            else if (MatchesHotkey(_config.ShowHotkey, vk))
            {
                PostShow();
            }
            else if (MatchesHotkey(_config.SettingsHotkey, vk))
            {
                PostAction(SettingsRequested);
            }
        }

        private void OnRawMouseButton(ushort flags)
        {
            if (ShouldIgnoreMouseHide())
            {
                return;
            }

            if (_config.MouseMiddle && (flags & WinApi.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
            {
                PostHide();
            }
            if (_config.MouseX1 && (flags & WinApi.RI_MOUSE_BUTTON_4_DOWN) != 0)
            {
                PostHide();
            }
            if (_config.MouseX2 && (flags & WinApi.RI_MOUSE_BUTTON_5_DOWN) != 0)
            {
                PostHide();
            }
        }

        private void PostHide()
        {
            PostAction(HideRequested);
        }

        private void PostShow()
        {
            PostAction(ShowRequested);
        }

        private void PostAction(Action action)
        {
            if (action == null || _messageWindow == null)
            {
                return;
            }

            try
            {
                if (!_messageWindow.IsHandleCreated)
                {
                    var created = _messageWindow.Handle;
                    if (created == IntPtr.Zero)
                    {
                        return;
                    }
                }

                _messageWindow.BeginInvoke(action);
            }
            catch
            {
            }
        }

        private bool ShouldIgnoreMouseHide()
        {
            return _isSettingsVisible != null && _isSettingsVisible();
        }

        private void TriggerHide()
        {
            Raise(HideRequested);
        }

        private void TriggerShow()
        {
            Raise(ShowRequested);
        }

        public void Dispose()
        {
            _pollTimer.Stop();
            _pollTimer.Dispose();
            _hookThreadStop = true;
            if (_hookThreadId != 0)
            {
                WinApi.PostThreadMessage(_hookThreadId, WinApi.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }

            if (_hookThread != null && _hookThread.IsAlive)
            {
                _hookThread.Join(800);
            }

            _rawInput.Dispose();
            UnregisterHotkeys();
            RemoveMouseHook();
            RemoveKeyboardHook();
        }
    }

    internal sealed class RawInputSink : IDisposable
    {
        public event Action<uint> KeyDown;
        public event Action<ushort> MouseButtonDown;

        private AppConfig _config;

        public void Attach(Form host, AppConfig config)
        {
            _config = config;
            if (host == null || !host.IsHandleCreated)
            {
                return;
            }

            AttachHandle(host.Handle, config);
        }

        public void AttachHandle(IntPtr hwnd, AppConfig config)
        {
            _config = config;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var devices = new WinApi.RAWINPUTDEVICE[2];
            devices[0].usUsagePage = 1;
            devices[0].usUsage = 6;
            devices[0].dwFlags = WinApi.RIDEV_INPUTSINK;
            devices[0].hwndTarget = hwnd;
            devices[1].usUsagePage = 1;
            devices[1].usUsage = 2;
            devices[1].dwFlags = WinApi.RIDEV_INPUTSINK;
            devices[1].hwndTarget = hwnd;
            WinApi.RegisterRawInputDevices(devices, 2, (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(WinApi.RAWINPUTDEVICE)));
        }

        public void Process(IntPtr hRawInput)
        {
            HandleRawInput(hRawInput);
        }

        private void HandleRawInput(IntPtr hRawInput)
        {
            uint size = 0;
            var headerSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(WinApi.RAWINPUTHEADER));
            WinApi.GetRawInputData(hRawInput, WinApi.RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size == 0)
            {
                return;
            }

            var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal((int)size);
            try
            {
                if (WinApi.GetRawInputData(hRawInput, WinApi.RID_INPUT, buffer, ref size, headerSize) == 0)
                {
                    return;
                }

                var header = (WinApi.RAWINPUTHEADER)System.Runtime.InteropServices.Marshal.PtrToStructure(buffer, typeof(WinApi.RAWINPUTHEADER));
                var dataPtr = new IntPtr(buffer.ToInt64() + System.Runtime.InteropServices.Marshal.SizeOf(typeof(WinApi.RAWINPUTHEADER)));
                if (header.dwType == WinApi.RIM_TYPEKEYBOARD)
                {
                    var keyboard = (WinApi.RAWKEYBOARD)System.Runtime.InteropServices.Marshal.PtrToStructure(dataPtr, typeof(WinApi.RAWKEYBOARD));
                    if ((keyboard.Flags & WinApi.RI_KEY_BREAK) == 0 && KeyDown != null)
                    {
                        KeyDown(keyboard.VKey);
                    }
                }
                else if (header.dwType == WinApi.RIM_TYPEMOUSE)
                {
                    var mouse = (WinApi.RAWMOUSE)System.Runtime.InteropServices.Marshal.PtrToStructure(dataPtr, typeof(WinApi.RAWMOUSE));
                    if (mouse.usButtonFlags != 0 && MouseButtonDown != null)
                    {
                        MouseButtonDown(mouse.usButtonFlags);
                    }
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            }
        }

        public void Dispose()
        {
        }
    }
}
