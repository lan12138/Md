// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Rendering/InlineRenderer.cs
//  说明：行内元素 -> FormattedString。
//        说明：MAUI 的 Span 不支持背景色，因此行内代码用「等宽 + 专属前景色」
//        表达（而不是像 HTML 那样铺底色），在明暗主题下都比强行画底色更干净。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using MD.Markdown;
using MD.Models;

namespace MD.Rendering;

/// <summary>行内样式上下文（随嵌套累积）。</summary>
public struct InlineStyle
{
    public bool Bold;
    public bool Italic;
    public bool Strike;
    public bool Underline;
    public bool Sub;
    public bool Sup;
    public bool Mono;
    public double Factor;
    public Color? ColorOverride;
    public int LinkDepth;
    public string? LinkUrl;
    public bool Secondary;

    public static InlineStyle Default => new() { Factor = 1.0 };
}

public static class InlineRenderer
{
    public static FormattedString Build(
        IReadOnlyList<InlineNode> nodes,
        RenderContext ctx,
        InlineStyle style,
        double baseSize,
        Func<string, Task>? linkHandler = null)
    {
        var fs = new FormattedString();
        Append(fs, nodes, ctx, style, baseSize, linkHandler);
        return fs;
    }

    public static void Append(
        FormattedString target,
        IReadOnlyList<InlineNode> nodes,
        RenderContext ctx,
        InlineStyle style,
        double baseSize,
        Func<string, Task>? linkHandler)
    {
        if (nodes is null || nodes.Count == 0)
            return;

        foreach (var node in nodes)
            AppendNode(target, node, ctx, style, baseSize, linkHandler);
    }

    private static void AppendNode(
        FormattedString target,
        InlineNode node,
        RenderContext ctx,
        InlineStyle style,
        double baseSize,
        Func<string, Task>? linkHandler)
    {
        switch (node)
        {
            case TextNode text:
                AddSpan(target, text.Text, ctx, style, baseSize, linkHandler);
                break;

            case CodeNode code:
            {
                var s = style;
                s.Mono = true;
                s.Factor = style.Factor * 0.92;
                s.ColorOverride = ctx.CInlineCodeText;
                AddSpan(target, code.Text, ctx, s, baseSize, linkHandler);
                break;
            }

            case EmphasisNode emphasis:
            {
                var s = style;
                s.Bold |= emphasis.Bold;
                s.Italic |= emphasis.Italic;
                s.Strike |= emphasis.Strike;
                s.Underline |= emphasis.IsInserted;
                s.Sub |= emphasis.IsSub;
                s.Sup |= emphasis.IsSup;
                if (emphasis.IsMark)
                {
                    s.Bold = true;
                    s.ColorOverride ??= ctx.CAccent;
                }
                if (emphasis.IsSub || emphasis.IsSup)
                    s.Factor = style.Factor * 0.82;

                Append(target, emphasis.Children, ctx, s, baseSize, linkHandler);
                break;
            }

            case LinkNode link:
            {
                var s = style;
                s.Underline = true;
                s.LinkDepth++;
                s.LinkUrl = link.Url;
                s.ColorOverride = ctx.CLink;
                if (link.Children.Count == 0)
                    AddSpan(target, link.Url, ctx, s, baseSize, linkHandler);
                else
                    Append(target, link.Children, ctx, s, baseSize, linkHandler);
                break;
            }

            case ImageNode image:
            {
                var s = style;
                s.Underline = true;
                s.ColorOverride = ctx.CLink;
                s.Mono = true;
                s.Factor = style.Factor * 0.92;
                var label = string.IsNullOrWhiteSpace(image.Alt) ? "🖼 图片" : $"🖼 {image.Alt}";
                var span = CreateSpan(label, ctx, s, baseSize);
                if (linkHandler is not null && !string.IsNullOrEmpty(image.Url))
                    span.GestureRecognizers.Add(CreateTap(image.Url, linkHandler));
                target.Spans.Add(span);
                break;
            }

            case LineBreakNode:
                AddSpan(target, "\n", ctx, style, baseSize, null);
                break;

            case RawHtmlInlineNode html:
            {
                var s = style;
                s.Mono = true;
                s.Factor = style.Factor * 0.9;
                s.ColorOverride = ctx.CTextSecondary;
                AddSpan(target, html.Html, ctx, s, baseSize, null);
                break;
            }
        }
    }

    private static void AddSpan(
        FormattedString target,
        string? text,
        RenderContext ctx,
        InlineStyle style,
        double baseSize,
        Func<string, Task>? linkHandler)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var span = CreateSpan(text, ctx, style, baseSize);

        if (linkHandler is not null && !string.IsNullOrEmpty(style.LinkUrl))
            span.GestureRecognizers.Add(CreateTap(style.LinkUrl!, linkHandler));

        target.Spans.Add(span);
    }

    private static Span CreateSpan(string text, RenderContext ctx, InlineStyle style, double baseSize)
    {
        var span = new Span
        {
            Text = text,
            FontSize = Math.Max(6, baseSize * style.Factor),
            FontFamily = style.Mono ? ctx.MonoFont : ctx.BodyFont,
            TextColor = style.ColorOverride
                         ?? (style.Secondary ? ctx.CTextSecondary : ctx.CText),
        };

        var attrs = FontAttributes.None;
        if (style.Bold) attrs |= FontAttributes.Bold;
        if (style.Italic) attrs |= FontAttributes.Italic;
        span.FontAttributes = attrs;

        var decorations = TextDecorations.None;
        if (style.Underline) decorations |= TextDecorations.Underline;
        if (style.Strike) decorations |= TextDecorations.Strikethrough;
        span.TextDecorations = decorations;

        return span;
    }

    private static TapGestureRecognizer CreateTap(string url, Func<string, Task> handler)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            try
            {
                await handler(url);
            }
            catch
            {
                // 链接打开失败不影响阅读
            }
        };
        return tap;
    }

    // -----------------------------------------------------------------------
    // 代码块着色
    // -----------------------------------------------------------------------

    /// <summary>把代码块渲染为带语法着色的 FormattedString（可选行号）。</summary>
    public static FormattedString BuildCode(
        string code,
        string language,
        RenderContext ctx,
        double fontSize,
        bool lineNumbers)
    {
        var fs = new FormattedString();
        var spans = SyntaxHighlighter.Highlight(code, language);

        if (!lineNumbers)
        {
            foreach (var sp in spans)
                fs.Spans.Add(MakeCodeSpan(sp.Text, ctx, ctx.SyntaxColor(sp.Kind), fontSize));
            return fs;
        }

        // 行号：等宽字体下用空格补齐即可对齐，无需额外列
        int totalLines = 1;
        foreach (var sp in spans)
        {
            foreach (var ch in sp.Text)
                if (ch == '\n') totalLines++;
        }
        int width = totalLines.ToString().Length;
        var numberColor = ctx.CCodeHeaderText;
        int line = 1;

        fs.Spans.Add(MakeCodeSpan(Pad(line, width) + "  ", ctx, numberColor, fontSize));

        foreach (var sp in spans)
        {
            var color = ctx.SyntaxColor(sp.Kind);
            var text = sp.Text;
            int start = 0;
            while (true)
            {
                int nl = text.IndexOf('\n', start);
                if (nl < 0)
                {
                    if (start < text.Length)
                        fs.Spans.Add(MakeCodeSpan(text[start..], ctx, color, fontSize));
                    break;
                }

                if (nl > start)
                    fs.Spans.Add(MakeCodeSpan(text[start..nl], ctx, color, fontSize));

                fs.Spans.Add(MakeCodeSpan("\n", ctx, color, fontSize));
                line++;
                fs.Spans.Add(MakeCodeSpan(Pad(line, width) + "  ", ctx, numberColor, fontSize));
                start = nl + 1;
            }
        }

        return fs;
    }

    private static string Pad(int value, int width)
    {
        var s = value.ToString();
        return s.Length >= width ? s : new string(' ', width - s.Length) + s;
    }

    private static Span MakeCodeSpan(string text, RenderContext ctx, Color color, double fontSize) => new()
    {
        Text = text,
        FontFamily = ctx.MonoFont,
        FontSize = fontSize,
        TextColor = color,
    };
}
