// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Platforms/Windows/WindowsInterop.cs
//  说明：WinUI / Win32 窗口互操作的最小公共部分。
//        原本这两段代码内嵌在 WindowsTitleBar 里，加窗口激活功能时需要复用，
//        于是提到这里 —— 标题栏上色与窗口激活都只是「拿到 hwnd 然后调 API」。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  从 WindowsTitleBar 抽离
// -----------------------------------------------------------------------------

#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using MD.Services;

namespace MD.Platforms.Windows;

internal static class WindowsInterop
{
    // SetWindowPos 的常用标志。两个调用方（窗口激活 / 屏幕外救援）都要用，放这里避免重复声明。
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;

    /// <summary>屏幕坐标矩形（设备像素）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>取窗口在其坐标系下的矩形。窗口还没创建时返回 false。</summary>
    public static bool GetWindowRect(IntPtr hwnd, out RECT rect)
    {
        rect = default;
        try
        {
            return hwnd != IntPtr.Zero && GetWindowRectNative(hwnd, out rect);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>移动 / 缩放窗口。坐标为设备像素。</summary>
    public static bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags)
    {
        try
        {
            return hwnd != IntPtr.Zero && SetWindowPosNative(hwnd, insertAfter, x, y, cx, cy, flags);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>取当前 MAUI 窗口背后的 WinUI Window。窗口还没创建时返回 null。</summary>
    public static Microsoft.UI.Xaml.Window? ResolveNativeWindow()
    {
        try
        {
            // 注意：这里要的是 MAUI 的 Application，不是 Microsoft.UI.Xaml.Application
            var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            if (mauiWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
                return native;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("取 WinUI 窗口失败", ex);
        }

        return null;
    }

    /// <summary>
    /// 取窗口句柄。窗口尚未创建时返回 <see cref="IntPtr.Zero"/>。
    /// 拿不到 WinRT 窗口时退回进程主窗口句柄。
    /// </summary>
    public static IntPtr ResolveWindowHandle()
    {
        try
        {
            if (ResolveNativeWindow() is { } native)
                return WinRT.Interop.WindowNative.GetWindowHandle(native);
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("取窗口句柄失败（退回进程主窗口句柄）", ex);
        }

        try
        {
            return Process.GetCurrentProcess().MainWindowHandle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRectNative(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPosNative(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
#endif
