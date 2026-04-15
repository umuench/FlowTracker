using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace FlowTracker.Interop;

internal static class NativeMethods
{
    internal const int WhMouseLl = 14;
    internal const int WsExTransparent = 0x20;
    internal const int GwlExstyle = -20;

    [StructLayout(LayoutKind.Sequential)]
    internal struct LastInputInfo
    {
        internal uint cbSize;
        internal uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    internal delegate nint HookProc(int nCode, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [DllImport("kernel32.dll")]
    internal static extern uint GetTickCount();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hmod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hhk, int nCode, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

    internal static bool TryGetIdleDuration(out TimeSpan idleDuration)
    {
        var lastInputInfo = new LastInputInfo
        {
            cbSize = (uint)Marshal.SizeOf<LastInputInfo>()
        };

        if (!GetLastInputInfo(ref lastInputInfo))
        {
            idleDuration = TimeSpan.Zero;
            return false;
        }

        var now = GetTickCount();
        var elapsedMilliseconds = unchecked(now - lastInputInfo.dwTime);
        idleDuration = TimeSpan.FromMilliseconds(elapsedMilliseconds);
        return true;
    }

    internal static bool TryGetCursorPosition(out Point point) => GetCursorPos(out point);

    internal static bool TryGetForegroundWindowProcessId(out int processId)
    {
        processId = 0;
        var handle = GetForegroundWindow();
        if (handle == nint.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(handle, out var pid);
        if (pid == 0)
        {
            return false;
        }

        processId = unchecked((int)pid);
        return true;
    }

    internal static bool IsForegroundWindowFullscreen()
    {
        var handle = GetForegroundWindow();
        if (handle == nint.Zero || !GetWindowRect(handle, out var rect))
        {
            return false;
        }

        var bounds = WinForms.Screen.FromHandle(handle).Bounds;
        return Math.Abs(rect.Left - bounds.Left) <= 2
            && Math.Abs(rect.Top - bounds.Top) <= 2
            && Math.Abs(rect.Right - bounds.Right) <= 2
            && Math.Abs(rect.Bottom - bounds.Bottom) <= 2;
    }
}
