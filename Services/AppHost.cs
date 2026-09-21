// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Services/AppHost.cs
//  说明：极简的应用级容器。单窗口阅读器不需要 DI 容器的复杂度，
//        这里用静态单例持有跨页面共享的服务实例。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

namespace MD.Services;

public static class AppHost
{
    private static SettingsService? _settings;
    private static ThemeCatalog? _themes;
    private static DocumentService? _documents;
    private static FolderService? _folders;

    public static SettingsService Settings => _settings ??= new SettingsService();

    public static ThemeCatalog Themes => _themes ??= new ThemeCatalog();

    public static DocumentService Documents => _documents ??= new DocumentService();

    public static FolderService Folders => _folders ??= new FolderService();

    /// <summary>应用版本号（用于「关于」）。</summary>
    public static string Version => "1.0.0";

    public static void Shutdown()
    {
        _settings?.Flush();
        _settings?.Dispose();
    }
}
