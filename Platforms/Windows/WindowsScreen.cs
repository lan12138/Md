// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Platforms/Windows/WindowsScreen.cs
//  说明：窗口可见性保护 —— 别让窗口跑到所有显示器之外。
//
//        为什么需要（实测踩过，用户反馈"弹出一个永远无法拖到前台的程序，
//        点任务栏也拉不回来"，实际上它只是被放到了屏幕外面）：
//        Win32 用 (-32000, -32000) 当成「最小化窗口的位置」哨兵值。
//        窗口在最小化状态下被读取几何信息，这个哨兵就会被写进 settings.json，
//        之后每次启动都照着它建窗口 —— 于是窗口永远在屏幕外，
//        而且不是最小化状态（IsIconic 为 false），SetForegroundWindow 会"成功"
//        但用户什么也看不到，点任务栏也只是把它在屏幕外还原一下。
//
//        两道防线：
//          ① WindowHelper 里做纯数值判断，拒绝荒谬坐标（跨平台，见那里的说明）；
//          ② 本文件在窗口创建后按**真实 Win32 矩形**再确认一次能否被用户够到，
//             够不到就搬回主显示器中央 —— 这一道还能兜住「保存位置所在的那台
//             显示器已经被拔掉」这类数值上完全合法、实际却看不见的情况。
//
//        这里全程用**设备像素**（GetWindowRect / SetWindowPos），不与 MAUI 的
//        X/Y（设备无关单位）混用，因此不需要任何 DPI 换算。
//
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

#if WINDOWS
using System.Runtime.InteropServices;
using MD.Services;

// 屏幕矩形定义在 WindowsInterop 里（窗口激活也要用），这里起个别名省得到处写限定名
using RECT = MD.Platforms.Windows.WindowsInterop.RECT;

namespace MD.Platforms.Windows;

internal static class WindowsScreen
{
    /// <summary>标题栏至少要有这么多像素落在显示器里，用户才够得到（拖动它）。</summary>
    private const int MinReachableWidth = 80;
    private const int MinReachableHeight = 24;

    /// <summary>探针只取窗口左上角这一小块，也就是标题栏的可见部分。</summary>
    private const int ProbeWidth = 260;
    private const int ProbeHeight = 48;

    /// <summary>
    /// 确认窗口在某个显示器上够得到；够不到就搬回主显示器中央。
    /// </summary>
    /// <returns>是否真的挪动过窗口。</returns>
    public static bool EnsureOnScreen(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero)
                return false;

            if (!WindowsInterop.GetWindowRect(hwnd, out var rect))
                return false;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                return false;

            if (IsReachable(rect))
            {
                // 位置正常也记一行：出问题时能一眼看出窗口当时到底在哪
                DiagnosticsLog.Write($"[窗口位置] {rect.Left},{rect.Top} {width}x{height} 在屏幕内，无需调整");
                return false;
            }

            // 目标：主显示器的可用区域（rcWork 已排除任务栏）
            var work = PrimaryWorkArea();
            if (work is not { } area)
            {
                DiagnosticsLog.Write($"[窗口位置] 窗口在 {rect.Left},{rect.Top}，屏幕外且取不到显示器信息，放弃搬移");
                return false;
            }

            // 窗口比屏幕还大时顺带缩小，否则搬回来也够不到右下角
            int availableWidth = area.Right - area.Left;
            int availableHeight = area.Bottom - area.Top;
            int targetWidth = Math.Min(width, availableWidth);
            int targetHeight = Math.Min(height, availableHeight);

            int x = area.Left + Math.Max(0, (availableWidth - targetWidth) / 2);
            int y = area.Top + Math.Max(0, (availableHeight - targetHeight) / 2);

            uint flags = WindowsInterop.SwpNoZOrder | WindowsInterop.SwpNoActivate;
            if (targetWidth == width)
                flags |= WindowsInterop.SwpNoSize;

            bool ok = WindowsInterop.SetWindowPos(hwnd, IntPtr.Zero, x, y, targetWidth, targetHeight, flags);

            DiagnosticsLog.Write(
                $"[窗口位置] 原位置 {rect.Left},{rect.Top} 在所有显示器之外，" +
                $"已搬到 {x},{y}（{targetWidth}x{targetHeight}，主屏可用区 {availableWidth}x{availableHeight}）结果={ok}");

            return ok;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("修正窗口可见性失败", ex);
            return false;
        }
    }

    /// <summary>窗口的标题栏那一条是否至少有一部分落在某个显示器的可用区域内。</summary>
    private static bool IsReachable(RECT windowRect)
    {
        var probe = new RECT
        {
            Left = windowRect.Left,
            Top = windowRect.Top,
            Right = windowRect.Left + Math.Min(windowRect.Right - windowRect.Left, ProbeWidth),
            Bottom = windowRect.Top + Math.Min(windowRect.Bottom - windowRect.Top, ProbeHeight),
        };

        bool reachable = false;

        // 委托必须在枚举期间保持存活，所以先存进局部变量
        MonitorEnumProc callback = (hMonitor, _, _, _) =>
        {
            var info = MONITORINFO.Create();
            if (GetMonitorInfo(hMonitor, ref info))
            {
                var work = info.rcWork;
                int overlapWidth = Math.Min(probe.Right, work.Right) - Math.Max(probe.Left, work.Left);
                int overlapHeight = Math.Min(probe.Bottom, work.Bottom) - Math.Max(probe.Top, work.Top);

                if (overlapWidth >= MinReachableWidth && overlapHeight >= MinReachableHeight)
                {
                    reachable = true;
                    return false;   // 找到就够了，不用继续枚举
                }
            }
            return true;
        };

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return reachable;
    }

    /// <summary>主显示器的可用区域（已排除任务栏）。</summary>
    private static RECT? PrimaryWorkArea()
    {
        // DEFAULTPRIMARY：窗口不在任何显示器上时返回主显示器，正是我们要的
        var hMonitor = MonitorFromWindow(IntPtr.Zero, MonitorDefaultToPrimary);
        if (hMonitor == IntPtr.Zero)
            hMonitor = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MonitorDefaultToPrimary);
        if (hMonitor == IntPtr.Zero)
            return null;

        var info = MONITORINFO.Create();
        return GetMonitorInfo(hMonitor, ref info) ? info.rcWork : null;
    }

    // ------------------------------------------------------------------

    private const uint MonitorDefaultToPrimary = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        public static MONITORINFO Create() => new()
        {
            cbSize = Marshal.SizeOf<MONITORINFO>(),
        };
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcClip, IntPtr dwData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
}
#endif
