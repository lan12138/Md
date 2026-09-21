// -----------------------------------------------------------------------------
//  MD MD - 跨平台 Markdown 阅读器
//  文件：Markdown/SyntaxHighlighter.cs
//  说明：零依赖的轻量语法着色器。用「语言族 + 单遍扫描正则」实现，
//        单文件代码块高亮耗时通常在微秒级，避免为了着色引入重量级依赖。
//        支持的族：C 系（C/C++/C#/Java/JS/TS/Go/Rust/Swift/Kotlin/PHP/CSS）、
//                  Hash 系（Python/Shell/YAML/TOML/Ruby/PowerShell）、SQL、Markup、JSON。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.RegularExpressions;
using MD.Models;

namespace MD.Markdown;

public sealed record SyntaxSpan(string Text, SyntaxTokenKind Kind);

public enum LanguageFamily
{
    Plain,
    C,
    Hash,
    Sql,
    Markup,
    Json,
}

internal sealed class LanguageProfile
{
    public LanguageFamily Family { get; init; } = LanguageFamily.Plain;
    public HashSet<string> Keywords { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> Types { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> Builtins { get; init; } = new(StringComparer.Ordinal);
    public bool HasTemplateString { get; init; }
    public bool CaseInsensitive { get; init; }
    public string? LineComment { get; init; }
    public (string Open, string Close)? BlockComment { get; init; }
}

/// <summary>代码块着色器（有缓存，纯 CPU，无 IO）。</summary>
public static class SyntaxHighlighter
{
    private const int MaxHighlightLength = 400_000; // 超过此长度直接降级为纯文本，保证不卡顿

    private static readonly Dictionary<string, LanguageProfile> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static IReadOnlyList<SyntaxSpan> Highlight(string code, string? language)
    {
        if (string.IsNullOrEmpty(code))
            return Array.Empty<SyntaxSpan>();

        var profile = ResolveProfile(language);
        if (profile.Family == LanguageFamily.Plain || code.Length > MaxHighlightLength)
            return new[] { new SyntaxSpan(code, SyntaxTokenKind.Plain) };

        try
        {
            return Scan(code, profile);
        }
        catch
        {
            // 着色属于「锦上添花」，任何异常都退化为纯文本，绝不影响阅读。
            return new[] { new SyntaxSpan(code, SyntaxTokenKind.Plain) };
        }
    }

    // -----------------------------------------------------------------------
    // 语言识别
    // -----------------------------------------------------------------------

    internal static LanguageProfile ResolveProfile(string? language)
    {
        var key = NormalizeLanguage(language);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
                return cached;
            var created = CreateProfile(key);
            Cache[key] = created;
            return created;
        }
    }

    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "plain";

        var lang = language.Trim().ToLowerInvariant();
        var space = lang.IndexOfAny(new[] { ' ', ',', ';', ':', '{', '[' });
        if (space > 0)
            lang = lang[..space];
        if (lang.StartsWith('.'))
            lang = lang[1..];
        return lang switch
        {
            "c#" or "cs" or "csharp" => "csharp",
            "c++" or "cxx" or "cc" or "hpp" => "cpp",
            "js" or "jsx" or "mjs" or "cjs" or "node" => "javascript",
            "ts" or "tsx" => "typescript",
            "py" or "py3" => "python",
            "sh" or "shell" or "console" or "zsh" => "bash",
            "yml" => "yaml",
            "ps1" or "pwsh" => "powershell",
            "htm" or "xhtml" or "svg" => "html",
            "xml" or "xaml" or "axaml" or "csproj" or "props" or "targets" => "xml",
            "md" or "markdown" => "markdown",
            "rs" => "rust",
            "kt" or "kts" => "kotlin",
            "golang" => "go",
            "rb" => "ruby",
            _ => lang,
        };
    }

    private static LanguageProfile CreateProfile(string lang)
    {
        switch (lang)
        {
            case "csharp":
                return new LanguageProfile
                {
                    Family = LanguageFamily.C,
                    LineComment = "//",
                    BlockComment = ("/*", "*/"),
                    Keywords = Words("abstract as async await base break case catch checked class const continue default delegate do else enum event explicit extern false finally fixed for foreach get goto if implicit in init interface internal is lock namespace new null operator out override params private protected public readonly record ref return sealed set sizeof stackalloc static struct switch this throw true try typeof unchecked unsafe using var virtual void volatile when where while yield not and or with nameof dynamic global partial"),
                    Types = Words("bool byte char decimal double float int long object sbyte short string uint ulong ushort nint nuint void Task ValueTask List Dictionary IEnumerable Span Memory Nullable Exception Action Func"),
                    Builtins = Words("Console Math String Enumerable Linq Environment File Path DateTime Guid Convert JsonSerializer"),
                };

            case "java":
                return new LanguageProfile
                {
                    Family = LanguageFamily.C,
                    LineComment = "//",
                    BlockComment = ("/*", "*/"),
                    Keywords = Words("abstract assert break case catch class const continue default do else enum extends final finally for goto if implements import instanceof interface native new package private protected public return static strictfp super switch synchronized this throw throws transient try volatile while var record sealed yield permit"),
                    Types = Words("boolean byte char double float int long short void String Object Integer Long Double Boolean List Map Set Optional Stream"),
                    Builtins = Words("System Math Thread Objects Arrays Collections Stream Collectors"),
                };

            case "kotlin":
                return new LanguageProfile
                {
                    Family = LanguageFamily.C,
                    LineComment = "//",
                    BlockComment = ("/*", "*/"),
                    Keywords = Words("as break class continue do else false for fun if in interface is null object package return super this throw true try typealias typeof val var when while by catch constructor delegate dynamic field file finally get import init param property receiver set setparam where actual abstract annotation companion const crossinline data enum expect external final infix inline inner internal lateinit noinline open operator out override private protected public reified sealed suspend tailrec vararg"),
                    Types = Words("Boolean Byte Char Double Float Int Long Short String Unit Any Nothing List Map Set Array"),
                    Builtins = Words("println print let also apply run with"),
                };

            case "javascript":
            case "typescript":
                return new LanguageProfile
                {
                    Family = LanguageFamily.C,
                    HasTemplateString = true,
                    LineComment = "//",
                    BlockComment = ("/*", "*/"),
                    Keywords = Words("as async await break case catch class const continue debugger default delete do else enum export extends finally for from function get if implements import in instanceof interface let new of package private protected public return set static super switch this throw try typeof var void while with yield type namespace declare readonly abstract satisfies keyof infer is never unknown any"),
                    Types = Words("string number boolean object symbol bigint undefined void null any unknown never Array Promise Record Partial Map Set Date RegExp Error"),
                    Builtins = Words("console window document Math JSON Object Array String Number Boolean Promise Symbol Reflect Proxy globalThis process require module exports setTimeout setInterval fetch localStorage"),
                };

            case "cpp":
            case "c":
                return new LanguageProfile
                {
                    Family = LanguageFamily.C,
                    LineComment = "//",
                    BlockComment = ("/*", "*/"),
                    Keywords = Words("alignas alignof asm auto break case catch class concept const consteval constexpr constinit const_cast continue co_await co_return co_yield decltype default delete do dynamic_cast else enum explicit export extern false final for friend goto if inline mutable namespace new noexcept nullptr operator override private protected public register reinterpret_cast requires return sizeof static static_assert static_cast struct switch template this thread_local throw true try typedef typeid typename union using virtual volatile while constexpr"),
                    Types = Words("bool char char8_t char16_t char32_t double float int long short signed unsigned void size_t wchar_t string vector map set unordered_map shared_ptr unique_ptr"),
                    Builtins = Words("printf malloc free memcpy strlen std cout cin endl"),
                };

            case "go":
                return new LanguageProfile
                {
                    Family = LanguageFamily.C,
                    LineComment = "//",
                    BlockComment = ("/*", "*/"),
                    Keywords = Words("break case chan const continue default defer else fallthrough for func go goto if import interface map package range return select struct switch type var"),
                    Types = Words("bool byte complex64 complex128 error float32 float64 int int8 int16 int32 int64 rune string uint uint8 uint16 uint32 uint64 uintptr any"),
                    Builtins = Words("append cap close complex copy delete imag len make new panic print println real recover nil true false iota"),
                };

            case "rust":
                return new LanguageProfile
                {
                    Family = LanguageFamily.C,
                    LineComment = "//",
                    BlockComment = ("/*", "*/"),
                    Keywords = Words("as async await break const continue crate dyn else enum extern false fn for if impl in let loop match mod move mut pub ref return self Self static struct super trait true type unsafe use where while"),
                    Types = Words("bool char f32 f64 i8 i16 i32 i64 i128 isize str String u8 u16 u32 u64 u128 usize Vec Option Result Box Rc Arc HashMap"),
                    Builtins = Words("println print vec format panic Some None Ok Err"),
                };

            case "swift":
                return new LanguageProfile
                {
                    Family = LanguageFamily.C,
                    LineComment = "//",
                    BlockComment = ("/*", "*/"),
                    Keywords = Words("associatedtype class deinit enum extension fileprivate func import init inout internal let open operator private protocol public static struct subscript typealias var break case continue default defer do else fallthrough for guard if in repeat return switch where while as any catch false is nil rethrows super self Self throw throws true try async await actor"),
                    Types = Words("Bool Character Double Float Int String Array Dictionary Set Optional Any Void"),
                    Builtins = Words("print"),
                };

            case "php":
                return new LanguageProfile
                {
                    Family = LanguageFamily.C,
                    LineComment = "//",
                    BlockComment = ("/*", "*/"),
                    Keywords = Words("abstract and array as break callable case catch class clone const continue declare default do echo else elseif empty enddeclare endfor endforeach endif endswitch endwhile enum extends final finally fn for foreach function global goto if implements include include_once instanceof insteadof interface isset list match namespace new or print private protected public readonly require require_once return static switch throw trait try unset use var while xor yield"),
                    Types = Words("bool int float string array object mixed void null callable iterable self parent static"),
                    Builtins = Words("echo print var_dump count strlen array_map array_filter json_encode json_decode"),
                };

            case "css":
            case "scss":
            case "less":
                return new LanguageProfile
                {
                    Family = LanguageFamily.Markup,
                    LineComment = null,
                    BlockComment = ("/*", "*/"),
                    Keywords = Words("important media supports keyframes import charset font-face from to and not only"),
                    Types = Words("px em rem vh vw percent fr s ms deg"),
                    Builtins = Words("var calc rgb rgba hsl hsla url env clamp min max translate rotate scale"),
                };

            case "python":
                return new LanguageProfile
                {
                    Family = LanguageFamily.Hash,
                    LineComment = "#",
                    Keywords = Words("and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield match case None True False self cls"),
                    Types = Words("int float str bool bytes list dict set tuple frozenset object type complex bytearray memoryview range enumerate zip map filter sorted len print sum min max abs any all isinstance issubclass hasattr getattr setattr super property staticmethod classmethod dataclass"),
                    Builtins = Words("print len range open input type super enumerate zip map filter sorted sum min max abs"),
                };

            case "bash":
            case "shell":
                return new LanguageProfile
                {
                    Family = LanguageFamily.Hash,
                    LineComment = "#",
                    Keywords = Words("if then else elif fi for while until do done case esac function in select time return break continue local export readonly declare unset shift eval exec trap set source alias"),
                    Types = Words("echo printf read cd pwd ls cp mv rm mkdir rmdir touch cat head tail grep sed awk sort uniq wc find xargs chmod chown ps kill curl wget git dotnet npm python pip docker kubectl systemctl"),
                    Builtins = Words("true false exit test"),
                };

            case "yaml":
                return new LanguageProfile
                {
                    Family = LanguageFamily.Hash,
                    LineComment = "#",
                    Keywords = Words("true false null yes no on off"),
                    Types = Array.Empty<string>().ToHashSet(),
                };

            case "toml":
                return new LanguageProfile
                {
                    Family = LanguageFamily.Hash,
                    LineComment = "#",
                    Keywords = Words("true false"),
                    Types = Array.Empty<string>().ToHashSet(),
                };

            case "ini":
                return new LanguageProfile
                {
                    Family = LanguageFamily.Hash,
                    LineComment = ";",
                    Keywords = Words("true false"),
                    Types = Array.Empty<string>().ToHashSet(),
                };

            case "powershell":
                return new LanguageProfile
                {
                    Family = LanguageFamily.Hash,
                    CaseInsensitive = true,
                    LineComment = "#",
                    BlockComment = ("<#", "#>"),
                    Keywords = Words("begin break catch class continue data define do dynamicparam else elseif end enum exit filter finally for foreach from function if in param process return switch throw trap try until using var while workflow"),
                    Types = Words("string int bool array hashtable pscustomobject datetime"),
                    Builtins = Words("Write-Host Write-Output Get-ChildItem Set-Location Get-Content Set-Content Where-Object ForEach-Object Select-Object New-Item Remove-Item Test-Path Join-Path"),
                };

            case "ruby":
                return new LanguageProfile
                {
                    Family = LanguageFamily.Hash,
                    LineComment = "#",
                    Keywords = Words("alias and begin break case class def defined do else elsif end ensure false for if in module next nil not or redo rescue retry return self super then true undef unless until when while yield attr_accessor attr_reader attr_writer require require_relative include extend"),
                    Types = Words("Array Hash String Integer Float Symbol Proc Method Module Class Range Regexp NilClass TrueClass FalseClass"),
                    Builtins = Words("puts print p raise lambda loop"),
                };

            case "sql":
                return new LanguageProfile
                {
                    Family = LanguageFamily.Sql,
                    CaseInsensitive = true,
                    LineComment = "--",
                    BlockComment = ("/*", "*/"),
                    Keywords = Words("add all alter and any as asc backup between by case check column constraint create database default delete desc distinct drop exec exists foreign from full group having if in index inner insert into is join key left like limit not null offset on or order outer primary procedure references right rollback rownum select set table top truncate union unique update values view where with over partition return declare begin end go output identity nolock option"),
                    Types = Words("int bigint smallint tinyint bit decimal numeric money float real datetime datetime2 smalldatetime date time char varchar nvarchar text ntext binary varbinary image uniqueidentifier xml"),
                    Builtins = Words("count sum avg min max isnull coalesce cast convert getdate dateadd datediff datediff_big substring len ltrim rtrim upper lower replace stuff row_number rank dense_rank ntile format iif choose"),
                };

            case "json":
            case "jsonc":
                return new LanguageProfile
                {
                    Family = LanguageFamily.Json,
                    LineComment = "//",
                    BlockComment = ("/*", "*/"),
                    Keywords = Words("true false null"),
                };

            case "html":
            case "xml":
            case "markup":
                return new LanguageProfile
                {
                    Family = LanguageFamily.Markup,
                    BlockComment = ("<!--", "-->"),
                };

            case "markdown":
                return new LanguageProfile
                {
                    Family = LanguageFamily.Markup,
                };

            default:
                // 未知语言：使用「C 系 + 通用关键字」的宽松规则，通常比纯文本好看。
                return new LanguageProfile
                {
                    Family = LanguageFamily.C,
                    LineComment = "//",
                    BlockComment = ("/*", "*/"),
                    Keywords = Words("if else for while return break continue class function def import from export const let var new try catch finally switch case default true false null nil void public private protected static async await yield in of do then end"),
                    Types = Array.Empty<string>().ToHashSet(),
                    Builtins = Array.Empty<string>().ToHashSet(),
                };
        }
    }

    private static HashSet<string> Words(string spaceSeparated)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in spaceSeparated.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            set.Add(w);
        return set;
    }

    // -----------------------------------------------------------------------
    // 扫描
    // -----------------------------------------------------------------------

    private static readonly Regex CIdentifier = new(@"\G[A-Za-z_$][A-Za-z0-9_$]*", RegexOptions.Compiled);
    private static readonly Regex NumberLiteral = new(
        @"\G(?:0[xXbBoO][0-9a-fA-F_]+|(?:\d[\d_]*)(?:\.\d[\d_]*)?(?:[eE][+-]?\d+)?)[fFdDmMuUlL]*",
        RegexOptions.Compiled);

    private static List<SyntaxSpan> Scan(string code, LanguageProfile p)
    {
        var result = new List<SyntaxSpan>(Math.Max(8, code.Length / 8));
        var buffer = new StringBuilder();
        int i = 0;
        var cmp = p.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        void Flush(SyntaxTokenKind kind)
        {
            if (buffer.Length == 0) return;
            result.Add(new SyntaxSpan(buffer.ToString(), kind));
            buffer.Clear();
        }

        while (i < code.Length)
        {
            char c = code[i];

            // ---- 块注释 ----
            if (p.BlockComment is { } bc && StartsWith(code, i, bc.Open))
            {
                Flush(SyntaxTokenKind.Plain);
                int end = code.IndexOf(bc.Close, i + bc.Open.Length, StringComparison.Ordinal);
                int stop = end < 0 ? code.Length : end + bc.Close.Length;
                result.Add(new SyntaxSpan(code[i..stop], SyntaxTokenKind.Comment));
                i = stop;
                continue;
            }

            // ---- 行注释 ----
            if (p.LineComment is { Length: > 0 } lc && StartsWith(code, i, lc))
            {
                Flush(SyntaxTokenKind.Plain);
                int end = code.IndexOf('\n', i);
                int stop = end < 0 ? code.Length : end;
                result.Add(new SyntaxSpan(code[i..stop], SyntaxTokenKind.Comment));
                i = stop;
                continue;
            }

            // ---- 字符串 ----
            if (c is '"' or '\'' || (p.HasTemplateString && c == '`'))
            {
                Flush(SyntaxTokenKind.Plain);
                int stop = ScanString(code, i, c);
                result.Add(new SyntaxSpan(code[i..stop], SyntaxTokenKind.String));
                i = stop;
                continue;
            }

            // ---- 数字 ----
            if (char.IsDigit(c) && (i == 0 || !(char.IsLetterOrDigit(code[i - 1]) || code[i - 1] == '_')))
            {
                var m = NumberLiteral.Match(code, i);
                if (m.Success && m.Length > 0)
                {
                    Flush(SyntaxTokenKind.Plain);
                    result.Add(new SyntaxSpan(m.Value, SyntaxTokenKind.Number));
                    i += m.Length;
                    continue;
                }
            }

            // ---- 标识符 ----
            if (char.IsLetter(c) || c == '_' || c == '$' || c > 127)
            {
                var m = CIdentifier.Match(code, i);
                if (m.Success && m.Length > 0)
                {
                    var word = m.Value;
                    if (IsWordCharBoundary(code, i + m.Length))
                    {
                        var kind = ClassifyWord(code, i, word, p, cmp);
                        Flush(kind);
                        result.Add(new SyntaxSpan(word, kind));
                        i += m.Length;
                        continue;
                    }
                }
            }

            // ---- 标点 / 运算符 ----
            var pk = IsOperator(c) ? SyntaxTokenKind.Operator : SyntaxTokenKind.Punctuation;
            if (pk != _lastKind)
            {
                Flush(_lastKind);
                _lastKind = pk;
            }
            buffer.Append(c);
            i++;
        }

        Flush(_lastKind);
        return Merge(result);
    }

    private static SyntaxTokenKind _lastKind = SyntaxTokenKind.Plain;

    private static SyntaxTokenKind ClassifyWord(string code, int pos, string word, LanguageProfile p, StringComparison cmp)
    {
        var containsUpper = false;
        for (int k = 1; k < word.Length; k++)
        {
            if (char.IsUpper(word[k])) { containsUpper = true; break; }
        }

        if (p.Keywords.Contains(word))
            return SyntaxTokenKind.Keyword;
        if (p.Types.Count > 0 && p.Types.Contains(word))
            return SyntaxTokenKind.Type;
        if (p.Builtins.Count > 0 && p.Builtins.Contains(word))
            return SyntaxTokenKind.Builtin;

        // 后接 '(' → 视为函数调用
        int j = pos + word.Length;
        while (j < code.Length && (code[j] == ' ' || code[j] == '\t')) j++;
        if (j < code.Length && code[j] == '(')
            return SyntaxTokenKind.Function;

        // 首字母大写的驼峰 → 视为类型名
        if (char.IsUpper(word[0]) && containsUpper)
            return SyntaxTokenKind.Type;

        return p.Family == LanguageFamily.Markup ? SyntaxTokenKind.Plain : SyntaxTokenKind.Plain;
    }

    private static bool IsWordCharBoundary(string code, int pos) => true;

    private static int ScanString(string code, int start, char quote)
    {
        int i = start + 1;
        while (i < code.Length)
        {
            char c = code[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }
            if (c == quote)
                return i + 1;
            if (c == '\n')
                return i; // 未闭合，遇到换行即止，避免整篇变色
            i++;
        }
        return code.Length;
    }

    private static bool StartsWith(string code, int pos, string token)
    {
        if (token.Length == 0 || pos + token.Length > code.Length)
            return false;
        return string.CompareOrdinal(code, pos, token, 0, token.Length) == 0;
    }

    private static bool IsOperator(char c) => c switch
    {
        '+' or '-' or '*' or '/' or '%' or '=' or '<' or '>' or '!' or '&' or '|' or '^' or '~' or '?' or ':' => true,
        _ => false,
    };

    private static List<SyntaxSpan> Merge(List<SyntaxSpan> spans)
    {
        if (spans.Count < 2)
            return spans;

        var merged = new List<SyntaxSpan>(spans.Count);
        var sb = new StringBuilder();
        var current = spans[0].Kind;
        sb.Append(spans[0].Text);

        for (int i = 1; i < spans.Count; i++)
        {
            if (spans[i].Kind == current)
            {
                sb.Append(spans[i].Text);
            }
            else
            {
                merged.Add(new SyntaxSpan(sb.ToString(), current));
                sb.Clear();
                current = spans[i].Kind;
                sb.Append(spans[i].Text);
            }
        }

        merged.Add(new SyntaxSpan(sb.ToString(), current));
        return merged;
    }
}
