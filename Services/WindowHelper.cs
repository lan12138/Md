// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Services/WindowHelper.cs
//  说明：窗口相关的平台差异封装。
//        窗口尺寸与位置由 MAUI 的 Window.X/Y/Width/Height 完成（跨平台），
//        「最大化状态」MAUI 没有暴露，Windows 端通过 WinAppSDK 的
//        OverlappedPresenter 读取/设置。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  拒绝荒谬坐标。窗口在**最小化**状态下被读到几何信息时，
//                   Win32 会给出 (-32000, -32000) 这个「最小化位置」哨兵值；
//                   只做 IsFinite 检查会把它照单全收，于是
//                   ① 写进 settings.json，② 下次启动照着它建窗口 ——
//                   窗口落在所有显示器之外，表现为「程序在跑但看不见、
//                   点任务栏也拖不回前台」。读写两侧都要拦。
// -----------------------------------------------------------------------------

namespace MD.Services;

public static class WindowHelper
{
    /// <summary>
    /// 小于这个值的坐标一律视为 Win32 的「最小化位置」哨兵（实测为 -32000），
    /// 而不是真实位置。留一点余量，也顺手挡掉其他荒谬的负值。
    /// </summary>
    private const double OffScreenSentinel = -30000;

    /// <summary>坐标是否是可信的真实位置。</summary>
    private static bool IsPlausiblePosition(double x, double y)
        => double.IsFinite(x) && double.IsFinite(y)
           && x > OffScreenSentinel && y > OffScreenSentinel;

    /// <summary>恢复窗口尺寸与位置；返回值表示是否成功应用过位置。</summary>
    public static void Restore(Window window, Models.WindowSettings settings)
    {
        try
        {
            if (settings.Width > 200)
                window.Width = settings.Width;
            if (settings.Height > 200)
                window.Height = settings.Height;

            if (settings.X is { } x && settings.Y is { } y && IsPlausiblePosition(x, y))
            {
                window.X = x;
                window.Y = y;
            }
            else if (settings.X is not null || settings.Y is not null)
            {
                // 记录里是坏值（例如 -32000）：不套用，交给系统决定位置
                DiagnosticsLog.Write(
                    $"[窗口位置] 忽略不可信的记录位置 {settings.X},{settings.Y}，改由系统决定");
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("恢复窗口几何信息失败", ex);
        }
    }

    /// <summary>把当前窗口几何信息写回设置对象。</summary>
    public static void Capture(Window window, Models.WindowSettings settings)
    {
        try
        {
            settings.Maximized = IsMaximized(window);

            if (!settings.Maximized)
            {
                var width = window.Width;
                var height = window.Height;
                if (width > 200 && height > 200)
                {
                    settings.Width = width;
                    settings.Height = height;
                }

                // MAUI 在窗口尚未定位时 X/Y 就是 NaN；
                // 窗口处于最小化状态时则是 Win32 的 -32000 哨兵 ——
                // 这两种都不能进 settings.json，否则下次启动窗口就跑到屏幕外了。
                if (IsPlausiblePosition(window.X, window.Y))
                {
                    settings.X = window.X;
                    settings.Y = window.Y;
                }
                else
                {
                    // 关键：这里要**清空**而不是保留旧值。否则配置里那个坏坐标会
                    // 永远留在原地（每次都因为窗口最小化而跳过更新），
                    // 变成「每次启动都要靠 Restore 拦一次」的长期噪音。
                    // 清成 null 表示「未记录」，下次启动由系统决定位置。
                    DiagnosticsLog.Write(
                        $"[窗口位置] 关闭时读到不可信坐标 {window.X},{window.Y}（窗口可能处于最小化），改为不记录位置");
                    settings.X = null;
                    settings.Y = null;
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("保存窗口几何信息失败", ex);
        }
    }

    /// <summary>
    /// 窗口跑到所有显示器之外时把它搬回主显示器中央。
    /// 兜住「保存位置所在的那台显示器已经被拔掉」这类数值上完全合法、
    /// 实际却看不见的情况（Windows 实现见 Platforms/Windows/WindowsScreen.cs）。
    /// </summary>
    public static void EnsureOnScreen()
    {
#if WINDOWS
        try
        {
            Platforms.Windows.WindowsScreen.EnsureOnScreen(
                Platforms.Windows.WindowsInterop.ResolveWindowHandle());
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("修正窗口可见性失败", ex);
        }
#endif
    }

    public static bool IsMaximized(Window window)
    {
#if WINDOWS
        try
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
            {
                var presenter = native.AppWindow?.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
                return presenter?.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;
            }
        }
        catch
        {
            // 忽略
        }
#endif
        return false;
    }

    public static void Maximize(Window window)
    {
#if WINDOWS
        try
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
            {
                if (native.AppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                    presenter.Maximize();
            }
        }
        catch
        {
            // 忽略
        }
#endif
    }
}
