// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Models/DocumentTab.cs
//  说明：一个标签页（一份已打开的文档）。持有正文、原文、编码格式、
//        编辑状态与阅读进度，切换标签时原样恢复。
//        纯数据，不依赖任何 UI 类型。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using MD.Markdown;

namespace MD.Models;

public sealed class DocumentTab
{
    /// <summary>标签页身份（同一文件只允许存在一个标签）。</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");

    /// <summary>磁盘路径。空字符串表示没有落盘的文件（内置示例文档）。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>标签标题。</summary>
    public string Title { get; set; } = "未命名";

    /// <summary>解析后的文档（阅读模式渲染用）。</summary>
    public MdDocument? Document { get; set; }

    /// <summary>当前文本内容（编辑后可能与磁盘不一致）。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>编码 / BOM / 换行风格，保存时严格照此写回。</summary>
    public TextFileFormat Format { get; set; } = TextFileFormat.DefaultUtf8;

    /// <summary>文件字节数（状态栏显示用）。</summary>
    public long ByteLength { get; set; }

    /// <summary>加载耗时摘要（状态栏显示用）。</summary>
    public string LoadSummary { get; set; } = string.Empty;

    /// <summary>内容已被编辑、尚未保存。</summary>
    public bool IsModified { get; set; }

    /// <summary>是否处于编辑模式。</summary>
    public bool IsEditing { get; set; }

    /// <summary>滚动进度（0~1）。</summary>
    public double Progress { get; set; }

    /// <summary>当前视口中心的块下标。</summary>
    public int CenterIndex { get; set; }

    /// <summary>编码显示名（如 UTF-8 / GB18030 / UTF-8-BOM）。</summary>
    public string EncodingDisplay => Format.DisplayName.ToUpperInvariant();

    /// <summary>换行风格名（CRLF / LF / CR）。</summary>
    public string NewLineName => Format.NewLineName;

    /// <summary>标签上显示的标题（过长时截断）。</summary>
    public string ShortTitle => Title.Length <= 24 ? Title : Title[..23] + "…";

    /// <summary>标签提示：完整路径 + 编码信息。</summary>
    public string Tooltip
    {
        get
        {
            if (string.IsNullOrEmpty(Path))
                return $"{Title}\n（内置示例，未落盘）· {EncodingDisplay} · {NewLineName}";
            return $"{Path}\n{EncodingDisplay} · {NewLineName}{(IsModified ? " · 未保存" : string.Empty)}";
        }
    }
}
