// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Models/FolderModel.cs
//  说明：文件夹浏览的树节点模型（纯数据，不依赖任何 UI 类型）。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

namespace MD.Models;

/// <summary>文件夹树节点（目录或 Markdown 文件）。</summary>
public sealed class FolderNode
{
    /// <summary>绝对路径。</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>显示名（目录名 / 文件名）。</summary>
    public string Name { get; init; } = string.Empty;

    public bool IsDirectory { get; init; }

    /// <summary>展开状态（仅目录有意义）。</summary>
    public bool IsExpanded { get; set; }

    /// <summary>层级，用于缩进。</summary>
    public int Depth { get; init; }

    public List<FolderNode> Children { get; } = new();

    /// <summary>子树内的 Markdown 文件数量（目录行右侧显示）。</summary>
    public int FileCount { get; set; }

    /// <summary>是否与当前打开的文档同路径。</summary>
    public bool IsCurrent { get; set; }

    /// <summary>该文件是否已在标签页中打开（用圆点标记）。</summary>
    public bool IsOpen { get; set; }
}

/// <summary>侧栏文件树里的一行（节点 + 该行的显示信息）。</summary>
public sealed class FolderRow
{
    public FolderRow(FolderNode node, int depth)
    {
        Node = node;
        Depth = depth;
    }

    public FolderNode Node { get; }
    public int Depth { get; }

    public string Name => Node.Name;
    public bool IsDirectory => Node.IsDirectory;
    public bool IsCurrent => Node.IsCurrent;
    public bool IsOpen => Node.IsOpen;
    public bool IsExpanded => Node.IsExpanded;

    /// <summary>目录前的展开箭头，文件用文档图标。</summary>
    public string Glyph => Node.IsDirectory ? (Node.IsExpanded ? "▾" : "▸") : "≡";
}
