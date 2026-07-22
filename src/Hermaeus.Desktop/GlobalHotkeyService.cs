using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Hermaeus.Desktop;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int QuickChatId = 0xAE01;
    private const int NewChatId = 0xAE02;
    private const int ServicesId = 0xAE03;
    private const int GwlWndProc = -4;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkSpace = 0x20;
    private const uint VkN = 0x4E;
    private const uint VkS = 0x53;

    private readonly WndProc _wndProc;
    private IntPtr _hwnd;
    private IntPtr _oldWndProc;
    private Action? _quickChat;
    private Action? _newChat;
    private Action? _services;

    public GlobalHotkeyService()
    {
        _wndProc = HandleWindowMessage;
    }

    public static bool IsSupported => OperatingSystem.IsWindows();

    public GlobalHotkeyRegistrationResult Register(
        Window window,
        Action quickChat,
        Action newChat,
        Action services)
    {
        Unregister();

        if (!IsSupported)
            return new(false, "System-wide hotkeys are unavailable on this OS/compositor.");

        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero)
            return new(false, "System-wide hotkeys are unavailable until the window handle exists.");

        _hwnd = handle.Handle;
        _quickChat = quickChat;
        _newChat = newChat;
        _services = services;

        var pointer = Marshal.GetFunctionPointerForDelegate(_wndProc);
        _oldWndProc = SetWindowLongPtr(_hwnd, GwlWndProc, pointer);
        if (_oldWndProc == IntPtr.Zero)
        {
            ClearCallbacks();
            return new(false, "System-wide hotkeys could not attach to the main window.");
        }

        var modifiers = ModControl | ModAlt | ModNoRepeat;
        if (!RegisterHotKey(_hwnd, QuickChatId, modifiers, VkSpace)
            || !RegisterHotKey(_hwnd, NewChatId, modifiers, VkN)
            || !RegisterHotKey(_hwnd, ServicesId, modifiers, VkS))
        {
            var error = Marshal.GetLastWin32Error();
            Unregister();
            return new(false, $"System-wide hotkeys could not be registered. Windows error: {error}.");
        }

        return new(true, "System-wide hotkeys active: Ctrl+Alt+Space, Ctrl+Alt+N, Ctrl+Alt+S.");
    }

    public void Unregister()
    {
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, QuickChatId);
            UnregisterHotKey(_hwnd, NewChatId);
            UnregisterHotKey(_hwnd, ServicesId);
        }

        if (_hwnd != IntPtr.Zero && _oldWndProc != IntPtr.Zero)
            SetWindowLongPtr(_hwnd, GwlWndProc, _oldWndProc);

        _hwnd = IntPtr.Zero;
        _oldWndProc = IntPtr.Zero;
        ClearCallbacks();
    }

    public void Dispose() => Unregister();

    private void ClearCallbacks()
    {
        _quickChat = null;
        _newChat = null;
        _services = null;
    }

    private IntPtr HandleWindowMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmHotkey)
        {
            switch (wParam.ToInt32())
            {
                case QuickChatId:
                    _quickChat?.Invoke();
                    return IntPtr.Zero;
                case NewChatId:
                    _newChat?.Invoke();
                    return IntPtr.Zero;
                case ServicesId:
                    _services?.Invoke();
                    return IntPtr.Zero;
            }
        }

        return CallWindowProc(_oldWndProc, hwnd, msg, wParam, lParam);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
}

public sealed record GlobalHotkeyRegistrationResult(bool Active, string Message);
