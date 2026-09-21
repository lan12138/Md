// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Platforms/Windows/WindowsTitleBar.cs
//  说明：让系统绘制的窗口标题栏跟随 MD 的配色。
//
//        为什么需要它：WinUI 的标题栏底色由**系统**的亮暗设置与 Mica 背景决定。
//        用户的 Windows 处于亮色模式时，即使 MD 切到 GitHub 暗色，标题栏依然是
//        一条浅蓝灰（实测 #CCDEEA）的亮带压在暗色应用顶上，非常割裂。
//
//        实现选型（踩过的坑都记在这里）：
//        ✗ AppWindowTitleBar.BackgroundColor —— 只在「扩展内容到标题栏」时生效。
//          默认（不扩展）标题栏下设了完全没效果，实测颜色纹丝不动。
//        ✗ MAUI 的 Window.TitleBar（自绘标题栏）—— 颜色是对的，但会把
//          MenuBarItems 挤掉，菜单栏直接消失，属于不可接受的回归。
//        ✓ DwmSetWindowAttribute —— 保留系统标题栏（菜单栏照旧内嵌在其中），
//          又能精确指定底色与文字色。Windows 11 起支持 DWMWA_CAPTION_COLOR(35)
//          与 DWMWA_TEXT_COLOR(36)，正好用来贴主题色；
//          DWMWA_USE_IMMERSIVE_DARK_MODE(20) 负责让三个窗口按钮也变成暗色。
//
//        ⚠ 所有 DWM 调用都可能因为系统版本过低而失败，失败只记日志，绝不影响主流程。
//
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版（AppWindowTitleBar，无效）
//  修改：2026-09-21  改用 MAUI TitleBar 自绘（颜色正确但菜单栏丢失，回退）
//  修改：2026-09-21  最终改为 DwmSetWindowAttribute 精确上色，保留系统标题栏与菜单栏
//  修改：2026-09-21  取窗口句柄 / WinUI 窗口的两段代码抽到 WindowsInterop，
//                   供窗口激活（单实例模式）复用
// -----------------------------------------------------------------------------

#if WINDOWS
using System.Runtime.InteropServices;
using MD.Rendering;
using MD.Services;

namespace MD.Platforms.Windows;

internal static class WindowsTitleBar
{
    // 属性号来自 dwmapi.h
    private const int DwmaUseImmersiveDarkModeLegacy = 19;  // 20H1 之前
    private const int DwmaUseImmersiveDarkMode = 20;        // Windows 10 20H1+ / Windows 11
    private const int DwmaCaptionColor = 35;                // Windows 11
    private const int DwmaTextColor = 36;                   // Windows 11

    /// <summary>按当前主题刷新标题栏配色。</summary>
    public static void Apply(RenderContext ctx)
    {
        try
        {
            var hwnd = WindowsInterop.ResolveWindowHandle();
            if (hwnd == IntPtr.Zero)
            {
                // 页面构造期（窗口还没创建）会走到这里，属正常，不记日志避免噪音
                return;
            }

            bool dark = ctx.Theme.IsDark;

            // WinUI 侧的亮暗：MAUI 把内容扩展进了标题栏，那一条的底色由 WinUI 主题
            // （Mica 背景的亮暗变体）决定，光设 MAUI 的 UserAppTheme 管不到它。
            if (WindowsInterop.ResolveNativeWindow() is { } native)
            {
                // 去掉系统背景材质：Mica 是半透明的，会让标题栏那一条既不是主题色、
                // 又压不住桌面壁纸的颜色，而且它会顶掉 DWMWA_CAPTION_COLOR 的效果。
                if (native.SystemBackdrop is not null)
                    native.SystemBackdrop = null;

                if (native.Content is Microsoft.UI.Xaml.FrameworkElement fe)
                {
                    var wanted = dark
                        ? Microsoft.UI.Xaml.ElementTheme.Dark
                        : Microsoft.UI.Xaml.ElementTheme.Light;
                    if (fe.RequestedTheme != wanted)
                        fe.RequestedTheme = wanted;
                }

                // 去掉材质后，标题栏那一条露出的是窗口根元素的背景（默认纯白）。
                // 直接把它设成工具栏底色：页面正文会盖住其余部分，
                // 只有标题栏那一条露出来，于是标题栏与工具条连成一片、颜色完全一致。
                // （Background 定义在 Panel 上，FrameworkElement 没有，必须缩窄类型）
                if (native.Content is Microsoft.UI.Xaml.Controls.Panel rootPanel)
                {
                    rootPanel.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        ToWinColor(ctx.Theme.Colors.ToolbarBg));
                }
            }

            // 先切亮暗模式：三个窗口按钮（最小化/最大化/关闭）的配色由它决定
            int flag = dark ? 1 : 0;
            int r1 = DwmSet(hwnd, DwmaUseImmersiveDarkMode, flag);
            int r2 = DwmSet(hwnd, DwmaUseImmersiveDarkModeLegacy, flag);

            // 再精确指定底色与文字色，让标题栏与顶部工具条连成一片
            int r3 = DwmSet(hwnd, DwmaCaptionColor, ToColorRef(ctx.Theme.Colors.ToolbarBg));
            int r4 = DwmSet(hwnd, DwmaTextColor, ToColorRef(ctx.Theme.Colors.ToolbarText));

            DiagnosticsLog.Write(
                $"标题栏配色: 底={ctx.Theme.Colors.ToolbarBg} 字={ctx.Theme.Colors.ToolbarText} " +
                $"暗色={dark} hwnd=0x{hwnd.ToInt64():X} 结果={r1}/{r2}/{r3}/{r4}（0 为成功）");
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("标题栏配色失败", ex);
        }
    }

    private static int DwmSet(IntPtr hwnd, int attribute, int value)
    {
        try
        {
            return DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>#AARRGGBB → WinUI 颜色。</summary>
    private static global::Windows.UI.Color ToWinColor(string hex)
    {
        var n = ThemeCatalog.NormalizeHex(hex);
        return global::Windows.UI.Color.FromArgb(
            Convert.ToByte(n.Substring(1, 2), 16),
            Convert.ToByte(n.Substring(3, 2), 16),
            Convert.ToByte(n.Substring(5, 2), 16),
            Convert.ToByte(n.Substring(7, 2), 16));
    }

    /// <summary>#AARRGGBB → Windows 的 COLORREF（0x00BBGGRR）。</summary>
    private static int ToColorRef(string hex)
    {
        var n = ThemeCatalog.NormalizeHex(hex);
        int r = Convert.ToInt32(n.Substring(3, 2), 16);
        int g = Convert.ToInt32(n.Substring(5, 2), 16);
        int b = Convert.ToInt32(n.Substring(7, 2), 16);
        return (b << 16) | (g << 8) | r;
    }

    // 取窗口句柄与 WinUI 窗口的能力已抽到 WindowsInterop（窗口激活也需要用）

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
#endif
