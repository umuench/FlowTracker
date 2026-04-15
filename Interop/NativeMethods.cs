using System.Runtime.InteropServices;

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
}
