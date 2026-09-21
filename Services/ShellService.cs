// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Services/ShellService.cs
//  说明：与操作系统「外壳」相关的两件事：
//        · ShellService  ：注册 / 注销右键「打开方式」与默认应用（仅 Windows 有意义）
//        · FolderPicker  ：选择文件夹（MAUI 的 FilePicker 只能选文件）
//        非 Windows 平台上这些调用是安全的空实现，界面按返回值提示即可。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

namespace MD.Services;

public static class ShellService
{
    /// <summary>当前平台是否支持这些外壳能力。</summary>
    public static bool IsSupported
    {
        get
        {
#if WINDOWS
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>是否已注册（用于设置面板显示当前状态）。</summary>
    public static bool IsRegistered
    {
        get
        {
#if WINDOWS
            return Platforms.Windows.WindowsShellIntegration.IsRegistered;
#else
            return false;
#endif
        }
    }

    public static (bool Ok, string Message) Register()
    {
#if WINDOWS
        return Platforms.Windows.WindowsShellIntegration.Register();
#else
        return (false, "当前平台不支持注册文件关联");
#endif
    }

    public static (bool Ok, string Message) Unregister()
    {
#if WINDOWS
        return Platforms.Windows.WindowsShellIntegration.Unregister();
#else
        return (false, "当前平台不支持注册文件关联");
#endif
    }

    /// <summary>打开系统「默认应用」页面（Windows 不允许程序直接改默认关联）。</summary>
    public static void OpenDefaultAppsSettings()
    {
#if WINDOWS
        Platforms.Windows.WindowsShellIntegration.OpenDefaultAppsSettings();
#endif
    }

    /// <summary>打开系统「打开方式」对话框。</summary>
    public static void OpenWithDialog(string? path)
    {
#if WINDOWS
        Platforms.Windows.WindowsShellIntegration.OpenWithDialog(path);
#endif
    }
}

public static class FolderPicker
{
    /// <summary>弹出文件夹选择框，取消或平台不支持时返回 null。</summary>
    public static async Task<string?> PickAsync(string? startAt = null)
    {
#if WINDOWS
        try
        {
            var window = Application.Current?.Windows.FirstOrDefault();
            return await Platforms.Windows.WindowsFolderPicker.PickAsync(window, startAt);
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("选择文件夹失败", ex);
            return null;
        }
#else
        await Task.CompletedTask;
        return null;
#endif
    }
}

public static class FileSaver
{
    /// <summary>弹出「另存为」对话框，取消或平台不支持时返回 null。</summary>
    public static async Task<string?> PickAsync(string suggestedName, string typeLabel = "Markdown 文件", string extension = ".md")
    {
#if WINDOWS
        try
        {
            var window = Application.Current?.Windows.FirstOrDefault();
            return await Platforms.Windows.WindowsFileSaver.PickAsync(window, suggestedName, typeLabel, extension);
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("另存为失败", ex);
            return null;
        }
#else
        await Task.CompletedTask;
        return null;
#endif
    }
}
