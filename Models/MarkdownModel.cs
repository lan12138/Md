// -----------------------------------------------------------------------------
//  MD MD - 跨平台 Markdown 阅读器
//  文件：Models/MarkdownModel.cs
//  说明：与渲染层解耦的 Markdown 文档领域模型（Block / Inline 两层）。
//        解析阶段只产出本模型，渲染阶段再映射为原生控件，便于：
//          1) 万行文档的虚拟化渲染（只渲染可视区域的 Block）
//          2) 主题热切换（只重建控件，不重新解析）
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

namespace MD.Models;

/// <summary>块级元素基类。</summary>
public abstract class MdBlock
{
    /// <summary>在文档扁平化序列中的索引（用于虚拟化、目录跳转、进度计算）。</summary>
    public int Index { get; set; }

    /// <summary>源代码中的起始行（从 1 开始），YAML Front Matter 之后的正文行号。</summary>
    public int SourceLine { get; set; }

    /// <summary>嵌套深度（列表 / 引用内部），用于渲染缩进。</summary>
    public int Depth { get; set; }
}

/// <summary>标题块。</summary>
public sealed class HeadingBlock : MdBlock
{
    public int Level { get; init; } = 1;
    public string Text { get; init; } = string.Empty;
    public string Anchor { get; init; } = string.Empty;
    public IReadOnlyList<InlineNode> Inlines { get; init; } = Array.Empty<InlineNode>();
}

/// <summary>段落块。</summary>
public sealed class ParagraphBlock : MdBlock
{
    public IReadOnlyList<InlineNode> Inlines { get; init; } = Array.Empty<InlineNode>();
}

/// <summary>引用块（可嵌套任意块）。</summary>
public sealed class QuoteBlock : MdBlock
{
    public IReadOnlyList<MdBlock> Children { get; init; } = Array.Empty<MdBlock>();
}

/// <summary>列表块。</summary>
public sealed class ListBlock : MdBlock
{
    public bool IsOrdered { get; init; }
    public int Start { get; init; } = 1;
    public IReadOnlyList<ListItemNode> Items { get; init; } = Array.Empty<ListItemNode>();
}

/// <summary>列表项。</summary>
public sealed class ListItemNode
{
    /// <summary>项目符号或序号文本，例如 "•" / "3." / "☑"。</summary>
    public string Marker { get; init; } = "•";

    /// <summary>是否为任务列表项。</summary>
    public bool IsTask { get; init; }

    /// <summary>任务是否勾选。</summary>
    public bool IsChecked { get; init; }

    public IReadOnlyList<MdBlock> Children { get; init; } = Array.Empty<MdBlock>();
}

/// <summary>代码块。</summary>
public sealed class CodeBlock : MdBlock
{
    public string Language { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Info { get; init; }
}

/// <summary>分割线。</summary>
public sealed class ThematicBreakBlock : MdBlock
{
}

/// <summary>表格。</summary>
public sealed class TableBlock : MdBlock
{
    public IReadOnlyList<TableColumn> Columns { get; init; } = Array.Empty<TableColumn>();
    public IReadOnlyList<TableRow> Rows { get; init; } = Array.Empty<TableRow>();
}

public sealed class TableColumn
{
    public CellAlignment Alignment { get; init; } = CellAlignment.Left;
}

public sealed class TableRow
{
    public bool IsHeader { get; init; }
    public IReadOnlyList<IReadOnlyList<InlineNode>> Cells { get; init; } =
        Array.Empty<IReadOnlyList<InlineNode>>();
}

/// <summary>HTML 原块（不做 HTML 渲染，按等宽文本降级展示）。</summary>
public sealed class HtmlBlock : MdBlock
{
    public string Html { get; init; } = string.Empty;
}

/// <summary>图片块（单独成段的图片）。</summary>
public sealed class ImageBlock : MdBlock
{
    public string Url { get; init; } = string.Empty;
    public string Alt { get; init; } = string.Empty;
}

/// <summary>
/// 文本对齐方式（表格用）。
/// 故意不叫 TextAlignment：那会与 Microsoft.Maui.TextAlignment 在使用方造成二义性。
/// </summary>
public enum CellAlignment
{
    Left,
    Center,
    Right,
}

// ---------------------------------------------------------------------------
// 行内元素
// ---------------------------------------------------------------------------

public abstract class InlineNode
{
}

public sealed class TextNode : InlineNode
{
    public string Text { get; init; } = string.Empty;
}

public sealed class EmphasisNode : InlineNode
{
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Strike { get; init; }
    public bool IsSub { get; init; }
    public bool IsSup { get; init; }
    public bool IsMark { get; init; }
    public bool IsInserted { get; init; }
    public IReadOnlyList<InlineNode> Children { get; init; } = Array.Empty<InlineNode>();
}

public sealed class CodeNode : InlineNode
{
    public string Text { get; init; } = string.Empty;
}

public sealed class LinkNode : InlineNode
{
    public string Url { get; init; } = string.Empty;
    public string? Title { get; init; }
    public bool IsAutoLink { get; init; }
    public IReadOnlyList<InlineNode> Children { get; init; } = Array.Empty<InlineNode>();
}

public sealed class ImageNode : InlineNode
{
    public string Url { get; init; } = string.Empty;
    public string Alt { get; init; } = string.Empty;
    public string? Title { get; init; }
}

public sealed class LineBreakNode : InlineNode
{
}

public sealed class RawHtmlInlineNode : InlineNode
{
    public string Html { get; init; } = string.Empty;
}

// ---------------------------------------------------------------------------
// 文档 / 大纲
// ---------------------------------------------------------------------------

public sealed class MdDocument
{
    public string FilePath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyList<MdBlock> Blocks { get; init; } = Array.Empty<MdBlock>();
    public IReadOnlyList<OutlineNode> Outline { get; init; } = Array.Empty<OutlineNode>();
    public string? FrontMatter { get; init; }
    public int SourceLineCount { get; init; }
    public int WordCount { get; init; }
    public int CharCount { get; init; }
    public TimeSpan ParseTime { get; init; }

    /// <summary>源码路径 → 块索引（用于按标题锚点跳转）。</summary>
    public IReadOnlyDictionary<string, int> AnchorIndex { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

public sealed class OutlineNode
{
    public int Level { get; init; }
    public string Text { get; init; } = string.Empty;
    public string Anchor { get; init; } = string.Empty;

    /// <summary>对应的块索引（CollectionView 的 item index）。</summary>
    public int BlockIndex { get; init; }

    /// <summary>源码行号。</summary>
    public int SourceLine { get; init; }

    /// <summary>在目录中的缩进层级（已压缩跳级）。</summary>
    public int Indent { get; init; }

    public override string ToString() => $"{new string('#', Math.Clamp(Level, 1, 6))} {Text}";
}
