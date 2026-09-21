// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：App.cs
//  说明：应用对象与主窗口的创建 / 生命周期。
//        · 窗口标题、初始尺寸与位置来自 settings.json
//        · 关闭时把主题、窗口尺寸/位置/最大化状态写回 settings.json
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  窗口创建后补一次「在屏幕上」检查：
//                   恢复屏幕外坐标会让窗口完全不可见（实测 -32000）
// -----------------------------------------------------------------------------

using MD.Services;
using MD.Views;

namespace MD;

public sealed class App : Application
{
    public App()
    {
        UserAppTheme = AppTheme.Unspecified;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var settings = AppHost.Settings;
        var windowSettings = settings.Current.Window;

        var pos = windowSettings.X is { } px && windowSettings.Y is { } py
            ? $"{px},{py}"
            : "未记录（由系统决定）";
        DiagnosticsLog.Write($"CreateWindow: 主题={settings.Current.Appearance.ThemeId} 尺寸={windowSettings.Width}x{windowSettings.Height} 位置={pos} 最大化={windowSettings.Maximized}");

        var page = new ReaderPage();

        var window = new Window(page)
        {
            Title = "MD · Markdown 阅读器",
            Width = windowSettings.Width > 200 ? windowSettings.Width : 1180,
            Height = windowSettings.Height > 200 ? windowSettings.Height : 840,
        };

        WindowHelper.Restore(window, windowSettings);

        if (windowSettings.Maximized)
        {
            window.Created += (_, _) => WindowHelper.Maximize(window);
        }

        // 菜单栏与快捷键由 ReaderPage 自己在构造时挂载（Page.MenuBarItems）
        window.Created += (_, _) =>
        {
            DiagnosticsLog.Write("Window.Created 触发，开始 StartupAsync");
            // 窗口已存在，这时才能拿到 AppWindow 给标题栏上色
            page.ApplyWindowChrome();

            // 恢复到屏幕外的位置会让窗口彻底看不见（实测 -32000：程序在跑、
            // 但用户既看不到窗口也拖不回来），先确认它在屏幕上
            WindowHelper.EnsureOnScreen();

            // 命令行参数 / 上次打开的文件 / 欢迎页
            _ = page.StartupAsync();
        };

        window.Destroying += (_, _) =>
        {
            DiagnosticsLog.Write("Window.Destroying：写回窗口几何信息与会话");
            // 标签页 / 文件夹 / 阅读进度属于会话状态，必须在这里落盘
            try
            {
                page.OnShutdown();
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Write("保存会话状态失败", ex);
            }

            WindowHelper.Capture(window, settings.Current.Window);
            settings.Touch();
            settings.Flush();
        };

        return window;
    }
}
