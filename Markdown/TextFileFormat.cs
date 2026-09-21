// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Markdown/TextFileFormat.cs
//  说明：文本文件的「形态」描述：编码、BOM、换行风格。
//
//        编辑功能最容易犯的错误就是把文件「写坏」：原本是 GBK 的存成了 UTF-8，
//        原本是 CRLF 的变成 LF，原本带 BOM 的丢了 BOM —— 这类改动在 Git 里
//        表现为整文件 diff，非常难收拾。
//        所以打开文件时先把这三件事记下来，保存时严格照原样写回。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using System.Text;

namespace MD.Markdown;

public sealed class TextFileFormat
{
    /// <summary>归一化编码名（utf-8 / utf-8-bom / utf-16-le / gb18030 …），用于界面显示。</summary>
    public string EncodingName { get; init; } = "utf-8";

    /// <summary>是否带 BOM。</summary>
    public bool HasBom { get; init; }

    /// <summary>换行风格（"\\r\\n" / "\\n" / "\\r"）。</summary>
    public string NewLine { get; init; } = "\r\n";

    /// <summary>实际用于编解码的编码对象（已去掉 BOM 前缀行为）。</summary>
    public Encoding Encoding { get; init; } = new UTF8Encoding(false);

    /// <summary>UTF-8（无 BOM）——最常见的默认值。</summary>
    public static TextFileFormat DefaultUtf8 { get; } = new();

    /// <summary>换行风格的可读名称。</summary>
    public string NewLineName => NewLine switch
    {
        "\r\n" => "CRLF",
        "\r" => "CR",
        _ => "LF",
    };

    /// <summary>界面显示用：编码 + BOM 标记。</summary>
    public string DisplayName => HasBom ? $"{EncodingName}-bom" : EncodingName;

    /// <summary>
    /// 按原格式把文本编码回字节。
    /// <paramref name="lossy"/> 为 true 表示有字符无法用目标编码表示、已被替换，
    /// 调用方应当提示用户（而不是默默写坏文件）。
    /// </summary>
    public byte[] Encode(string text, out bool lossy)
    {
        lossy = false;

        // 换行统一：先折算成 \n，再按原风格展开，避免出现 \r\r\n 这种混合换行
        var normalized = NormalizeNewLines(text, NewLine);

        // 优先用「无法表示就抛异常」的严格编码器精确判断是否存在有损替换；
        // 注意 UTF-8 / UTF-16 这类 Unicode 编码不允许自定义 fallback，
        // 此时退回「编码再解码比对」的办法判断。
        try
        {
            var strict = Encoding.GetEncoding(
                Encoding.CodePage,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            return PrependBom(strict.GetBytes(normalized));
        }
        catch (EncoderFallbackException)
        {
            lossy = true;
            return PrependBom(Encoding.GetBytes(normalized));
        }
        catch
        {
            var body = Encoding.GetBytes(normalized);
            try
            {
                lossy = !string.Equals(Encoding.GetString(body), normalized, StringComparison.Ordinal);
            }
            catch
            {
                // 连回读都失败时不做判断，交给用户看保存结果
            }
            return PrependBom(body);
        }
    }

    private byte[] PrependBom(byte[] body)
    {
        if (!HasBom)
            return body;

        var preamble = Encoding.GetPreamble();
        if (preamble.Length == 0)
            return body;

        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    /// <summary>把任意换行风格统一成指定的换行风格。</summary>
    public static string NormalizeNewLines(string text, string newLine)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new StringBuilder(text.Length + 16);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                sb.Append(newLine);
            }
            else if (c == '\n')
            {
                sb.Append(newLine);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>把文本的换行统一成「只用 \n」，便于比较内容是否真的变了。</summary>
    public static string ToLf(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>统计文本的主换行风格。</summary>
    public static string DetectNewLine(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        if (crlf == 0 && lf == 0 && cr == 0)
            return Environment.NewLine;

        if (crlf >= lf && crlf >= cr)
            return "\r\n";
        return lf >= cr ? "\n" : "\r";
    }

    /// <summary>由嗅探结果构造格式描述。</summary>
    public static TextFileFormat From(DecodedText decoded, string text) => new()
    {
        EncodingName = decoded.EncodingName,
        HasBom = decoded.HadBom,
        Encoding = decoded.Encoding,
        NewLine = DetectNewLine(text),
    };
}
