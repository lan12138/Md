// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Markdown/MarkdownParser.cs
//  说明：Markdown -> 领域模型 的单向转换。
//        · 语法分析交给 Markdig（与 CommonMark 高度一致，速度快）
//        · 输出 MD.Models 中的 Block/Inline 模型，渲染层完全原生（不用 WebView）
//        · 同时产出扁平块索引与大纲（目录），供虚拟化渲染与跳转使用
//
//  ⚠ 命名约定：本文件同时引用 Markdig 与本项目的类型，而两边存在大量同名类型
//    （HeadingBlock / ParagraphBlock / ListBlock / CodeBlock / …）。
//    为避免 CS0104 二义性，Markdig 侧的类型**一律走 Md* 别名**，
//    并且不 `using Markdig.Syntax;`。新增代码请沿用这一约定。
//
//  ⚠ 特别注意：Markdig 的「块基类」别名**不能**叫 MdBlock，
//    因为本项目模型里已经有一个 MD.Models.MdBlock，而 using 别名的优先级
//    高于 using 命名空间导入的类型 —— 撞名会让整份文件的 MdBlock 全部
//    指向 Markdig 的 Block，引发一串 CS0029 / CS1061 / CS8121。
//    故 Markdig 基类别名固定为 MdSourceBlock。未被占用的 Md* 别名可继续使用。
//
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  改为 Markdig 类型别名化，消除与领域模型的命名冲突
//  修改：2026-09-21  MdBlock 别名与领域模型撞名 → 更名为 MdSourceBlock
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax.Inlines;
using MD.Models;

// ---- Markdig 块级类型别名（避免与 MD.Models 同名类型冲突） ----
// MdSourceBlock = 语法树节点基类；项目模型基类 MdBlock 保持原名不动。
using MdSourceBlock = Markdig.Syntax.Block;
using MdContainerBlock = Markdig.Syntax.ContainerBlock;
using MdLeafBlock = Markdig.Syntax.LeafBlock;
using MdBlankLineBlock = Markdig.Syntax.BlankLineBlock;
using MdHeadingBlock = Markdig.Syntax.HeadingBlock;
using MdParagraphBlock = Markdig.Syntax.ParagraphBlock;
using MdQuoteBlock = Markdig.Syntax.QuoteBlock;
using MdListBlock = Markdig.Syntax.ListBlock;
using MdListItemBlock = Markdig.Syntax.ListItemBlock;
using MdCodeBlock = Markdig.Syntax.CodeBlock;
using MdFencedCodeBlock = Markdig.Syntax.FencedCodeBlock;
using MdThematicBreakBlock = Markdig.Syntax.ThematicBreakBlock;
using MdHtmlBlock = Markdig.Syntax.HtmlBlock;
using MdYamlFrontMatterBlock = Markdig.Extensions.Yaml.YamlFrontMatterBlock;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableColumnAlign = Markdig.Extensions.Tables.TableColumnAlign;

namespace MD.Markdown;

public sealed class ParseOptions
{
    /// <summary>是否解析 YAML Front Matter。</summary>
    public bool FrontMatter { get; init; } = true;

    /// <summary>是否启用脚注 / 定义列表 / 任务列表等 GFM 扩展。</summary>
    public bool Extensions { get; init; } = true;

    public static readonly ParseOptions Default = new();
}

/// <summary>Markdown 解析器（线程安全，可复用）。</summary>
public sealed class MarkdownParser
{
    private readonly MarkdownPipeline _full;
    private readonly MarkdownPipeline _basic;

    public MarkdownParser()
    {
        _full = new MarkdownPipelineBuilder()
            .UsePreciseSourceLocation()
            .UseAdvancedExtensions()
            .UsePipeTables()
            .UseGridTables()
            .UseEmphasisExtras()
            .UseTaskLists()
            .UseAutoIdentifiers()
            .UseFootnotes()
            .UseDefinitionLists()
            .UseAutoLinks()
            .UseYamlFrontMatter()
            .UseEmojiAndSmiley()
            .Build();

        _basic = new MarkdownPipelineBuilder()
            .UsePreciseSourceLocation()
            .UsePipeTables()
            .Build();
    }

    /// <summary>把 Markdown 源码解析为文档模型。</summary>
    public MdDocument Parse(string markdown, string filePath, ParseOptions? options = null)
    {
        options ??= ParseOptions.Default;
        var sw = Stopwatch.StartNew();

        markdown ??= string.Empty;
        markdown = NormalizeNewLines(markdown);

        var pipeline = options.Extensions ? _full : _basic;
        var doc = Markdig.Markdown.Parse(markdown, pipeline);

        var builder = new BlockBuilder();
        string? frontMatter = null;

        foreach (var block in doc)
        {
            if (block is MdYamlFrontMatterBlock yaml)
            {
                // Front Matter 不进入正文渲染，仅作为元数据保留
                frontMatter = yaml.Lines.ToString()?.Trim() ?? string.Empty;
                continue;
            }

            builder.ConvertBlock(block, 0);
        }

        var blocks = builder.Finish();

        int words = 0, chars = 0;
        foreach (var b in blocks)
            CountText(b, ref words, ref chars);

        var outline = BuildOutline(blocks);

        var anchorIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in blocks)
        {
            if (b is HeadingBlock h && !string.IsNullOrEmpty(h.Anchor))
                anchorIndex.TryAdd(h.Anchor, h.Index);
        }

        var name = string.IsNullOrEmpty(filePath) ? "未命名" : Path.GetFileName(filePath);

        return new MdDocument
        {
            FilePath = filePath,
            DisplayName = name,
            Blocks = blocks,
            Outline = outline,
            FrontMatter = frontMatter,
            SourceLineCount = markdown.Length == 0 ? 0 : CountLines(markdown),
            WordCount = words,
            CharCount = chars,
            ParseTime = sw.Elapsed,
            AnchorIndex = anchorIndex,
        };
    }

    private static int CountLines(string text)
    {
        int n = 1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') n++;
        return n;
    }

    internal static string NormalizeNewLines(string text)
    {
        if (text.IndexOf('\r') < 0)
            return text;

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    continue;
                sb.Append('\n');
            }
            else
            {
                sb.Append(text[i]);
            }
        }
        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // 大纲
    // -----------------------------------------------------------------------

    private static IReadOnlyList<OutlineNode> BuildOutline(IReadOnlyList<MdBlock> blocks)
    {
        var list = new List<OutlineNode>();

        int minLevel = 7;
        foreach (var b in blocks)
            if (b is HeadingBlock h && h.Level < minLevel)
                minLevel = h.Level;
        if (minLevel > 6)
            minLevel = 1;

        foreach (var b in blocks)
        {
            if (b is not HeadingBlock h)
                continue;

            list.Add(new OutlineNode
            {
                Level = h.Level,
                Text = string.IsNullOrWhiteSpace(h.Text) ? "(空标题)" : h.Text,
                Anchor = h.Anchor,
                BlockIndex = h.Index,
                SourceLine = h.SourceLine,
                Indent = Math.Max(0, h.Level - minLevel),
            });
        }

        return list;
    }

    // -----------------------------------------------------------------------
    // 统计
    // -----------------------------------------------------------------------

    private static void CountText(MdBlock block, ref int words, ref int chars)
    {
        switch (block)
        {
            case HeadingBlock h:
                CountInlines(h.Inlines, ref words, ref chars);
                break;
            case ParagraphBlock p:
                CountInlines(p.Inlines, ref words, ref chars);
                break;
            case CodeBlock c:
                chars += c.Code.Length;
                words += CountLatinWords(c.Code);
                break;
            case QuoteBlock q:
                foreach (var child in q.Children)
                    CountText(child, ref words, ref chars);
                break;
            case ListBlock l:
                foreach (var item in l.Items)
                    foreach (var child in item.Children)
                        CountText(child, ref words, ref chars);
                break;
            case TableBlock t:
                foreach (var row in t.Rows)
                    foreach (var cell in row.Cells)
                        CountInlines(cell, ref words, ref chars);
                break;
            case HtmlBlock html:
                chars += html.Html.Length;
                break;
        }
    }

    private static void CountInlines(IReadOnlyList<InlineNode> nodes, ref int words, ref int chars)
    {
        var sb = new StringBuilder();
        CollectText(nodes, sb);
        var text = sb.ToString();
        chars += text.Length;
        words += CountCjk(text) + CountLatinWords(text);
    }

    internal static int CountCjk(string text)
    {
        int n = 0;
        foreach (var ch in text)
        {
            if ((ch >= 0x2E80 && ch <= 0x9FFF) ||
                (ch >= 0xF900 && ch <= 0xFAFF) ||
                (ch >= 0xFF00 && ch <= 0xFFEF))
                n++;
        }
        return n;
    }

    internal static int CountLatinWords(string text)
    {
        int n = 0;
        bool inWord = false;
        foreach (var ch in text)
        {
            bool isWord = char.IsLetterOrDigit(ch) && ch < 0x2E80;
            if (isWord && !inWord)
                n++;
            inWord = isWord;
        }
        return n;
    }

    internal static void CollectText(IReadOnlyList<InlineNode> nodes, StringBuilder sb)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode t: sb.Append(t.Text); break;
                case CodeNode c: sb.Append(c.Text); break;
                case LineBreakNode: sb.Append(' '); break;
                case ImageNode i: sb.Append(i.Alt); break;
                case EmphasisNode e: CollectText(e.Children, sb); break;
                case LinkNode l: CollectText(l.Children, sb); break;
            }
        }
    }

    // =======================================================================
    // 内部：Markdig AST -> 领域模型
    // =======================================================================

    private sealed class BlockBuilder
    {
        private readonly List<MdBlock> _blocks = new();
        private readonly HashSet<string> _usedAnchors = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<MdBlock> Finish()
        {
            for (int i = 0; i < _blocks.Count; i++)
                _blocks[i].Index = i;
            return _blocks;
        }

        public void ConvertBlock(MdSourceBlock block, int depth)
        {
            foreach (var converted in Convert(block, depth))
                _blocks.Add(converted);
        }

        private IEnumerable<MdBlock> Convert(MdSourceBlock block, int depth)
        {
            switch (block)
            {
                case MdHeadingBlock heading:
                {
                    var inlines = ConvertInlines(heading.Inline);
                    var text = PlainText(inlines);
                    yield return new HeadingBlock
                    {
                        Level = Math.Clamp(heading.Level, 1, 6),
                        Text = text,
                        Anchor = UniqueAnchor(text),
                        Inlines = inlines,
                        Depth = depth,
                        SourceLine = heading.Line + 1,
                    };
                    break;
                }

                case MdParagraphBlock para:
                {
                    // 整段只有一张图片 → 提升为图片块，便于大图展示
                    if (TryExtractSingleImage(para, out var imageUrl, out var alt))
                    {
                        yield return new ImageBlock
                        {
                            Url = imageUrl,
                            Alt = alt,
                            Depth = depth,
                            SourceLine = para.Line + 1,
                        };
                        break;
                    }

                    var inlines = ConvertInlines(para.Inline);
                    if (inlines.Count == 0)
                        yield break;

                    yield return new ParagraphBlock
                    {
                        Inlines = inlines,
                        Depth = depth,
                        SourceLine = para.Line + 1,
                    };
                    break;
                }

                case MdListBlock list:
                    yield return ConvertList(list, depth);
                    break;

                case MdQuoteBlock quote:
                {
                    var children = new List<MdBlock>();
                    foreach (var child in quote)
                        children.AddRange(Convert(child, depth + 1));

                    yield return new QuoteBlock
                    {
                        Children = children,
                        Depth = depth,
                        SourceLine = quote.Line + 1,
                    };
                    break;
                }

                case MdTable table:
                    yield return ConvertTable(table, depth);
                    break;

                case MdThematicBreakBlock hr:
                    yield return new ThematicBreakBlock { Depth = depth, SourceLine = hr.Line + 1 };
                    break;

                case MdCodeBlock code:
                {
                    var info = (code as MdFencedCodeBlock)?.Info ?? string.Empty;
                    yield return new CodeBlock
                    {
                        Language = ExtractLanguage(info),
                        Info = info,
                        Code = code.Lines.ToString()?.TrimEnd('\n') ?? string.Empty,
                        Depth = depth,
                        SourceLine = code.Line + 1,
                    };
                    break;
                }

                // 数学块（$$...$$）→ 按等宽公式块呈现
                case MdLeafBlock mathBlock when mathBlock.GetType().Name == "MathBlock":
                    yield return new CodeBlock
                    {
                        Language = "math",
                        Code = mathBlock.Lines.ToString()?.Trim() ?? string.Empty,
                        Depth = depth,
                        SourceLine = mathBlock.Line + 1,
                    };
                    break;

                case MdHtmlBlock html:
                {
                    var text = html.Lines.ToString()?.TrimEnd('\n') ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(text))
                        yield break;

                    yield return new HtmlBlock
                    {
                        Html = text,
                        Depth = depth,
                        SourceLine = html.Line + 1,
                    };
                    break;
                }

                case MdBlankLineBlock:
                    yield break;

                // 兜底：容器块递归展开，叶子块降级为段落
                case MdContainerBlock container:
                {
                    foreach (var child in container)
                        foreach (var c in Convert(child, depth))
                            yield return c;
                    break;
                }

                case MdLeafBlock leaf when leaf.Inline is not null:
                {
                    var inlines = ConvertInlines(leaf.Inline);
                    if (inlines.Count == 0)
                        yield break;

                    yield return new ParagraphBlock
                    {
                        Inlines = inlines,
                        Depth = depth,
                        SourceLine = leaf.Line + 1,
                    };
                    break;
                }

                case MdLeafBlock leaf:
                {
                    var text = leaf.Lines.ToString()?.TrimEnd('\n') ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(text))
                        yield break;

                    yield return new ParagraphBlock
                    {
                        Inlines = new[] { new TextNode { Text = text } },
                        Depth = depth,
                        SourceLine = leaf.Line + 1,
                    };
                    break;
                }
            }
        }

        private MdBlock ConvertList(MdListBlock list, int depth)
        {
            var items = new List<ListItemNode>(list.Count);

            // 注意：Markdig 的 OrderedStart 是字符串（保留原始写法，如 "3"）
            int counter = 1;
            if (list.IsOrdered && !string.IsNullOrWhiteSpace(list.OrderedStart) &&
                int.TryParse(list.OrderedStart, out var parsed))
                counter = parsed;

            foreach (var child in list)
            {
                if (child is not MdListItemBlock li)
                    continue;

                var children = new List<MdBlock>();
                foreach (var sub in li)
                    children.AddRange(Convert(sub, depth + 1));

                DetectTask(li, out bool isTask, out bool isChecked);

                string marker;
                if (isTask)
                    marker = isChecked ? "☑" : "☐";
                else if (list.IsOrdered)
                    marker = counter + ".";
                else
                    marker = depth % 2 == 0 ? "•" : "◦";

                if (list.IsOrdered)
                    counter++;

                items.Add(new ListItemNode
                {
                    Marker = marker,
                    IsTask = isTask,
                    IsChecked = isChecked,
                    Children = children,
                });
            }

            return new ListBlock
            {
                IsOrdered = list.IsOrdered,
                Start = counter,
                Items = items,
                Depth = depth,
                SourceLine = list.Line + 1,
            };
        }

        private static TableBlock ConvertTable(MdTable table, int depth)
        {
            var columns = new List<TableColumn>();
            if (table.ColumnDefinitions is not null)
            {
                foreach (var def in table.ColumnDefinitions)
                {
                    columns.Add(new TableColumn
                    {
                        Alignment = def.Alignment switch
                        {
                            MdTableColumnAlign.Center => CellAlignment.Center,
                            MdTableColumnAlign.Right => CellAlignment.Right,
                            _ => CellAlignment.Left,
                        },
                    });
                }
            }

            var rows = new List<TableRow>();

            foreach (var child in table)
            {
                if (child is not MdTableRow row)
                    continue;

                var cells = new List<IReadOnlyList<InlineNode>>();
                foreach (var cellObj in row)
                {
                    if (cellObj is not MdTableCell cell)
                        continue;

                    // TableCell 是容器块，内容通常在它内部的第一个段落里
                    var inlines = ConvertInlines(FirstInlineContainer(cell));
                    cells.Add(inlines);
                }

                int colCount = Math.Max(columns.Count, cells.Count);
                while (columns.Count < colCount)
                    columns.Add(new TableColumn());
                while (cells.Count < colCount)
                    cells.Add(Array.Empty<InlineNode>());

                rows.Add(new TableRow { IsHeader = row.IsHeader, Cells = cells });
            }

            // 若全表无表头（网格表常见），把第一行标记为表头
            if (rows.Count > 0 && !rows.Any(r => r.IsHeader))
                rows[0] = new TableRow { IsHeader = true, Cells = rows[0].Cells };

            return new TableBlock
            {
                Columns = columns,
                Rows = rows,
                Depth = depth,
                SourceLine = table.Line + 1,
            };
        }

        private static ContainerInline? FirstInlineContainer(MdTableCell cell)
        {
            foreach (var block in cell)
            {
                if (block is MdParagraphBlock p)
                    return p.Inline;
                if (block is MdLeafBlock leaf && leaf.Inline is not null)
                    return leaf.Inline;
            }
            return null;
        }

        private static bool TryExtractSingleImage(MdParagraphBlock para, out string url, out string alt)
        {
            url = string.Empty;
            alt = string.Empty;

            if (para.Inline is null)
                return false;

            LinkInline? onlyImage = null;
            foreach (var inline in para.Inline)
            {
                if (inline is LineBreakInline)
                    continue;
                if (inline is LinkInline { IsImage: true } img && onlyImage is null)
                {
                    onlyImage = img;
                    continue;
                }
                return false;
            }

            if (onlyImage is null)
                return false;

            url = onlyImage.Url ?? string.Empty;
            alt = InlineText(onlyImage);
            return !string.IsNullOrEmpty(url);
        }

        /// <summary>生成唯一锚点（重复标题追加 -1 / -2 …）。</summary>
        private string UniqueAnchor(string text)
        {
            var slug = Slugify(text);
            if (_usedAnchors.Add(slug))
                return slug;

            for (int i = 1; i < 1000; i++)
            {
                var candidate = $"{slug}-{i}";
                if (_usedAnchors.Add(candidate))
                    return candidate;
            }
            return slug + "-x";
        }

        // ---------------- 行内 ----------------

        public static IReadOnlyList<InlineNode> ConvertInlines(ContainerInline? container)
        {
            if (container is null)
                return Array.Empty<InlineNode>();

            // 不用 container.Count：Markdig 的 Count 来自 LINQ 扩展方法而非属性，
            // 直接当参数传给 Math.Max 会被当成「方法组」，故按经验值预分配。
            var list = new List<InlineNode>(8);
            foreach (var inline in container)
                ConvertInline(inline, list);
            return list;
        }

        private static IReadOnlyList<InlineNode> ConvertInlinesFrom(ContainerInline container)
        {
            var list = new List<InlineNode>(4);
            foreach (var inline in container)
                ConvertInline(inline, list);
            return list;
        }

        private static void ConvertInline(Inline inline, List<InlineNode> output)
        {
            switch (inline)
            {
                case LiteralInline lit:
                {
                    var text = lit.Content.ToString();
                    if (text.Length > 0)
                        output.Add(new TextNode { Text = text });
                    break;
                }

                case CodeInline code:
                    output.Add(new CodeNode { Text = code.Content ?? string.Empty });
                    break;

                case EmphasisInline emphasis:
                {
                    var children = ConvertInlinesFrom(emphasis);
                    if (children.Count == 0)
                        children = new InlineNode[] { new TextNode { Text = string.Empty } };

                    bool bold = false, italic = false, strike = false;
                    bool sub = false, sup = false, mark = false, inserted = false;

                    switch (emphasis.DelimiterChar)
                    {
                        case '*':
                        case '_':
                            if (emphasis.DelimiterCount >= 2) bold = true;
                            else italic = true;
                            break;
                        case '~':
                            if (emphasis.DelimiterCount >= 2) strike = true;
                            else sub = true;
                            break;
                        case '=':
                            mark = true;
                            break;
                        case '+':
                            inserted = true;
                            break;
                        case '^':
                            sup = true;
                            break;
                        default:
                            bold = emphasis.DelimiterCount >= 2;
                            italic = !bold;
                            break;
                    }

                    output.Add(new EmphasisNode
                    {
                        Bold = bold,
                        Italic = italic,
                        Strike = strike,
                        IsSub = sub,
                        IsSup = sup,
                        IsMark = mark,
                        IsInserted = inserted,
                        Children = children,
                    });
                    break;
                }

                case LinkInline link when link.IsImage:
                    output.Add(new ImageNode
                    {
                        Url = link.Url ?? string.Empty,
                        Alt = InlineText(link),
                        Title = link.Title,
                    });
                    break;

                case LinkInline link:
                    output.Add(new LinkNode
                    {
                        Url = link.Url ?? string.Empty,
                        Title = link.Title,
                        IsAutoLink = link.IsAutoLink,
                        Children = ConvertInlinesFrom(link),
                    });
                    break;

                case AutolinkInline autolink:
                {
                    var url = autolink.Url ?? string.Empty;
                    output.Add(new LinkNode
                    {
                        Url = url,
                        IsAutoLink = true,
                        Children = new InlineNode[] { new TextNode { Text = url } },
                    });
                    break;
                }

                case LineBreakInline:
                    output.Add(new LineBreakNode());
                    break;

                case HtmlInline html:
                {
                    var tag = html.Tag ?? string.Empty;
                    if (tag.Equals("<br>", StringComparison.OrdinalIgnoreCase) ||
                        tag.Equals("<br/>", StringComparison.OrdinalIgnoreCase) ||
                        tag.Equals("<br />", StringComparison.OrdinalIgnoreCase))
                        output.Add(new LineBreakNode());
                    else
                        output.Add(new RawHtmlInlineNode { Html = tag });
                    break;
                }

                case HtmlEntityInline entity:
                    output.Add(new TextNode { Text = entity.Transcoded.ToString() });
                    break;

                // 数学行内公式 → 等宽行内代码观感
                case LeafInline leaf when leaf.GetType().Name == "MathInline":
                {
                    var content = leaf.GetType().GetProperty("Content")?.GetValue(leaf)?.ToString() ?? string.Empty;
                    output.Add(new CodeNode { Text = content });
                    break;
                }

                case ContainerInline nested:
                {
                    var children = ConvertInlinesFrom(nested);
                    if (children.Count == 1)
                        output.Add(children[0]);
                    else if (children.Count > 1)
                        output.Add(new EmphasisNode { Children = children });
                    break;
                }
            }
        }

        private static string InlineText(ContainerInline container)
        {
            var sb = new StringBuilder();
            AppendPlain(container, sb);
            return sb.ToString();
        }

        private static void AppendPlain(ContainerInline container, StringBuilder sb)
        {
            foreach (var child in container)
            {
                switch (child)
                {
                    case LiteralInline lit: sb.Append(lit.Content.ToString()); break;
                    case CodeInline code: sb.Append(code.Content); break;
                    case ContainerInline c: AppendPlain(c, sb); break;
                }
            }
        }

        /// <summary>任务列表标记：Markdig 把它作为列表项首段落的第一个行内元素。</summary>
        private static void DetectTask(MdListItemBlock item, out bool isTask, out bool isChecked)
        {
            isTask = false;
            isChecked = false;

            if (item.Count == 0)
                return;
            if (item[0] is not MdParagraphBlock para || para.Inline is null)
                return;

            foreach (var inline in para.Inline)
            {
                if (inline.GetType().Name != "TaskList")
                    break;

                isTask = true;
                isChecked = inline.GetType().GetProperty("Checked")?.GetValue(inline) is true;
                break;
            }
        }
    }

    internal static string ExtractLanguage(string? info)
    {
        if (string.IsNullOrWhiteSpace(info))
            return string.Empty;

        var span = info.Trim();
        int end = span.IndexOfAny(new[] { ' ', '\t', '{', ':' });
        if (end > 0)
            span = span[..end];
        return span;
    }

    internal static string PlainText(IReadOnlyList<InlineNode> nodes)
    {
        var sb = new StringBuilder();
        CollectText(nodes, sb);
        return sb.ToString().Trim();
    }

    private static readonly Regex NonSlug = new(@"[^\w\u4e00-\u9fff\-]+", RegexOptions.Compiled);

    internal static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "section";

        var slug = NonSlug.Replace(text.Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "section" : slug;
    }
}
