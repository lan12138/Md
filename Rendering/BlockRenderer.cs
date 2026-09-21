// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Rendering/BlockRenderer.cs
//  说明：块级元素 -> 原生控件。
//
//        关键设计：
//        · 不渲染 HTML、不嵌入 WebView。每个块直接映射为 MAUI 原生控件，
//          滚动、选择、滚动条、DPI 缩放都交给平台，性能和观感都优于内嵌浏览器。
//        · 每个块是「自包含」的：只依赖 RenderContext，主题切换时原地重建即可。
//        · 缩进由「容器嵌套」自然形成（列表的符号列 / 引用的竖条），
//          不做 depth × 固定值的机械缩进，避免多层嵌套后过度右移。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using MD.Controls;
using MD.Markdown;
using MD.Models;

namespace MD.Rendering;

public static class BlockRenderer
{
    public static View Render(MdBlock block, RenderContext ctx)
    {
        try
        {
            return block switch
            {
                HeadingBlock h => RenderHeading(h, ctx),
                ParagraphBlock p => RenderParagraph(p, ctx),
                QuoteBlock q => RenderQuote(q, ctx),
                ListBlock l => RenderList(l, ctx),
                CodeBlock c => RenderCode(c, ctx),
                TableBlock t => RenderTable(t, ctx),
                ThematicBreakBlock => RenderRule(ctx),
                ImageBlock i => RenderImage(i, ctx),
                HtmlBlock x => RenderHtmlBlock(x, ctx),
                _ => new BoxView { HeightRequest = 0 },
            };
        }
        catch (Exception ex)
        {
            // 单个块渲染失败不应该让整篇文档打不开
            return new Label
            {
                Text = $"[渲染失败：{ex.Message}]",
                FontSize = ctx.BodySize * 0.85,
                TextColor = ctx.CTextSecondary,
                FontFamily = ctx.BodyFont,
            };
        }
    }

    // =======================================================================
    // 标题
    // =======================================================================

    private static View RenderHeading(HeadingBlock heading, RenderContext ctx)
    {
        var size = ctx.HeadingSize(heading.Level);
        var style = InlineStyle.Default;
        style.Bold = true;
        style.ColorOverride = ctx.CHeading;
        // 标题里的行内代码不应抢走标题的视觉重心
        style.Mono = false;

        var formatted = new FormattedString();
        InlineRenderer.Append(formatted, heading.Inlines, ctx, style, size, ctx.LinkHandler);
        if (formatted.Spans.Count == 0)
            formatted.Spans.Add(new Span { Text = heading.Text, FontSize = size });

        foreach (var span in formatted.Spans)
        {
            span.FontSize = size;
            span.FontAttributes |= FontAttributes.Bold;
            if (span.TextColor == ctx.CText || span.TextColor == ctx.CInlineCodeText)
                span.TextColor = ctx.CHeading;
        }

        var label = new Label
        {
            FormattedText = formatted,
            FontFamily = ctx.BodyFont,
            LineHeight = Math.Clamp(ctx.LineHeight * 0.95, 1.05, 2.4),
            HorizontalOptions = LayoutOptions.Fill,
        };

        bool withRule = heading.Level <= 2;
        var top = ctx.HeadingTop(heading.Level);
        var bottom = ctx.HeadingBottom(heading.Level);
        if (heading.Level == 1) bottom *= 1.35;

        View result;
        if (withRule)
        {
            var grid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                },
                RowSpacing = heading.Level == 1 ? 10 : 8,
            };
            grid.Add(label, 0, 0);
            grid.Add(new BoxView
            {
                Color = ctx.CHeadingBorder,
                HeightRequest = 1,
                HorizontalOptions = LayoutOptions.Fill,
            }, 0, 1);
            result = grid;
        }
        else
        {
            result = label;
        }

        result.Margin = new Thickness(0, top, 0, bottom);
        return result;
    }

    // =======================================================================
    // 段落
    // =======================================================================

    private static View RenderParagraph(ParagraphBlock paragraph, RenderContext ctx)
    {
        var formatted = InlineRenderer.Build(
            paragraph.Inlines, ctx, InlineStyle.Default, ctx.BodySize, ctx.LinkHandler);

        var label = new Label
        {
            FormattedText = formatted,
            FontFamily = ctx.BodyFont,
            FontSize = ctx.BodySize,
            LineHeight = ctx.LineHeight,
            TextColor = ctx.CText,
            HorizontalOptions = LayoutOptions.Fill,
        };

        label.Margin = new Thickness(0, 0, 0, ctx.ParagraphSpacing);
        return label;
    }

    // =======================================================================
    // 引用
    // =======================================================================

    private static View RenderQuote(QuoteBlock quote, RenderContext ctx)
    {
        var stack = new VerticalStackLayout { Spacing = 0 };
        foreach (var child in quote.Children)
            stack.Add(Render(child, ctx));

        var bar = new BoxView
        {
            Color = ctx.CQuoteBorder,
            WidthRequest = 3.5,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(3.5)),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 0,
        };
        grid.Add(bar, 0, 0);
        grid.Add(new ContentView
        {
            Content = stack,
            Padding = new Thickness(14, 2, 0, 2),
            VerticalOptions = LayoutOptions.Fill,
        }, 1, 0);

        var content = (View)grid;
        if (ctx.CQuoteBg.Alpha > 0.001f)
        {
            content = new Border
            {
                Content = grid,
                BackgroundColor = ctx.CQuoteBg,
                Stroke = Colors.Transparent,
                StrokeThickness = 0,
                StrokeShape = UiKit.Round(6),
                Padding = new Thickness(0, 6, 8, 6),
            };
        }

        content.Margin = new Thickness(0, ctx.BodySize * 0.15, 0, ctx.ParagraphSpacing);
        return content;
    }

    // =======================================================================
    // 列表
    // =======================================================================

    private static View RenderList(ListBlock list, RenderContext ctx)
    {
        var stack = new VerticalStackLayout { Spacing = ctx.BodySize * 0.3 };

        foreach (var item in list.Items)
        {
            var content = new VerticalStackLayout { Spacing = 0 };
            foreach (var child in item.Children)
                content.Add(Render(child, ctx));

            // 列表项内的最后一个块去掉底边距，否则行间会出现空洞
            if (content.Children.Count > 0 && content.Children[^1] is View lastChild)
                lastChild.Margin = new Thickness(lastChild.Margin.Left, lastChild.Margin.Top, lastChild.Margin.Right, 0);

            var markerColor = item.IsTask && item.IsChecked ? ctx.CAccent : ctx.CTextSecondary;
            var marker = new Label
            {
                Text = item.Marker,
                FontSize = ctx.BodySize * (item.IsTask ? 1.05 : 0.95),
                FontFamily = list.IsOrdered ? ctx.BodyFont : ctx.MonoFont,
                TextColor = markerColor,
                LineHeight = ctx.LineHeight,
                HorizontalTextAlignment = item.IsTask ? TextAlignment.Center : TextAlignment.End,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, item.IsTask ? 0 : ctx.BodySize * 0.12, 0, 0),
                MinimumWidthRequest = list.IsOrdered ? ctx.BodySize * 1.35 : ctx.BodySize * 0.9,
                LineBreakMode = LineBreakMode.NoWrap,
            };

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                },
                ColumnSpacing = ctx.BodySize * 0.45,
            };
            row.Add(marker, 0, 0);
            row.Add(content, 1, 0);
            stack.Add(row);
        }

        stack.Margin = new Thickness(0, ctx.BodySize * 0.1, 0, ctx.ParagraphSpacing);
        return stack;
    }

    // =======================================================================
    // 代码块
    // =======================================================================

    private static View RenderCode(CodeBlock code, RenderContext ctx)
    {
        double fontSize = Math.Clamp(ctx.BodySize * 0.9, 8, 30);
        double lineHeightRatio = Math.Clamp(ctx.LineHeight - 0.3, 1.2, 1.85);

        var label = new Label
        {
            FormattedText = InlineRenderer.BuildCode(code.Code, code.Language, ctx, fontSize, ctx.Settings.Reading.CodeLineNumbers),
            FontFamily = ctx.MonoFont,
            FontSize = fontSize,
            LineHeight = lineHeightRatio,
            TextColor = ctx.CCodeText,
            LineBreakMode = LineBreakMode.NoWrap,
        };

        var scroller = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = label,
            MinimumHeightRequest = fontSize * lineHeightRatio * 1.4,
            HorizontalOptions = LayoutOptions.Fill,
        };

        var body = new ContentView
        {
            Content = scroller,
            Padding = new Thickness(12, 8, 12, 10),
        };

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
        };
        grid.Add(BuildCodeHeader(code, ctx), 0, 0);
        grid.Add(body, 0, 1);

        return new Border
        {
            Content = grid,
            BackgroundColor = ctx.CCodeBg,
            Stroke = ctx.CCodeBorder,
            StrokeThickness = 1,
            StrokeShape = UiKit.Round(8),
            Padding = 0,
            Margin = new Thickness(0, ctx.BodySize * 0.2, 0, ctx.ParagraphSpacing),
        };
    }

    private static View BuildCodeHeader(CodeBlock code, RenderContext ctx)
    {
        var langText = string.IsNullOrWhiteSpace(code.Language) ? "text" : code.Language;
        var language = new Label
        {
            Text = langText,
            FontSize = Math.Clamp(ctx.BodySize * 0.72, 9, 16),
            FontFamily = ctx.MonoFont,
            TextColor = ctx.CCodeHeaderText,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
        };

        var copy = UiKit.FlatButton("复制", ctx, async () =>
        {
            try
            {
                await Clipboard.SetTextAsync(code.Code);
                ctx.Notify?.Invoke("代码已复制");
            }
            catch
            {
                ctx.Notify?.Invoke("复制失败");
            }
        }, Math.Clamp(ctx.BodySize * 0.72, 9, 15), horizontalPadding: 8);

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Padding = new Thickness(12, 6, 8, 2),
        };
        header.Add(language, 0, 0);
        header.Add(copy, 1, 0);
        return header;
    }

    // =======================================================================
    // 表格
    // =======================================================================

    private static View RenderTable(TableBlock table, RenderContext ctx)
    {
        int colCount = Math.Max(1, table.Columns.Count);
        double cellPadding = ctx.Type.TableCellPadding;
        var stripes = ctx.CTableStripeBg.Alpha > 0.001f;

        var weights = ComputeColumnWeights(table, colCount);

        var grid = new Grid
        {
            RowSpacing = 0,
            ColumnSpacing = 0,
        };
        for (int c = 0; c < colCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(weights[c], GridUnitType.Star)));
        for (int r = 0; r < table.Rows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            bool isLastRow = r == table.Rows.Count - 1;

            for (int c = 0; c < colCount; c++)
            {
                var inlines = c < row.Cells.Count ? row.Cells[c] : Array.Empty<InlineNode>();
                var style = InlineStyle.Default;
                style.Bold = row.IsHeader;
                style.ColorOverride = row.IsHeader ? ctx.CTableHeaderText : ctx.CText;

                var content = new Label
                {
                    FormattedText = InlineRenderer.Build(inlines, ctx, style, ctx.BodySize * 0.95, ctx.LinkHandler),
                    FontSize = ctx.BodySize * 0.95,
                    FontFamily = ctx.BodyFont,
                    LineHeight = Math.Clamp(ctx.LineHeight * 0.94, 1.1, 2.2),
                    TextColor = style.ColorOverride,
                    LineBreakMode = LineBreakMode.WordWrap,
                    HorizontalTextAlignment = table.Columns.Count > c
                        ? ToMauiAlignment(table.Columns[c].Alignment)
                        : TextAlignment.Start,
                };

                var bg = row.IsHeader
                    ? ctx.CTableHeaderBg
                    : stripes && r % 2 == 1
                        ? ctx.CTableStripeBg
                        : Colors.Transparent;

                var cell = new Border
                {
                    Content = content,
                    BackgroundColor = bg,
                    // 只画右 / 下边框：容器外框再补一圈，得到干净的 1px 网格
                    Stroke = ctx.CTableBorder,
                    StrokeThickness = 1,
                    StrokeShape = UiKit.Round(0),
                    Padding = new Thickness(cellPadding + 2, cellPadding, cellPadding + 2, cellPadding),
                };

                // 末列去掉右边线、末行去掉下边线
                if (c == colCount - 1 || isLastRow)
                {
                    cell.StrokeThickness = 0;
                }

                grid.Add(cell, c, r);
            }
        }

        return new Border
        {
            Content = grid,
            BackgroundColor = Colors.Transparent,
            Stroke = ctx.CTableBorder,
            StrokeThickness = 1,
            StrokeShape = UiKit.Round(6),
            Padding = 0,
            Margin = new Thickness(0, ctx.BodySize * 0.25, 0, ctx.ParagraphSpacing),
        };
    }

    /// <summary>
    /// 列宽权重：按各列最长单元格的字符数的平方根分配。
    /// 等分在「一列很短一列很长」时非常难看，这个近似够便宜也够像样。
    /// </summary>
    private static double[] ComputeColumnWeights(TableBlock table, int colCount)
    {
        var longest = new int[colCount];
        foreach (var row in table.Rows)
        {
            for (int c = 0; c < colCount && c < row.Cells.Count; c++)
            {
                int len = 0;
                foreach (var node in row.Cells[c])
                    len += ApproximateLength(node);
                if (len > longest[c])
                    longest[c] = len;
            }
        }

        var weights = new double[colCount];
        double total = 0;
        for (int c = 0; c < colCount; c++)
        {
            weights[c] = Math.Sqrt(Math.Clamp(longest[c], 2, 200));
            total += weights[c];
        }
        if (total <= 0)
        {
            for (int c = 0; c < colCount; c++)
                weights[c] = 1;
            return weights;
        }

        for (int c = 0; c < colCount; c++)
            weights[c] = Math.Max(0.6, weights[c] / total * colCount);
        return weights;
    }

    private static int ApproximateLength(InlineNode node) => node switch
    {
        TextNode t => VisualWidth(t.Text),
        CodeNode c => VisualWidth(c.Text),
        ImageNode => 8,
        LineBreakNode => 0,
        EmphasisNode e => e.Children.Sum(ApproximateLength),
        LinkNode l => l.Children.Sum(ApproximateLength),
        _ => 2,
    };

    private static int VisualWidth(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        int w = 0;
        foreach (var ch in text)
            w += ch > 0x2E80 ? 2 : 1; // 中日韩字符按双宽计算
        return w;
    }

    private static TextAlignment ToMauiAlignment(CellAlignment alignment) => alignment switch
    {
        CellAlignment.Center => TextAlignment.Center,
        CellAlignment.Right => TextAlignment.End,
        _ => TextAlignment.Start,
    };

    // =======================================================================
    // 分割线 / 图片 / HTML
    // =======================================================================

    private static View RenderRule(RenderContext ctx) => new BoxView
    {
        Color = ctx.CRule,
        HeightRequest = 1.6,
        HorizontalOptions = LayoutOptions.Fill,
        Margin = new Thickness(0, ctx.BodySize * 1.1, 0, ctx.BodySize * 1.1),
    };

    private static View RenderImage(ImageBlock image, RenderContext ctx)
    {
        var container = new Border
        {
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = UiKit.Round(8),
            Padding = 0,
            Margin = new Thickness(0, ctx.BodySize * 0.3, 0, ctx.ParagraphSpacing),
            HorizontalOptions = LayoutOptions.Fill,
        };

        if (!ctx.Settings.Reading.LoadImages)
        {
            container.BackgroundColor = ctx.CCodeBg;
            container.Stroke = ctx.CCodeBorder;
            container.StrokeThickness = 1;
            container.Content = new Label
            {
                Text = string.IsNullOrWhiteSpace(image.Alt) ? "图片（已关闭加载）" : $"图片（已关闭加载）：{image.Alt}",
                FontSize = ctx.BodySize * 0.85,
                TextColor = ctx.CCodeHeaderText,
                FontFamily = ctx.BodyFont,
                HorizontalTextAlignment = TextAlignment.Center,
                Padding = new Thickness(16, 24),
            };
            return container;
        }

        var source = ResolveImageSource(image.Url, ctx.BaseDirectory);
        if (source is null)
        {
            container.BackgroundColor = ctx.CCodeBg;
            container.Stroke = ctx.CCodeBorder;
            container.StrokeThickness = 1;
            container.Content = new Label
            {
                Text = $"无法加载图片：{image.Url}",
                FontSize = ctx.BodySize * 0.85,
                TextColor = ctx.CCodeHeaderText,
                FontFamily = ctx.BodyFont,
                HorizontalTextAlignment = TextAlignment.Center,
                Padding = new Thickness(16, 24),
            };
            return container;
        }

        var img = new Image
        {
            Source = source,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
        };

        if (!string.IsNullOrWhiteSpace(image.Alt))
            SemanticProperties.SetDescription(img, image.Alt);

        container.Content = img;

        if (ctx.LinkHandler is not null && !string.IsNullOrWhiteSpace(image.Url))
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                try { await ctx.LinkHandler(image.Url); } catch { /* 忽略 */ }
            };
            container.GestureRecognizers.Add(tap);
        }

        return container;
    }

    /// <summary>把 Markdown 里的图片地址解析为 ImageSource（支持 http/绝对/相对/data URI）。</summary>
    public static ImageSource? ResolveImageSource(string url, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        url = url.Trim();

        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                int comma = url.IndexOf(',');
                if (comma < 0)
                    return null;
                var payload = url[(comma + 1)..];
                if (url.AsSpan(0, comma).Contains("base64", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = Convert.FromBase64String(payload);
                    return ImageSource.FromStream(() => new MemoryStream(bytes));
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return ImageSource.FromUri(new Uri(url));

        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var local = new Uri(url).LocalPath;
                return File.Exists(local) ? ImageSource.FromFile(local) : null;
            }
            catch
            {
                return null;
            }
        }

        var decoded = Uri.UnescapeDataString(url);
        var path = Path.IsPathRooted(decoded)
            ? decoded
            : Path.GetFullPath(Path.Combine(baseDirectory, decoded));

        return File.Exists(path) ? ImageSource.FromFile(path) : null;
    }

    private static View RenderHtmlBlock(HtmlBlock html, RenderContext ctx)
    {
        var header = new Label
        {
            Text = "html",
            FontSize = Math.Clamp(ctx.BodySize * 0.72, 9, 15),
            FontFamily = ctx.MonoFont,
            TextColor = ctx.CCodeHeaderText,
        };

        var body = new Label
        {
            Text = html.Html,
            FontSize = Math.Clamp(ctx.BodySize * 0.84, 9, 20),
            FontFamily = ctx.MonoFont,
            TextColor = ctx.CTextSecondary,
            LineHeight = Math.Clamp(ctx.LineHeight * 0.92, 1.1, 2),
        };

        var stack = new VerticalStackLayout
        {
            Spacing = 4,
            Padding = new Thickness(12, 8, 12, 10),
            Children = { header, body },
        };

        return new Border
        {
            Content = stack,
            BackgroundColor = ctx.CCodeBg,
            Stroke = ctx.CCodeBorder,
            StrokeThickness = 1,
            StrokeShape = UiKit.Round(8),
            Padding = 0,
            Margin = new Thickness(0, ctx.BodySize * 0.2, 0, ctx.ParagraphSpacing),
        };
    }
}
