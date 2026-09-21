// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Rendering/RenderContext.cs
//  说明：渲染上下文。把「主题 + 用户个性化设置」收敛成渲染层唯一依赖的
//        颜色/字号/间距换算中心，避免颜色字符串在各处反复解析。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  正文宽度改为按窗口宽度自适应（新增 AvailableWidth），
//                     解决「最大化后正文只占窗口三分之一、两侧大片空白」
// -----------------------------------------------------------------------------

using MD.Models;
using MD.Services;

namespace MD.Rendering;

/// <summary>颜色解析缓存（同一主题下颜色重复使用率极高）。</summary>
public static class Palette
{
    private static readonly Dictionary<string, Color> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static Color Get(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Colors.Black;

        lock (Gate)
        {
            if (Cache.TryGetValue(hex, out var cached))
                return cached;

            Color color;
            try
            {
                color = Color.FromArgb(ThemeCatalog.NormalizeHex(hex));
            }
            catch
            {
                color = Colors.Black;
            }

            if (Cache.Count > 512)
                Cache.Clear();
            Cache[hex] = color;
            return color;
        }
    }

    public static Color WithAlpha(Color color, float alpha) => color.WithAlpha(alpha);
}

/// <summary>平台字体族解析。</summary>
public static class Fonts
{
    public static string DefaultBody { get; } =
#if WINDOWS
        "Segoe UI";
#elif ANDROID
        "sans-serif";
#elif MACCATALYST || IOS
        "Helvetica Neue";
#else
        "sans-serif";
#endif

    public static string DefaultMono { get; } =
#if WINDOWS
        "Consolas";
#elif ANDROID
        "monospace";
#elif MACCATALYST || IOS
        "Menlo";
#else
        "monospace";
#endif

    public static string Body(string? family) =>
        string.IsNullOrWhiteSpace(family) ? DefaultBody : family!;

    public static string Mono(string? family) =>
        string.IsNullOrWhiteSpace(family) ? DefaultMono : family!;
}

/// <summary>渲染上下文。</summary>
public sealed class RenderContext
{
    public ThemeDefinition Theme { get; private set; }
    public AppSettings Settings { get; private set; }

    /// <summary>当前文档所在目录，用于解析相对路径图片。</summary>
    public string BaseDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 阅读区当前可用宽度（由页面在尺寸变化时写入，0 表示尚未测量）。
    /// 正文宽度自适应依赖它，否则无法判断「窗口到底有多宽」。
    /// </summary>
    public double AvailableWidth { get; set; }

    /// <summary>链接点击回调（http/https → 外部浏览器；相对路径 → 打开文件）。</summary>
    public Func<string, Task>? LinkHandler { get; set; }

    /// <summary>轻提示回调。</summary>
    public Action<string>? Notify { get; set; }

    /// <summary>每个块被重建时触发（用于调试 / 统计）。</summary>
    public event EventHandler? Invalidated;

    public RenderContext(ThemeDefinition theme, AppSettings settings)
    {
        Theme = theme;
        Settings = settings;
    }

    public void Update(ThemeDefinition theme, AppSettings settings)
    {
        Theme = theme;
        Settings = settings;
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    // ---------------- 尺寸换算 ----------------

    public ThemeTypography Type => Theme.Typography;

    /// <summary>正文字号。</summary>
    public double BodySize => Math.Clamp(Type.BaseFontSize + Settings.Reading.FontSizeDelta, 9, 48);

    /// <summary>行高倍数。</summary>
    public double LineHeight => Math.Clamp(Type.LineHeight + Settings.Reading.LineHeightDelta, 1.0, 3.2);

    public double HeadingSize(int level)
    {
        var scale = Type.HeadingScale;
        int idx = Math.Clamp(level - 1, 0, scale.Count - 1);
        return BodySize * scale[idx];
    }

    public double ParagraphSpacing => BodySize * Type.ParagraphSpacing;

    public double HeadingTop(int level) => BodySize * Type.HeadingMarginTop * level switch
    {
        1 => 1.35,
        2 => 1.05,
        3 => 0.85,
        _ => 0.7,
    };

    public double HeadingBottom(int level) => BodySize * Type.HeadingMarginBottom * (level <= 2 ? 1.2 : 0.8);

    /// <summary>缩进单位（列表 / 引用嵌套）。</summary>
    public double IndentUnit => BodySize * 1.5;

    /// <summary>
    /// 正文列宽。返回 0 表示「不限宽，铺满可用区域」。
    ///
    /// 以前这里直接返回主题的 ContentMaxWidth，于是在 2560 宽的屏幕上正文只有
    /// 820pt、两侧留下大片空白，最大化之后反而更难读 —— 这正是「全屏可读区域太小」
    /// 的原因。现在改为：窗口越宽，允许正文列按比例放宽，但设 1.55 倍上限，
    /// 保证行长仍落在可读区间内（不会宽到眼睛追不上行首）。
    /// </summary>
    public double ContentWidth
    {
        get
        {
            switch (Settings.Reading.ContentWidthMode)
            {
                case "narrow": return 700;
                case "standard": return 900;
                case "wide": return 1150;
                case "full": return 0;
            }

            // auto：跟随主题的舒适宽度，并随窗口宽度自适应放宽
            var comfort = Type.ContentMaxWidth;
            if (comfort <= 0)
                return 0;

            var available = AvailableWidth - ContentPadding * 2;
            if (available <= 0 || available <= comfort)
                return 0;

            var factor = Math.Clamp(available / (comfort + 420), 1.0, 1.55);
            return Math.Round(comfort * factor);
        }
    }

    public double ContentPadding => Type.ContentPadding;

    public string BodyFont => Fonts.Body(Type.FontFamily);

    public string MonoFont => Fonts.Mono(Type.MonoFontFamily);

    // ---------------- 常用颜色快捷方式 ----------------

    public Color CWindowBg => Palette.Get(Theme.Colors.WindowBg);
    public Color CToolbarBg => Palette.Get(Theme.Colors.ToolbarBg);
    public Color CToolbarBorder => Palette.Get(Theme.Colors.ToolbarBorder);
    public Color CToolbarText => Palette.Get(Theme.Colors.ToolbarText);
    public Color CToolbarTextHover => Palette.Get(Theme.Colors.ToolbarTextHover);
    public Color CAccent => Palette.Get(Theme.Colors.Accent);
    public Color CSidebarBg => Palette.Get(Theme.Colors.SidebarBg);
    public Color CSidebarBorder => Palette.Get(Theme.Colors.SidebarBorder);
    public Color CSidebarText => Palette.Get(Theme.Colors.SidebarText);
    public Color CSidebarTextActive => Palette.Get(Theme.Colors.SidebarTextActive);
    public Color CSidebarHoverBg => Palette.Get(Theme.Colors.SidebarHoverBg);
    public Color CSidebarHeaderBg => Palette.Get(Theme.Colors.SidebarHeaderBg);
    public Color CSidebarActiveBg => Palette.Get(Theme.Colors.SidebarActiveBg);
    public Color CStatusBarBg => Palette.Get(Theme.Colors.StatusBarBg);
    public Color CStatusBarText => Palette.Get(Theme.Colors.StatusBarText);
    public Color CScrim => Palette.Get(Theme.Colors.Scrim);
    public Color CPanelBg => Palette.Get(Theme.Colors.PanelBg);
    public Color CPanelBorder => Palette.Get(Theme.Colors.PanelBorder);
    public Color CControlBg => Palette.Get(Theme.Colors.ControlBg);
    public Color CControlBorder => Palette.Get(Theme.Colors.ControlBorder);
    public Color CEditorBg => Palette.Get(Theme.Colors.EditorBg);
    public Color CText => Palette.Get(Theme.Colors.Text);
    public Color CTextSecondary => Palette.Get(Theme.Colors.TextSecondary);
    public Color CHeading => Palette.Get(Theme.Colors.Heading);
    public Color CHeadingBorder => Palette.Get(Theme.Colors.HeadingBorder);
    public Color CLink => Palette.Get(Theme.Colors.Link);
    public Color CRule => Palette.Get(Theme.Colors.Rule);
    public Color CInlineCodeText => Palette.Get(Theme.Colors.InlineCodeText);
    public Color CCodeBg => Palette.Get(Theme.Colors.CodeBg);
    public Color CCodeBorder => Palette.Get(Theme.Colors.CodeBorder);
    public Color CCodeText => Palette.Get(Theme.Colors.CodeText);
    public Color CCodeHeaderText => Palette.Get(Theme.Colors.CodeHeaderText);
    public Color CQuoteBg => Palette.Get(Theme.Colors.QuoteBg);
    public Color CQuoteText => Palette.Get(Theme.Colors.QuoteText);
    public Color CQuoteBorder => Palette.Get(Theme.Colors.QuoteBorder);
    public Color CTableBorder => Palette.Get(Theme.Colors.TableBorder);
    public Color CTableHeaderBg => Palette.Get(Theme.Colors.TableHeaderBg);
    public Color CTableHeaderText => Palette.Get(Theme.Colors.TableHeaderText);
    public Color CTableStripeBg => Palette.Get(Theme.Colors.TableStripeBg);

    public Color SyntaxColor(SyntaxTokenKind kind) => Palette.Get(Theme.Colors.ColorFor(kind));
}
