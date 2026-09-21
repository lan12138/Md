// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Markdown/TextEncodingDetector.cs
//  说明：文件编码嗅探。Markdown 文档在中文环境下常见 UTF-8 / UTF-8 BOM /
//        UTF-16 / GB18030 混用，直接按 UTF-8 读会出现乱码。
//        策略：BOM 优先 -> 严格 UTF-8 试解码 -> 回退 GB18030 -> 最后 ISO-8859-1。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  DecodedText 带回实际使用的 Encoding 对象：
//                   编辑保存时必须按原编码写回，不能一律存成 UTF-8
// -----------------------------------------------------------------------------

using System.Text;

namespace MD.Markdown;

/// <summary>
/// 解码结果。<paramref name="Encoding"/> 是可以直接用于「原样写回」的编码对象：
/// 带 BOM 的情形使用会输出 BOM 前导码的编码实例，写文件时由 TextFileFormat 负责补 BOM。
/// </summary>
public sealed record DecodedText(string Text, string EncodingName, bool HadBom, Encoding Encoding);

public static class TextEncodingDetector
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static bool _providerRegistered;

    /// <summary>注册代码页编码提供程序（GB18030 / GBK / Big5 等）。</summary>
    public static void EnsureProvider()
    {
        if (_providerRegistered)
            return;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch
        {
            // 忽略：极端情况下无法注册时仍可使用 UTF 系列
        }
        _providerRegistered = true;
    }

    public static DecodedText Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
            return new DecodedText(string.Empty, "utf-8", false, new UTF8Encoding(false));

        EnsureProvider();

        // ---- BOM ----
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            var enc = new UTF8Encoding(true);
            return new DecodedText(enc.GetString(bytes, 3, bytes.Length - 3), "utf-8", true, enc);
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            var enc = new UTF32Encoding(false, true);
            return new DecodedText(enc.GetString(bytes, 4, bytes.Length - 4), "utf-32-le", true, enc);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            var enc = Encoding.Unicode;
            return new DecodedText(enc.GetString(bytes, 2, bytes.Length - 2), "utf-16-le", true, enc);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            var enc = Encoding.BigEndianUnicode;
            return new DecodedText(enc.GetString(bytes, 2, bytes.Length - 2), "utf-16-be", true, enc);
        }

        // ---- 严格 UTF-8 ----
        if (IsValidUtf8(bytes, out var utf8Text))
            return new DecodedText(utf8Text!, "utf-8", false, new UTF8Encoding(false));

        // ---- 中文环境最常见的回退 ----
        //
        // 这一步必须额外做「解码后再编码能不能拿回完全相同的字节」的校验：
        // 编辑保存是「按探测出的编码写回」的，一旦探测到的编码无法无损还原原始字节，
        // 保存就会把文件写坏。同时用「可疑字符惩罚分」把明显是解错编码的结果压下去
        // （GB18030 的解码面覆盖极广，Big5 内容也能解出来，但会落进私用区）。
        (string Name, Encoding Enc, string Text, int Penalty)? best = null;

        foreach (var name in new[] { "GB18030", "GBK", "big5" })
        {
            try
            {
                var enc = Encoding.GetEncoding(name);
                var text = enc.GetString(bytes);

                if (LooksLikeMojibake(text))
                    continue;

                if (!RoundTrips(enc, text, bytes))
                    continue;

                int penalty = SuspicionPenalty(text);
                if (best is null || penalty < best.Value.Penalty)
                    best = (name, enc, text, penalty);
            }
            catch
            {
                // 该代码页不可用，继续尝试
            }
        }

        if (best is not null)
            return new DecodedText(best.Value.Text, best.Value.Name.ToLowerInvariant(), false, best.Value.Enc);

        // ---- 最终兜底：不做有损替换的直接映射（Latin-1 一定无损） ----
        return new DecodedText(Encoding.Latin1.GetString(bytes), "iso-8859-1", false, Encoding.Latin1);
    }

    /// <summary>
    /// 把文本按同一编码写回，是否与原始字节完全一致。
    ///
    /// 这里必须用「无法表示就抛异常」的严格编码器：
    /// 普通编码器遇到表示不了的字符会悄悄替换成 '?'（0x3F），
    /// 于是解码 → 编码仍然「字节一致」，把错误的编码判定放过去。
    /// 例如把简体中文的字节当成 big5 解，就会因为这种替换而误判成功。
    /// </summary>
    private static bool RoundTrips(Encoding enc, string text, byte[] original)
    {
        try
        {
            var strict = Encoding.GetEncoding(
                enc.CodePage,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            var again = strict.GetBytes(text);
            return again.AsSpan().SequenceEqual(original);
        }
        catch (EncoderFallbackException)
        {
            // 该编码表示不了这些字符 → 不可能是这份文件的编码
            return false;
        }
        catch
        {
            // 不支持自定义 fallback 时退回普通编码（至少保证字节一致）
            try
            {
                return enc.GetBytes(text).AsSpan().SequenceEqual(original);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 可疑字符惩罚分：私用区、替换字符、兼容汉字、部首笔画等
    /// 都是「解错编码」的典型痕迹，分越高越不可能是正确编码。
    /// </summary>
    private static int SuspicionPenalty(string text)
    {
        int penalty = 0;
        int limit = Math.Min(text.Length, 8192);
        for (int i = 0; i < limit; i++)
        {
            char c = text[i];
            if (c >= '\uE000' && c <= '\uF8FF') penalty += 4;              // 私用区
            else if (c == '\uFFFD') penalty += 6;                          // 替换字符
            else if (c >= '\uF900' && c <= '\uFAFF') penalty += 1;         // 兼容汉字
            else if (c >= '\u2E80' && c <= '\u2FDF') penalty += 1;         // 部首
            else if (c >= '\u31C0' && c <= '\u31EF') penalty += 1;         // 笔画
            else if (c >= '\u0080' && c <= '\u009F') penalty += 2;         // 控制字符
        }
        return penalty;
    }

    private static bool IsValidUtf8(byte[] bytes, out string? text)
    {
        text = null;
        try
        {
            text = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>粗略判断是否出现典型乱码字符（“锟斤拷”“\uFFFD”等）。</summary>
    private static bool LooksLikeMojibake(string text)
    {
        if (text.Length == 0)
            return false;
        int bad = 0;
        int limit = Math.Min(text.Length, 4096);
        for (int i = 0; i < limit; i++)
        {
            char c = text[i];
            if (c == '\uFFFD' || c == '\u0000')
                bad++;
        }
        if (text.Contains("锟斤拷", StringComparison.Ordinal))
            bad += 8;
        return bad > limit * 0.02;
    }
}
