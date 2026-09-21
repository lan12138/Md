// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Platforms/Windows/WindowsActivation.cs
//  说明：把 MD 的窗口拉到前台。
//
//        用途：单实例模式下，用户双击第二篇文档时，文件是在已有的窗口里打开的
//        （见 Services/SingleInstance.cs），所以必须把这个窗口显式提到前台 ——
//        否则用户看到的是「文件打开了，但窗口还压在别的窗口后面」，体验上等同于没反应。
//
//        难点：SetForegroundWindow 会被系统**静默拒绝**（返回 false、不抛异常），
//        当前台进程不是我们自己时尤其明显。三道措施依次兜底：
//          ① 窗口最小化时先 SW_RESTORE —— 状态变化本身就会带一次前台；
//          ② 第二个实例退出前调用 AllowSetForegroundWindow(ASFW_ANY)，
//             把它刚获得的前台权限让给我们（见 SingleInstance.Forward）；
//          ③ 仍不成功就 topmost 抬一下再放开，制造一次 Z 序变化再请求前台。
//
//        刻意**不做**「SW_MINIMIZE + SW_RESTORE」那一套：虽然最可靠，
//        但用户每双击一次文档窗口就闪一下，观感很差。
//
//        另外：激活之前先调 WindowsScreen.EnsureOnScreen —— 一个跑到屏幕外的
//        窗口，无论怎么"提到前台"用户都看不见。
//
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  激活前先做屏幕内检查（否则"激活成功"却什么都看不到）
// -----------------------------------------------------------------------------

#if WINDOWS
using System.Runtime.InteropServices;
using MD.Services;

namespace MD.Platforms.Windows;

internal static class WindowsActivation
{
    private const int SwRestore = 9;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);

    /// <summary>把主窗口提到前台。失败只记日志，绝不影响打开文档这件事本身。</summary>
    public static void Activate()
    {
        try
        {
            var hwnd = WindowsInterop.ResolveWindowHandle();
            if (hwnd == IntPtr.Zero)
            {
                DiagnosticsLog.Write("[激活] 拿不到窗口句柄，跳过");
                return;
            }

            // 先确保窗口在屏幕上：一个跑到屏幕外的窗口，无论怎么"提到前台"
            // 用户都看不见（实测踩过，见 WindowsScreen 的说明）
            WindowsScreen.EnsureOnScreen(hwnd);

            bool wasMinimized = IsIconic(hwnd);
            if (wasMinimized)
            {
                ShowWindow(hwnd, SwRestore);
                if (GetForegroundWindow() == hwnd)
                {
                    DiagnosticsLog.Write("[激活] 最小化还原即已到前台");
                    return;
                }
            }

            // ① 直接请求
            SetForegroundWindow(hwnd);
            if (GetForegroundWindow() == hwnd)
            {
                DiagnosticsLog.Write("[激活] SetForegroundWindow 成功");
                return;
            }

            // ② 兜底：制造一次 Z 序变化再请求
            WindowsInterop.SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMoveOrSizeNoActivate);
            WindowsInterop.SetWindowPos(hwnd, HwndNotTopmost, 0, 0, 0, 0, SwpNoMoveOrSizeNoActivate);
            SetForegroundWindow(hwnd);

            bool ok = GetForegroundWindow() == hwnd;
            DiagnosticsLog.Write($"[激活] hwnd=0x{hwnd.ToInt64():X} 最小化还原={wasMinimized} 结果={(ok ? "成功" : "仍被系统拒绝")}");
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("激活窗口失败", ex);
        }
    }

    /// <summary>不移动、不缩放、不激活，只改 Z 序。</summary>
    private const uint SwpNoMoveOrSizeNoActivate =
        WindowsInterop.SwpNoMove | WindowsInterop.SwpNoSize | WindowsInterop.SwpNoActivate;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
#endif
