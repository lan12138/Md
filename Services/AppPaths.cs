// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Services/AppPaths.cs
//  说明：统一的路径解析（配置目录 / 主题目录 / 缓存目录）。
//        Windows 用 %APPDATA%\MD，macOS 用 ~/Library/Application Support/MD，
//        Linux 用 ~/.config/MD，Android 用应用私有目录。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using System.Runtime.InteropServices;

namespace MD.Services;

public static class AppPaths
{
    public const string AppFolderName = "MD";

    private static string? _configRoot;

    /// <summary>配置根目录（首次访问时创建）。</summary>
    public static string ConfigRoot
    {
        get
        {
            if (_configRoot is not null)
                return _configRoot;

            string root;
#if ANDROID
            root = Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, AppFolderName);
#else
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", AppFolderName);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(appData))
                    appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming");
                root = Path.Combine(appData, AppFolderName);
            }
            else
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrWhiteSpace(xdg))
                    xdg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                root = Path.Combine(xdg, AppFolderName);
            }
#endif

            try
            {
                Directory.CreateDirectory(root);
            }
            catch
            {
                // 目录不可写时退化为临时目录，保证程序仍可运行
                root = Path.Combine(Path.GetTempPath(), AppFolderName);
                try { Directory.CreateDirectory(root); } catch { /* ignore */ }
            }

            _configRoot = root;
            return root;
        }
    }

    public static string SettingsFile => Path.Combine(ConfigRoot, "settings.json");

    public static string ThemesDirectory
    {
        get
        {
            var dir = Path.Combine(ConfigRoot, "themes");
            try { Directory.CreateDirectory(dir); } catch { /* ignore */ }
            return dir;
        }
    }

    public static string CacheDirectory
    {
        get
        {
            var dir = Path.Combine(ConfigRoot, "cache");
            try { Directory.CreateDirectory(dir); } catch { /* ignore */ }
            return dir;
        }
    }

    public static string LogFile => Path.Combine(ConfigRoot, "md.log");
}
