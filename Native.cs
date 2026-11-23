using System.Runtime.InteropServices;

static class Native
{
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowEx(nint parent, nint after, string cls, string? title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT r);
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);

    public static readonly nint HWND_TOPMOST = -1;
    public const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10;

    // per-pixel alpha window (smooth text over a transparent taskbar)
    public struct POINT { public int X, Y; }
    public struct SIZE { public int W, H; }
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref POINT pptDst, ref SIZE psize, nint hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] public static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] public static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(nint obj);
}
