// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Services/PlatformLauncher.cs
//  说明：打开链接 / 打开本地目录 / 打开文件所在位置等平台差异操作。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using System.Diagnostics;

namespace MD.Services;

public static class PlatformLauncher
{
    /// <summary>在系统默认浏览器中打开链接。</summary>
    public static async Task OpenUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            await Launcher.Default.OpenAsync(url);
        }
        catch
        {
            // 某些平台无可用浏览器时忽略
        }
    }

    /// <summary>在文件管理器中打开目录。</summary>
    public static void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            path = AppPaths.ConfigRoot;

        try
        {
#if WINDOWS
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                return;
            }
#endif
            _ = Launcher.Default.OpenAsync(new Uri($"file://{path.Replace('\\', '/')}"));
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>在文件管理器中定位并选中文件。</summary>
    public static void RevealFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
#if WINDOWS
            if (File.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
                return;
            }
#endif
            var dir = Path.GetDirectoryName(filePath);
            OpenFolder(dir);
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>把相对链接解析为目标：URL 直接用，相对路径相对文档目录。</summary>
    public static string ResolveLink(string url, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return url;

        var decoded = Uri.UnescapeDataString(url);
        var hashIndex = decoded.IndexOf('#');
        if (hashIndex >= 0)
            decoded = decoded[..hashIndex];

        if (decoded.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try { return new Uri(decoded).LocalPath; } catch { return decoded; }
        }

        if (string.IsNullOrWhiteSpace(decoded))
            return url; // 纯锚点跳转，交给文档内处理

        return Path.IsPathRooted(decoded)
            ? decoded
            : Path.GetFullPath(Path.Combine(baseDirectory, decoded));
    }
}
