// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Controls/UiKit.cs
//  说明：一小组手写的扁平控件构造函数。
//        本项目界面全部用 C# 构建（不用 XAML），原因：
//          1) 主题热切换需要「按颜色重建整棵可视树」，命令式代码比 XAML 绑定更直接可靠
//          2) 单文件发布时不需要打包 XAML 资源
//          3) 虚拟化列表里每个块都是动态生成的，用 C# 组合天然合适
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using MD.Rendering;
using Microsoft.Maui.Controls.Shapes;

namespace MD.Controls;

/// <summary>手写控件工具箱。</summary>
public static class UiKit
{
    public static RoundRectangle Round(double radius) => new() { CornerRadius = new CornerRadius(radius) };

    /// <summary>扁平文字按钮（带悬停反馈）。</summary>
    public static Border FlatButton(
        string text,
        RenderContext ctx,
        Func<Task> onClick,
        double fontSize = 12.5,
        bool primary = false,
        double minWidth = 0,
        double horizontalPadding = 12)
    {
        var label = new Label
        {
            Text = text,
            FontSize = fontSize,
            FontFamily = ctx.BodyFont,
            TextColor = primary ? Colors.White : ctx.CToolbarText,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
        };

        var border = new Border
        {
            Content = label,
            BackgroundColor = primary ? ctx.CAccent : Colors.Transparent,
            Stroke = primary ? Colors.Transparent : Colors.Transparent,
            StrokeThickness = primary ? 0 : 1,
            StrokeShape = Round(primary ? 7 : 7),
            Padding = new Thickness(horizontalPadding, 6),
            MinimumWidthRequest = minWidth,
            HeightRequest = -1,
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            try { await onClick(); } catch { /* 忽略交互异常 */ }
        };
        border.GestureRecognizers.Add(tap);

        AddHover(border, ctx, () =>
        {
            if (primary)
                border.BackgroundColor = ctx.CAccent.WithAlpha(0.86f);
            else
                border.BackgroundColor = ctx.CSidebarHoverBg;
        }, () =>
        {
            border.BackgroundColor = primary ? ctx.CAccent : Colors.Transparent;
        });

        return border;
    }

    /// <summary>字形图标按钮（☰ ⚙ ＋ － 等，不依赖字体图标资源）。</summary>
    public static Border GlyphButton(
        string glyph,
        string tooltip,
        RenderContext ctx,
        Func<Task> onClick,
        double size = 30,
        double fontSize = 15)
    {
        var label = new Label
        {
            Text = glyph,
            FontSize = fontSize,
            FontFamily = ctx.BodyFont,
            TextColor = ctx.CToolbarText,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
        };

        var border = new Border
        {
            Content = label,
            BackgroundColor = Colors.Transparent,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = Round(7),
            WidthRequest = size,
            HeightRequest = size,
            Padding = 0,
        };

        SemanticProperties.SetDescription(border, tooltip);

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            try { await onClick(); } catch { /* 忽略交互异常 */ }
        };
        border.GestureRecognizers.Add(tap);

        AddHover(border, ctx,
            () =>
            {
                border.BackgroundColor = ctx.CSidebarHoverBg;
                label.TextColor = ctx.CToolbarTextHover;
            },
            () =>
            {
                border.BackgroundColor = Colors.Transparent;
                label.TextColor = ctx.CToolbarText;
            });

        return border;
    }

    private static void AddHover(Border border, RenderContext ctx, Action onEnter, Action onExit)
    {
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => onEnter();
        pointer.PointerExited += (_, _) => onExit();
        border.GestureRecognizers.Add(pointer);
    }

    /// <summary>一条水平细分隔线。</summary>
    public static BoxView Hairline(Color color, double thickness = 1, double height = 1) => new()
    {
        Color = color,
        HeightRequest = height,
        HorizontalOptions = LayoutOptions.Fill,
    };

    /// <summary>带边框的圆角容器。</summary>
    public static Border Card(
        View content,
        Color background,
        Color borderColor,
        double radius = 8,
        double strokeThickness = 1,
        Thickness? padding = null) => new()
        {
            Content = content,
            BackgroundColor = background,
            Stroke = borderColor,
            StrokeThickness = strokeThickness,
            StrokeShape = Round(radius),
            Padding = padding ?? new Thickness(0),
        };

    /// <summary>正文级标签（统一字体/行高/颜色，避免各处重复设置）。</summary>
    public static Label Text(
        string text,
        RenderContext ctx,
        double? fontSize = null,
        Color? color = null,
        bool bold = false,
        double? lineHeight = null) => new()
        {
            Text = text,
            FontSize = fontSize ?? ctx.BodySize,
            FontFamily = ctx.BodyFont,
            TextColor = color ?? ctx.CText,
            LineHeight = lineHeight ?? ctx.LineHeight,
            FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
        };

    /// <summary>空状态占位。</summary>
    public static Label Placeholder(string text, RenderContext ctx, double factor = 1.0) => new()
    {
        Text = text,
        FontSize = ctx.BodySize * factor,
        FontFamily = ctx.BodyFont,
        TextColor = ctx.CTextSecondary,
        LineHeight = ctx.LineHeight,
    };
}
