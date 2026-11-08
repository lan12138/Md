// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Services/StartupGuard.cs
//  说明：启动期兜底与「打不开」问题取证。
//
//        背景：GUI 程序没有控制台，WinUI / Windows App SDK 在 Main 之前
//        加载失败时，用户看到的只是「双击没反应」。这里做三件事：
//          1) 在创建 MauiApp 之前检查最低系统版本，给出明确中文提示；
//          2) 创建失败时用 Win32 MessageBox 弹出原因 + 日志路径（不依赖 WinUI）；
//          3) 提供 --diagnose，把系统/架构/路径/运行时信息写到文件，便于远程排查。
//
//        最低版本 17763（Windows 10 1809）：Windows App SDK 的下限。
//  作者：MD
//  创建：2026-11-08
//  修改：2026-11-08  初版
// -----------------------------------------------------------------------------

using System.Runtime.InteropServices;
using System.Text;

namespace MD.Services;

public static class StartupGuard
{
    /// <summary>Windows App SDK 支持的最低 Windows 10 版本（1809 / build 17763）。</summary>
    public const int MinimumWindowsBuild = 17763;

    /// <summary>检查运行环境；不满足时返回 false 并给出中文原因。</summary>
    public static bool CheckEnvironment(out string? error)
    {
        error = null;

#if WINDOWS
        try
        {
            var version = Environment.OSVersion.Version;
            if (version.Build < MinimumWindowsBuild)
            {
                error =
                    $"MD 需要 Windows 10 1809（build {MinimumWindowsBuild}）或更高版本。\n" +
                    $"当前系统：{Environment.OSVersion}（build {version.Build}）。\n\n" +
                    "Windows 7 / 8 / 8.1 以及 Windows 10 1803 及更早版本无法运行本程序。";
                return false;
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("检查系统版本失败", ex);
        }
#endif

        return true;
    }

    /// <summary>用 Win32 MessageBox 显示致命错误（WinUI 还没起来时也能弹）。</summary>
    public static void ShowFatal(string title, string message)
    {
#if WINDOWS
        try
        {
            // MB_OK | MB_ICONERROR | MB_TOPMOST
            MessageBoxW(IntPtr.Zero, message, title, 0x00000000u | 0x00000010u | 0x00040000u);
        }
        catch
        {
            // 弹窗失败也不能再抛
        }
#endif
    }

    /// <summary>写一份诊断信息，返回文件路径。</summary>
    public static string WriteDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine("MD 启动诊断");
        sb.AppendLine("============");
        sb.AppendLine($"时间        : {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"程序版本    : {typeof(StartupGuard).Assembly.GetName().Version}");
        sb.AppendLine($"运行时      : {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"OS          : {RuntimeInformation.OSDescription}");
        sb.AppendLine($"OS 版本     : {Environment.OSVersion}");
        sb.AppendLine($"进程架构    : {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"系统架构    : {RuntimeInformation.OSArchitecture}");
        sb.AppendLine($"64 位系统   : {Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"进程路径    : {Environment.ProcessPath}");
        sb.AppendLine($"程序目录    : {AppContext.BaseDirectory}");
        sb.AppendLine($"配置目录    : {AppPaths.ConfigRoot}");
        sb.AppendLine($"日志文件    : {DiagnosticsLog.FilePath ?? "(不可用)"}");
        sb.AppendLine($"临时目录    : {Path.GetTempPath()}");
        sb.AppendLine($"TEMP        : {Environment.GetEnvironmentVariable("TEMP")}");
        sb.AppendLine($"TMP         : {Environment.GetEnvironmentVariable("TMP")}");
        sb.AppendLine($"BUNDLE 目录 : {Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR") ?? "(默认)"}");
        sb.AppendLine($"命令行      : {string.Join(" ", Environment.GetCommandLineArgs())}");

        try
        {
            sb.AppendLine($"屏幕        : {GetSystemMetrics(0)} x {GetSystemMetrics(1)}");
        }
        catch
        {
            // 忽略
        }

        string path;
        try
        {
            path = Path.Combine(AppPaths.ConfigRoot, "diagnose.txt");
        }
        catch
        {
            path = Path.Combine(Path.GetTempPath(), "MD-diagnose.txt");
        }

        try
        {
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
        catch
        {
            // 配置目录不可写时再退一次
            path = Path.Combine(Path.GetTempPath(), "MD-diagnose.txt");
            try { File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false)); } catch { /* ignore */ }
        }

        DiagnosticsLog.Write($"诊断信息已写入: {path}");
        return path;
    }

#if WINDOWS
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
#endif
}
