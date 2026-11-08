// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Models/AppSettings.cs
//  说明：个性化设置的 JSON 模型。运行时写入 <配置目录>/settings.json，
//        记录主题、窗口尺寸/位置/最大化、字号行距、侧栏宽度、最近文件、
//        每个文件的阅读进度等，下次启动原样恢复。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  窗口 X/Y 由 double.NaN 哨兵改为 double?：
//                   System.Text.Json 拒写 NaN/Infinity 会抛异常，导致整份
//                   settings.json 保存失败（表现为「设置永远不生效」）
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace MD.Models;

public sealed class AppSettings
{
    /// <summary>配置格式版本，便于后续迁移。v2：不再持久化任何文件历史。</summary>
    public int Version { get; set; } = 2;

    // ---------------- 外观 ----------------
    public AppearanceSettings Appearance { get; set; } = new();

    // ---------------- 窗口 ----------------
    public WindowSettings Window { get; set; } = new();

    // ---------------- 阅读区 ----------------
    public ReadingSettings Reading { get; set; } = new();

    // ---------------- 文件 ----------------
    public FileSettings Files { get; set; } = new();

    // ---------------- 工作区（文件夹 / 标签页） ----------------
    public WorkspaceSettings Workspace { get; set; } = new();

    public AppSettings Clone()
    {
        // 源生成上下文：裁剪发布下没有反射元数据
        var json = System.Text.Json.JsonSerializer.Serialize(this, AppJsonContext.Default.AppSettings);
        return System.Text.Json.JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings) ?? new AppSettings();
    }
}

public sealed class AppearanceSettings
{
    /// <summary>当前主题 Id。</summary>
    public string ThemeId { get; set; } = "github-light";

    /// <summary>深色模式下的主题 Id。</summary>
    public string DarkThemeId { get; set; } = "github-dark";

    /// <summary>是否跟随系统明暗。</summary>
    public bool FollowSystemTheme { get; set; } = true;

    /// <summary>界面语言（预留，目前仅 zh-CN）。</summary>
    public string Language { get; set; } = "zh-CN";
}

public sealed class WindowSettings
{
    /// <summary>窗口左上角 X。null 表示「尚无记录」，交给系统决定默认位置。</summary>
    [JsonConverter(typeof(FiniteNullableDoubleConverter))]
    public double? X { get; set; }

    /// <summary>窗口左上角 Y。null 表示「尚无记录」。</summary>
    [JsonConverter(typeof(FiniteNullableDoubleConverter))]
    public double? Y { get; set; }

    public double Width { get; set; } = 1180;
    public double Height { get; set; } = 840;
    public bool Maximized { get; set; }
    public bool SidebarVisible { get; set; } = true;
    public double SidebarWidth { get; set; } = 268;
}

public sealed class ReadingSettings
{
    /// <summary>字号增量（用户微调，叠加在主题基准之上）。</summary>
    public double FontSizeDelta { get; set; }

    /// <summary>行高增量。</summary>
    public double LineHeightDelta { get; set; }

    /// <summary>
    /// 正文列宽模式：
    /// auto（跟随主题舒适宽度并按窗口自适应放宽）/ narrow(700) /
    /// standard(900) / wide(1150) / full（铺满可用宽度）。
    /// </summary>
    public string ContentWidthMode { get; set; } = "auto";

    /// <summary>专注模式：隐藏工具栏与状态栏。</summary>
    public bool FocusMode { get; set; }

    /// <summary>显示状态栏。</summary>
    public bool StatusBarVisible { get; set; } = true;

    /// <summary>滚动时大纲自动高亮当前标题。</summary>
    public bool SyncOutline { get; set; } = true;

    /// <summary>代码块显示行号。</summary>
    public bool CodeLineNumbers { get; set; }

    /// <summary>图片自动加载（关闭后显示占位）。</summary>
    public bool LoadImages { get; set; } = true;

    /// <summary>
    /// 文件路径 → 滚动进度（0~1）。**只在本次运行内有效**。
    /// 不写进 settings.json：文件路径属于「打开记录」，用户明确要求不保存历史。
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, double> ScrollPositions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>文件路径 → 最后访问时间（UTC ticks），仅本次运行内用于容量控制。</summary>
    [JsonIgnore]
    public Dictionary<string, long> LastSeen { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FileSettings
{
    /// <summary>
    /// 「最近打开」只服务本次运行，**不落盘**。
    /// 旧版本曾把它写进 settings.json，导致每次启动左上角恢复一堆历史标签；
    /// v2 起用 [JsonIgnore] 彻底移出持久化，并清掉磁盘上的旧字段。
    /// </summary>
    [JsonIgnore]
    public List<RecentFile> Recent { get; set; } = new();

    public int MaxRecent { get; set; } = 20;
}

public sealed class RecentFile
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public string SubTitle => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    [JsonIgnore]
    public bool Exists => System.IO.File.Exists(Path);
}

/// <summary>
/// 工作区状态。只保留「与文件无关」的偏好（侧栏页签、递归开关）。
/// 文件夹路径 / 标签页列表属于打开记录，v2 起不再持久化。
/// </summary>
public sealed class WorkspaceSettings
{
    /// <summary>本次运行打开的文件夹（仅用于文件夹选择框的起始位置）。</summary>
    [JsonIgnore]
    public string? FolderPath { get; set; }

    /// <summary>是否递归扫描子目录。</summary>
    public bool FolderRecursive { get; set; } = true;

    /// <summary>侧栏当前页签：outline / recent / folder。</summary>
    public string SidebarTab { get; set; } = "outline";
}
