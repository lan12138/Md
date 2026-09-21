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
    /// <summary>配置格式版本，便于后续迁移。</summary>
    public int Version { get; set; } = 1;

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
        var json = System.Text.Json.JsonSerializer.Serialize(this, ThemeJson.Options);
        return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, ThemeJson.Options) ?? new AppSettings();
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
    public double? X { get; set; }

    /// <summary>窗口左上角 Y。null 表示「尚无记录」。</summary>
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

    /// <summary>文件路径 → 滚动进度（0~1）。</summary>
    public Dictionary<string, double> ScrollPositions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>文件路径 → 最后访问时间（UTC ticks），用于清理过旧记录。</summary>
    public Dictionary<string, long> LastSeen { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FileSettings
{
    public string? LastFile { get; set; }

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

/// <summary>工作区状态：打开的文件夹与标签页，下次启动原样恢复。</summary>
public sealed class WorkspaceSettings
{
    /// <summary>当前打开的文件夹（用于侧栏「文件」页签）。</summary>
    public string? FolderPath { get; set; }

    /// <summary>是否递归扫描子目录。</summary>
    public bool FolderRecursive { get; set; } = true;

    /// <summary>侧栏当前页签：outline / recent / folder。</summary>
    public string SidebarTab { get; set; } = "outline";

    /// <summary>已打开的标签页（按显示顺序）。</summary>
    public List<string> OpenTabs { get; set; } = new();

    /// <summary>当前激活的标签页路径。</summary>
    public string? ActiveTab { get; set; }
}
