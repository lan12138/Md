// -----------------------------------------------------------------------------
//  MD MD - 跨平台 Markdown 阅读器
//  文件：Models/ThemeDefinition.cs
//  说明：主题 = 配色（ThemePalette）+ 排版（Typography），整体可 JSON 序列化，
//        内置主题以常量形式内嵌（保证单文件 exe 自包含），
//        用户主题放在 <配置目录>/themes/*.json 中即可被自动发现。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  新增 sidebarHeaderBg / sidebarActiveBg：
//                   侧栏页签条与「当前项」底色不再用强调色实心块（与正文底色冲突），
//                   改为留空由主题底色派生，见 ThemeCatalog.DeriveShades。
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MD.Models;

/// <summary>一套完整的主题定义。</summary>
public sealed class ThemeDefinition
{
    public string Id { get; set; } = "custom";
    public string Name { get; set; } = "自定义";
    public string? Author { get; set; }
    public bool IsDark { get; set; }

    public ThemePalette Colors { get; set; } = new();
    public ThemeTypography Typography { get; set; } = new();

    public bool IsBuiltIn { get; set; }

    public ThemeDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        Author = Author,
        IsDark = IsDark,
        IsBuiltIn = IsBuiltIn,
        Colors = Colors.Clone(),
        Typography = Typography.Clone(),
    };
}

/// <summary>配色表。所有值均为 #RRGGBB 或 #AARRGGBB。</summary>
public sealed class ThemePalette
{
    // ---- 界面 ----
    public string WindowBg { get; set; } = "#FFFFFFFF";
    public string ToolbarBg { get; set; } = "#FFF6F8FA";
    public string ToolbarBorder { get; set; } = "#FFE1E4E8";
    public string ToolbarText { get; set; } = "#FF57606A";
    public string ToolbarTextHover { get; set; } = "#FF0969DA";
    public string Accent { get; set; } = "#FF0969DA";
    public string SidebarBg { get; set; } = "#FFF6F8FA";
    public string SidebarBorder { get; set; } = "#FFE1E4E8";
    public string SidebarText { get; set; } = "#FF57606A";
    public string SidebarTextActive { get; set; } = "#FF0969DA";
    public string SidebarHoverBg { get; set; } = "#FFE8EDF2";

    /// <summary>
    /// 侧栏顶部「页签条」底色。
    /// 留空（默认）表示由主题底色自动派生：亮色往窗口底色方向提亮，暗色轻微提亮。
    /// 之所以不写死，是为了让每套主题都得到「和自己底色同一色系、只差几个色号」的结果。
    /// </summary>
    public string SidebarHeaderBg { get; set; } = string.Empty;

    /// <summary>
    /// 侧栏「当前项」（选中的页签 / 当前章节）底色。留空表示自动派生。
    /// </summary>
    public string SidebarActiveBg { get; set; } = string.Empty;

    public string StatusBarBg { get; set; } = "#FFF6F8FA";
    public string StatusBarText { get; set; } = "#FF8B949E";
    public string Scrim { get; set; } = "#66000000";
    public string PanelBg { get; set; } = "#FFFFFFFF";
    public string PanelBorder { get; set; } = "#FFD0D7DE";
    public string ControlBg { get; set; } = "#FFF6F8FA";
    public string ControlBorder { get; set; } = "#FFD0D7DE";

    // ---- 正文 ----
    public string EditorBg { get; set; } = "#FFFFFFFF";
    public string Text { get; set; } = "#FF24292F";
    public string TextSecondary { get; set; } = "#FF57606A";
    public string Heading { get; set; } = "#FF1F2328";
    public string HeadingBorder { get; set; } = "#FFEAEEF2";
    public string Link { get; set; } = "#FF0969DA";
    public string Rule { get; set; } = "#FFD8DEE4";
    public string Selection { get; set; } = "#FFB6DBFF";

    // ---- 行内代码 / 代码块 ----
    public string InlineCodeText { get; set; } = "#FFCF222E";
    public string CodeBg { get; set; } = "#FFF6F8FA";
    public string CodeBorder { get; set; } = "#FFE1E4E8";
    public string CodeText { get; set; } = "#FF24292F";
    public string CodeHeaderText { get; set; } = "#FF8B949E";

    // ---- 引用 ----
    public string QuoteBg { get; set; } = "#00000000";
    public string QuoteText { get; set; } = "#FF57606A";
    public string QuoteBorder { get; set; } = "#FFD0D7DE";

    // ---- 表格 ----
    public string TableBorder { get; set; } = "#FFD0D7DE";
    public string TableHeaderBg { get; set; } = "#FFF6F8FA";
    public string TableHeaderText { get; set; } = "#FF1F2328";
    public string TableStripeBg { get; set; } = "#00000000";

    // ---- 语法着色 ----
    public string SyntaxKeyword { get; set; } = "#FFCF222E";
    public string SyntaxType { get; set; } = "#FF953800";
    public string SyntaxString { get; set; } = "#FF0A3069";
    public string SyntaxNumber { get; set; } = "#FF0550AE";
    public string SyntaxComment { get; set; } = "#FF6E7781";
    public string SyntaxFunction { get; set; } = "#FF8250DF";
    public string SyntaxOperator { get; set; } = "#FF0550AE";
    public string SyntaxPunctuation { get; set; } = "#FF57606A";
    public string SyntaxVariable { get; set; } = "#FF24292F";
    public string SyntaxBuiltin { get; set; } = "#FF0550AE";

    public ThemePalette Clone()
    {
        var json = JsonSerializer.Serialize(this, ThemeJson.Options);
        return JsonSerializer.Deserialize<ThemePalette>(json, ThemeJson.Options) ?? new ThemePalette();
    }

    /// <summary>取语法着色对应的颜色。</summary>
    public string ColorFor(SyntaxTokenKind kind) => kind switch
    {
        SyntaxTokenKind.Keyword => SyntaxKeyword,
        SyntaxTokenKind.Type => SyntaxType,
        SyntaxTokenKind.String => SyntaxString,
        SyntaxTokenKind.Number => SyntaxNumber,
        SyntaxTokenKind.Comment => SyntaxComment,
        SyntaxTokenKind.Function => SyntaxFunction,
        SyntaxTokenKind.Operator => SyntaxOperator,
        SyntaxTokenKind.Punctuation => SyntaxPunctuation,
        SyntaxTokenKind.Variable => SyntaxVariable,
        SyntaxTokenKind.Builtin => SyntaxBuiltin,
        _ => CodeText,
    };
}

/// <summary>排版参数（与平台无关，字号单位 pt/dp 由渲染层换算）。</summary>
public sealed class ThemeTypography
{
    /// <summary>正文字号基准（pt）。</summary>
    public double BaseFontSize { get; set; } = 16.5;

    /// <summary>行高倍数。</summary>
    public double LineHeight { get; set; } = 1.75;

    /// <summary>正文最大宽度（pt），0 表示不限制。</summary>
    public double ContentMaxWidth { get; set; } = 780;

    /// <summary>阅读区内边距（pt）。</summary>
    public double ContentPadding { get; set; } = 28;

    /// <summary>正文无语体（系统字体族名，留空用平台默认）。</summary>
    public string? FontFamily { get; set; }

    /// <summary>等宽字体族名（留空用平台默认等宽）。</summary>
    public string? MonoFontFamily { get; set; }

    /// <summary>标题字号 = BaseFontSize × HeadingScale[level-1]。</summary>
    public List<double> HeadingScale { get; set; } = new() { 1.92, 1.55, 1.28, 1.12, 1.0, 0.94 };

    /// <summary>标题上间距倍数。</summary>
    public double HeadingMarginTop { get; set; } = 1.6;

    /// <summary>标题下间距倍数。</summary>
    public double HeadingMarginBottom { get; set; } = 0.5;

    /// <summary>段落间距倍数。</summary>
    public double ParagraphSpacing { get; set; } = 0.72;

    /// <summary>表格单元格内边距。</summary>
    public double TableCellPadding { get; set; } = 7;

    public ThemeTypography Clone() => new()
    {
        BaseFontSize = BaseFontSize,
        LineHeight = LineHeight,
        ContentMaxWidth = ContentMaxWidth,
        ContentPadding = ContentPadding,
        FontFamily = FontFamily,
        MonoFontFamily = MonoFontFamily,
        HeadingScale = new List<double>(HeadingScale),
        HeadingMarginTop = HeadingMarginTop,
        HeadingMarginBottom = HeadingMarginBottom,
        ParagraphSpacing = ParagraphSpacing,
        TableCellPadding = TableCellPadding,
    };
}

public static class ThemeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // 非有限 double（NaN/Infinity）降级为 null，绝不让一次序列化失败
        // 拖垮整份配置的保存。详见 FiniteDoubleConverter 的说明。
        Converters = { new FiniteDoubleConverter() },
    };
}

public enum SyntaxTokenKind
{
    Plain,
    Keyword,
    Type,
    String,
    Number,
    Comment,
    Function,
    Operator,
    Punctuation,
    Variable,
    Builtin,
}
