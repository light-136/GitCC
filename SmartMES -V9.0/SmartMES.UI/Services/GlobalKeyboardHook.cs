using SmartMES.Core.Interfaces;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace SmartMES.UI.Services
{
    public class GlobalKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private IntPtr _hookHandle = IntPtr.Zero;
        private readonly LowLevelKeyboardProc _proc;
        private readonly ILoggingService _logger;
        private bool _disposed;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

        private readonly List<HotkeyRegistration> _registrations = new();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public GlobalKeyboardHook(ILoggingService logger)
        {
            _logger = logger;
            _proc = HookCallback;
        }

        public void Install()
        {
            try
            {
                using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule!;
                _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);

                if (_hookHandle == IntPtr.Zero)
                    _logger.LogWarning("全局键盘Hook安装失败（可能需要管理员权限）", "KeyboardHook");
                else
                    _logger.LogInfo("全局键盘Hook已安装", "KeyboardHook");
            }
            catch (Exception ex)
            {
                _logger.LogError($"键盘Hook异常: {ex.Message}", "KeyboardHook");
            }
        }

        public void RegisterHotkey(Key key, ModifierKeys modifiers, string actionName, Action action)
        {
            _registrations.Add(new HotkeyRegistration(key, modifiers, actionName, action));
            _logger.LogInfo($"注册快捷键: {modifiers}+{key} -> {actionName}", "KeyboardHook");
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                var key = KeyInterop.KeyFromVirtualKey(vkCode);

                var modifiers = ModifierKeys.None;
                if ((GetAsyncKeyState(0x11) & 0x8000) != 0) modifiers |= ModifierKeys.Control;
                if ((GetAsyncKeyState(0x12) & 0x8000) != 0) modifiers |= ModifierKeys.Alt;
                if ((GetAsyncKeyState(0x10) & 0x8000) != 0) modifiers |= ModifierKeys.Shift;

                foreach (var reg in _registrations)
                {
                    if (reg.Key == key && reg.Modifiers == modifiers)
                    {
                        HotkeyPressed?.Invoke(this, new HotkeyEventArgs(reg.ActionName, key, modifiers));
                        reg.Action();
                    }
                }
            }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public void Uninstall()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
                _logger.LogInfo("全局键盘Hook已卸载", "KeyboardHook");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Uninstall();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }

    public class HotkeyRegistration
    {
        public Key Key { get; }
        public ModifierKeys Modifiers { get; }
        public string ActionName { get; }
        public Action Action { get; }

        public HotkeyRegistration(Key key, ModifierKeys modifiers, string name, Action action)
        {
            Key = key;
            Modifiers = modifiers;
            ActionName = name;
            Action = action;
        }
    }

    public class HotkeyEventArgs : EventArgs
    {
        public string ActionName { get; }
        public Key Key { get; }
        public ModifierKeys Modifiers { get; }

        public HotkeyEventArgs(string name, Key key, ModifierKeys mod)
        {
            ActionName = name;
            Key = key;
            Modifiers = mod;
        }
    }
}
