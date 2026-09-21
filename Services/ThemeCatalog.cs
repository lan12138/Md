// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Services/ThemeCatalog.cs
//  说明：主题仓库。负责
//        · 加载内置主题（模板 + 差异覆盖）
//        · 扫描 <配置目录>/themes/*.json 加载用户自制主题（可覆盖同 id 内置主题）
//        · 提供「跟随系统」明暗解析
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  重写模板合并：改为直接在「原始 JSON 节点」上做差异覆盖。
//                   旧实现先把主题反序列化成 ThemeDefinition，而该对象的 Colors
//                   带有全套**亮色**CLR 默认值，再序列化回去就把暗色模板整个盖掉了，
//                   于是 github-dark / night / vue-dark / dracula 全部渲染成亮色
//                   （「明暗切换失效」的真正原因）。
//  修改：2026-09-21  新增 DeriveShades：按主题底色派生侧栏页签条 / 当前项底色
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MD.Models;

namespace MD.Services;

public sealed class ThemeCatalog
{
    private readonly List<ThemeDefinition> _themes = new();
    private readonly Dictionary<string, ThemeDefinition> _index = new(StringComparer.OrdinalIgnoreCase);

    public ThemeCatalog()
    {
        Reload();
    }

    /// <summary>按内置顺序排列的主题清单。</summary>
    public IReadOnlyList<ThemeDefinition> Themes => _themes;

    public IReadOnlyList<ThemeDefinition> LightThemes => _themes.Where(t => !t.IsDark).ToList();

    public IReadOnlyList<ThemeDefinition> DarkThemes => _themes.Where(t => t.IsDark).ToList();

    /// <summary>重新扫描（内置 + 用户目录）。</summary>
    public void Reload()
    {
        _themes.Clear();
        _index.Clear();

        // 1) 内置主题：Definitions 本身就是「差异 JSON」，直接拿去和明/暗模板合并
        try
        {
            if (JsonNode.Parse(BuiltinThemes.Definitions) is JsonArray array)
            {
                foreach (var node in array)
                {
                    if (node is not JsonObject def)
                        continue;
                    var merged = ApplyTemplate(def);
                    if (merged is null)
                        continue;
                    merged.IsBuiltIn = true;
                    Add(merged);
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("内置主题加载失败", ex);
        }

        // 2) 用户在 <配置目录>/themes 下的自定义主题（同 id 覆盖内置）
        try
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.ThemesDirectory, "*.json"))
            {
                try
                {
                    // 同样保留原始 JSON：只覆盖「显式写了」的字段，其余继承模板
                    if (JsonNode.Parse(File.ReadAllText(file, System.Text.Encoding.UTF8)) is not JsonObject def)
                        continue;

                    var merged = ApplyTemplate(def);
                    if (merged is null || string.IsNullOrWhiteSpace(merged.Id))
                        continue;

                    merged.IsBuiltIn = false;
                    if (string.IsNullOrWhiteSpace(merged.Author))
                        merged.Author = "用户";
                    Add(merged, replace: true);
                }
                catch (Exception ex)
                {
                    DiagnosticsLog.Write($"用户主题 '{file}' 加载失败", ex);
                }
            }
        }
        catch
        {
            // 用户主题目录不可读时忽略
        }

        if (_themes.Count == 0)
            Add(BuildFallback());
    }

    private void Add(ThemeDefinition theme, bool replace = false)
    {
        if (replace && _index.TryGetValue(theme.Id, out var existing))
        {
            int idx = _themes.IndexOf(existing);
            if (idx >= 0)
                _themes[idx] = theme;
            _index[theme.Id] = theme;
            return;
        }

        if (_index.ContainsKey(theme.Id))
            return;

        _themes.Add(theme);
        _index[theme.Id] = theme;
    }

    public ThemeDefinition Get(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && _index.TryGetValue(id, out var theme))
            return theme;
        return _themes.Count > 0 ? _themes[0] : BuildFallback();
    }

    /// <summary>解析当前应使用的主题 Id（考虑跟随系统）。</summary>
    public string ResolveThemeId(AppSettings settings, bool systemIsDark)
    {
        var appearance = settings.Appearance;
        if (appearance.FollowSystemTheme)
            return systemIsDark ? appearance.DarkThemeId : appearance.ThemeId;

        var t = Get(appearance.ThemeId);
        return t.Id;
    }

    /// <summary>确保指定 id 对应的主题符合期望的明暗；不符合则在该明暗集合中挑一个。</summary>
    public ThemeDefinition Resolve(AppSettings settings, bool systemIsDark)
    {
        ThemeDefinition theme;
        if (settings.Appearance.FollowSystemTheme)
        {
            theme = Get(systemIsDark ? settings.Appearance.DarkThemeId : settings.Appearance.ThemeId);
            if (theme.IsDark != systemIsDark)
            {
                var pool = systemIsDark ? DarkThemes : LightThemes;
                if (pool.Count > 0)
                    theme = pool[0];
            }
        }
        else
        {
            theme = Get(settings.Appearance.ThemeId);
        }
        return theme;
    }

    /// <summary>
    /// 把主题差异覆盖到明/暗模板之上。
    ///
    /// 入参是**原始 JSON 节点**而不是已反序列化的对象 —— 这一点是必须的：
    /// ThemeDefinition.Colors 的属性带有整套亮色默认值，一旦先反序列化再合并，
    /// 那些默认值会当成「用户显式指定的颜色」把暗色模板覆盖掉。
    /// 走原始 JSON 才能区分「没写」和「写了」。
    /// </summary>
    private static ThemeDefinition? ApplyTemplate(JsonObject defNode)
    {
        bool isDark = ReadBool(defNode, "isDark", false);
        var templateJson = isDark ? BuiltinThemes.DarkTemplate : BuiltinThemes.LightTemplate;

        try
        {
            var templateNode = JsonNode.Parse(templateJson)!.AsObject();

            foreach (var kv in defNode)
            {
                if (kv.Value is null)
                    continue;

                if (kv.Key.Equals("colors", StringComparison.OrdinalIgnoreCase) && kv.Value is JsonObject colorOverrides)
                {
                    var target = templateNode["colors"]!.AsObject();
                    foreach (var c in colorOverrides)
                    {
                        if (c.Value is null)
                            continue;
                        var colorValue = c.Value;
                        // 兼容 "#RRGGBB"（无 alpha）写法，统一补成 #AARRGGBB
                        if (colorValue is JsonValue jv && jv.TryGetValue<string>(out var hex))
                            colorValue = JsonValue.Create(NormalizeHex(hex));
                        target[c.Key] = colorValue?.DeepClone();
                    }
                }
                else if (kv.Key.Equals("typography", StringComparison.OrdinalIgnoreCase) && kv.Value is JsonObject typoOverrides)
                {
                    JsonObject target;
                    if (templateNode["typography"] is JsonObject existing)
                    {
                        target = existing;
                    }
                    else
                    {
                        target = new JsonObject();
                        templateNode["typography"] = target;
                    }

                    foreach (var t in typoOverrides)
                    {
                        if (t.Value is null)
                            continue;
                        target[t.Key] = t.Value.DeepClone();
                    }
                }
                else
                {
                    templateNode[kv.Key] = kv.Value.DeepClone();
                }
            }

            var merged = templateNode.Deserialize<ThemeDefinition>(ThemeJson.Options);
            if (merged is null)
                return null;

            // 身份字段以主题定义为准（模板里没有这些）
            merged.Id = ReadString(defNode, "id") ?? merged.Id;
            merged.Name = ReadString(defNode, "name") ?? merged.Name;
            merged.Author = ReadString(defNode, "author");
            merged.IsDark = isDark;

            Sanitize(merged);
            DeriveShades(merged);
            return merged;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write($"主题合并失败: {ReadString(defNode, "id")}", ex);
            return null;
        }
    }

    private static string? ReadString(JsonObject node, string key)
    {
        if (!node.TryGetPropertyValue(key, out var value) || value is not JsonValue v)
            return null;
        return v.TryGetValue<string>(out var s) ? s : null;
    }

    private static bool ReadBool(JsonObject node, string key, bool fallback)
    {
        if (!node.TryGetPropertyValue(key, out var value) || value is not JsonValue v)
            return fallback;
        return v.TryGetValue<bool>(out var b) ? b : fallback;
    }

    private static void Sanitize(ThemeDefinition def)
    {
        var t = def.Typography;
        t.BaseFontSize = Math.Clamp(t.BaseFontSize, 9, 40);
        t.LineHeight = Math.Clamp(t.LineHeight, 1.0, 3.0);
        if (t.ContentMaxWidth < 0) t.ContentMaxWidth = 0;
        t.ContentPadding = Math.Clamp(t.ContentPadding, 0, 200);
        if (t.HeadingScale is null || t.HeadingScale.Count < 6)
            t.HeadingScale = new List<double> { 1.92, 1.55, 1.28, 1.12, 1.0, 0.94 };
        t.ParagraphSpacing = Math.Clamp(t.ParagraphSpacing, 0, 4);
        t.HeadingMarginTop = Math.Clamp(t.HeadingMarginTop, 0, 5);
        t.HeadingMarginBottom = Math.Clamp(t.HeadingMarginBottom, 0, 5);
    }

    /// <summary>
    /// 按主题自己的底色派生「侧栏页签条」和「侧栏当前项」底色。
    ///
    /// 目的：这两个位置的底色必须和主题同一色系、只差几个色号，
    /// 而不能是强调色实心块（那会和正文底色正面冲突，非常突兀）。
    /// 主题若显式写了 sidebarHeaderBg / sidebarActiveBg 则以主题为准。
    /// </summary>
    private static void DeriveShades(ThemeDefinition def)
    {
        var c = def.Colors;

        if (string.IsNullOrWhiteSpace(c.SidebarHeaderBg))
        {
            // 亮色：往窗口底色方向提亮，得到「和主题差不多的白」；
            // 暗色：不能往白走太多，轻微提亮即可，否则会刺眼。
            c.SidebarHeaderBg = def.IsDark
                ? Shade(c.SidebarBg, 0.045)
                : Mix(c.SidebarBg, c.WindowBg, 0.75);
        }

        if (string.IsNullOrWhiteSpace(c.SidebarActiveBg))
        {
            // 亮色：比侧栏底色深几个色号；暗色：比侧栏底色浅几个色号。
            c.SidebarActiveBg = def.IsDark ? Shade(c.SidebarBg, 0.11) : Shade(c.SidebarBg, -0.10);
        }
    }

    /// <summary>把颜色朝白色（amount &gt; 0）或黑色（amount &lt; 0）推进 amount 比例。</summary>
    internal static string Shade(string? hex, double amount)
    {
        var (a, r, g, b) = Split(hex);
        double k = Math.Clamp(Math.Abs(amount), 0, 1);
        int target = amount >= 0 ? 255 : 0;
        r = (byte)Math.Round(r + (target - r) * k);
        g = (byte)Math.Round(g + (target - g) * k);
        b = (byte)Math.Round(b + (target - b) * k);
        return $"#{a:X2}{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>两色按比例混合（amount 为 to 的占比）。</summary>
    internal static string Mix(string? from, string? to, double amount)
    {
        var (a1, r1, g1, b1) = Split(from);
        var (_, r2, g2, b2) = Split(to);
        double k = Math.Clamp(amount, 0, 1);
        byte r = (byte)Math.Round(r1 + (r2 - r1) * k);
        byte g = (byte)Math.Round(g1 + (g2 - g1) * k);
        byte b = (byte)Math.Round(b1 + (b2 - b1) * k);
        return $"#{a1:X2}{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>把 #RRGGBB / #AARRGGBB 拆成 (A, R, G, B)。</summary>
    private static (byte A, byte R, byte G, byte B) Split(string? hex)
    {
        var n = NormalizeHex(hex ?? "#000000");
        return (
            byte.Parse(n.AsSpan(1, 2), NumberStyles.HexNumber),
            byte.Parse(n.AsSpan(3, 2), NumberStyles.HexNumber),
            byte.Parse(n.AsSpan(5, 2), NumberStyles.HexNumber),
            byte.Parse(n.AsSpan(7, 2), NumberStyles.HexNumber));
    }

    internal static string NormalizeHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return "#FF000000";
        hex = hex.Trim();
        if (!hex.StartsWith('#'))
            hex = "#" + hex;
        return hex.Length switch
        {
            4 => $"#{hex[1]}{hex[1]}{hex[2]}{hex[2]}{hex[3]}{hex[3]}",           // #RGB
            5 => $"#{hex[1]}{hex[1]}{hex[2]}{hex[2]}{hex[3]}{hex[3]}{hex[4]}{hex[4]}", // #ARGB
            7 => "#FF" + hex[1..],                                              // #RRGGBB
            9 => hex,                                                           // #AARRGGBB
            _ => "#FF000000",
        };
    }

    private static ThemeDefinition BuildFallback()
    {
        var def = new ThemeDefinition
        {
            Id = "fallback",
            Name = "默认",
            IsDark = false,
            IsBuiltIn = true,
        };
        Sanitize(def);
        DeriveShades(def);
        return def;
    }
}
