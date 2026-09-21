// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：tests/MD.EncodingSelfCheck/Program.cs
//  说明：编码 / BOM / 换行「三元组」的往返自检。
//        用 <Compile Include> 直接链接 MD 项目里真实的
//        Markdown/TextEncodingDetector.cs 与 Markdown/TextFileFormat.cs
//        （这两个文件零 UI 依赖，所以控制台工程能直接编进来），
//        跑的是真实现，而不是复刻一份逻辑。
//
//        断言两条核心不变量：
//          1. 读进来再原样写回去，字节必须完全一致；
//          2. 在编辑器里改一行后保存，编码 / BOM / 换行三者都不能变。
//
//  运行：dotnet run --project tests/MD.EncodingSelfCheck
//  作者：MD
//  创建：2026-09-21  从 scratch/enctest 提升为正式回归测试
//  修改：2026-09-21  初版（73 用例全绿）
// -----------------------------------------------------------------------------

using System.Text;
using MD.Markdown;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

int passed = 0, failed = 0;

void Check(string name, bool ok, string? detail = null)
{
    if (ok) { passed++; Console.WriteLine($"  [通过] {name}"); }
    else { failed++; Console.WriteLine($"  [失败] {name}   {detail}"); }
}

// 一份包含中英混排、需要多行、且带空行的内容
const string Body =
    "# 标题 Heading\r\n" +
    "\r\n" +
    "中文段落，含标点：顿号、书名号《测试》、破折号——以及英文 mixed content.\r\n" +
    "\r\n" +
    "```java\r\n" +
    "int a = 1;\r\n" +
    "```\r\n";

// 注意：big5 是繁体码表，编码不了简体中文（会退化成 '?'），
// 所以 Big5 用例必须用繁体正文，否则测的不是探测器而是「输入本身就不合法」。
const string BodyTraditional =
    "# 標題 Heading\r\n" +
    "\r\n" +
    "繁體段落，含標點：頓號、書名號《測試》、破折號——以及英文 mixed content.\r\n" +
    "\r\n" +
    "```java\r\n" +
    "int a = 1;\r\n" +
    "```\r\n";

// 目标：文件原有的编码 / BOM / 换行风格，在「读出来再写回去」之后必须字节完全一致。
// 期望的嗅探结果：GBK 与 GB18030 在常用范围内兼容，且 GB18030 是其超集，
// 探测结果落到 gb18030 属于可接受（字节仍然无损），因此这里按「可接受集合」断言。
var cases = new (string Name, Encoding Enc, bool Bom, string NewLine, string[] AcceptableEncodings, string Text, string Expect)[]
{
    ("UTF-8 无 BOM",      new UTF8Encoding(false),         false, "\r\n", new[] { "utf-8" },     Body,            "中文段落"),
    ("UTF-8 带 BOM",      new UTF8Encoding(true),          true,  "\r\n", new[] { "utf-8" },     Body,            "中文段落"),
    ("UTF-8 无 BOM + LF", new UTF8Encoding(false),         false, "\n",   new[] { "utf-8" },     Body,            "中文段落"),
    ("UTF-16 LE",         Encoding.Unicode,                true,  "\r\n", new[] { "utf-16-le" }, Body,            "中文段落"),
    ("UTF-16 BE",         Encoding.BigEndianUnicode,       true,  "\r\n", new[] { "utf-16-be" }, Body,            "中文段落"),
    ("GB18030",           Encoding.GetEncoding("GB18030"), false, "\r\n", new[] { "gb18030" },   Body,            "中文段落"),
    ("GBK + LF",          Encoding.GetEncoding("GBK"),     false, "\n",   new[] { "gbk", "gb18030" }, Body,       "中文段落"),
    ("Big5",              Encoding.GetEncoding("big5"),    false, "\r\n", new[] { "big5" },      BodyTraditional, "繁體段落"),
};

Console.WriteLine("=== 1. 读取 → 原样写回：字节必须完全一致 ===");
foreach (var c in cases)
{
    var text = TextFileFormat.NormalizeNewLines(c.Text, c.NewLine);
    var original = Concat(c.Bom ? c.Enc.GetPreamble() : Array.Empty<byte>(), c.Enc.GetBytes(text));

    var decoded = TextEncodingDetector.Decode(original);
    var format = TextFileFormat.From(decoded, decoded.Text);
    var written = format.Encode(decoded.Text, out bool lossy);

    Check($"{c.Name}：嗅探出 {format.DisplayName} / {format.NewLineName}",
        c.AcceptableEncodings.Contains(format.EncodingName, StringComparer.OrdinalIgnoreCase),
        $"实际 {format.DisplayName}，可接受 {string.Join('/', c.AcceptableEncodings)}");

    Check($"{c.Name}：换行识别为 {format.NewLineName}", format.NewLine == c.NewLine, $"实际 {(int)format.NewLine[0]} 长度 {format.NewLine.Length}");
    Check($"{c.Name}：字节完全一致", original.AsSpan().SequenceEqual(written),
        $"原 {original.Length}B → 写回 {written.Length}B，lossy={lossy}");
    // 解出来的中文必须是中文，而不是乱码
    Check($"{c.Name}：正文解码正确", decoded.Text.Contains(c.Expect), $"解出的文本里找不到「{c.Expect}」");
}

Console.WriteLine();
Console.WriteLine("=== 2. 编辑一行后再保存：编码 / BOM / 换行不变，内容可读回 ===");
foreach (var c in cases)
{
    var text = TextFileFormat.NormalizeNewLines(c.Text, c.NewLine);
    var original = Concat(c.Bom ? c.Enc.GetPreamble() : Array.Empty<byte>(), c.Enc.GetBytes(text));

    var decoded = TextEncodingDetector.Decode(original);
    var format = TextFileFormat.From(decoded, decoded.Text);

    // 模拟用户在编辑器里追加一行（编辑器产出的是 LF，这正是最容易写坏编码的路径）
    var edited = decoded.Text + TextFileFormat.NormalizeNewLines("新增一行 edited line\n", c.NewLine);

    var written = format.Encode(edited, out bool lossy);
    var reread = TextEncodingDetector.Decode(written);

    Check($"{c.Name}：编辑后编码仍为 {format.DisplayName}", reread.EncodingName == format.EncodingName, $"实际 {reread.EncodingName}");
    Check($"{c.Name}：BOM 保持 {(format.HasBom ? "有" : "无")}", reread.HadBom == format.HasBom);
    Check($"{c.Name}：内容无损读回", reread.Text == edited, $"长度 {reread.Text.Length} vs {edited.Length}，lossy={lossy}");
    Check($"{c.Name}：新行用原换行风格", reread.Text.Contains("新增一行 edited line" + c.NewLine));
}

Console.WriteLine();
Console.WriteLine("=== 3. 换行风格归一：不产生 \\r\\r\\n 这类混合换行 ===");
Check("CRLF 文件里混入 LF 会被归一", TextFileFormat.NormalizeNewLines("a\nb\r\nc\rd", "\r\n") == "a\r\nb\r\nc\r\nd");
Check("LF 文件里混入 CRLF 会被归一", TextFileFormat.NormalizeNewLines("a\r\nb\rc\nd", "\n") == "a\nb\nc\nd");
Check("ToLf 归一", TextFileFormat.ToLf("a\r\nb\rc\n") == "a\nb\nc\n");
Check("空文本不炸", TextFileFormat.NormalizeNewLines("", "\r\n") == "");
Check("无换行时退回系统换行", TextFileFormat.DetectNewLine("abc") == Environment.NewLine);
Check("CRLF 占多数时识别为 CRLF", TextFileFormat.DetectNewLine("a\r\nb\r\nc\n") == "\r\n");
Check("LF 占多数时识别为 LF", TextFileFormat.DetectNewLine("a\nb\nc\r\n") == "\n");

Console.WriteLine();
Console.WriteLine("=== 4. 有损编码能被识别（不能悄悄把文件写坏） ===");
{
    var big5 = Encoding.GetEncoding("big5");
    var format = new TextFileFormat
    {
        EncodingName = "big5",
        Encoding = big5,
        NewLine = "\r\n",
        HasBom = false,
    };
    var emoji = "正常中文 + 😀 表情（Big5 表示不了）\r\n";
    var bytes = format.Encode(emoji, out bool lossy);
    Check("Big5 遇到无法表示的字符时 lossy=true", lossy);

    var utf8 = TextFileFormat.DefaultUtf8;
    utf8.Encode(emoji, out bool utf8Lossy);
    Check("UTF-8 永远无损", !utf8Lossy);
}

Console.WriteLine();
Console.WriteLine($"结果：通过 {passed}，失败 {failed}");
return failed == 0 ? 0 : 1;

static byte[] Concat(byte[] a, byte[] b)
{
    if (a.Length == 0) return b;
    var r = new byte[a.Length + b.Length];
    Buffer.BlockCopy(a, 0, r, 0, a.Length);
    Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
    return r;
}
