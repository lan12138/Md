// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Models/AppJsonContext.cs
//  说明：System.Text.Json 的源生成上下文（trim / AOT 安全）。
//
//        为什么需要：裁剪发布时 .NET 默认关闭反射式序列化
//        （System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault=false），
//        没有 JsonTypeInfo 的 Serialize/Deserialize 会抛
//        InvalidOperationException：设置永远保存不进去、主题也读不出来。
//        把设置与主题的类型登记到源生成上下文后，裁剪版不再依赖反射。
//
//        新增可序列化的模型时，必须在这里补一个 [JsonSerializable]，
//        否则裁剪版会静默失败（Debug 版仍然正常，很容易漏）。
//  作者：MD
//  创建：2026-11-08
//  修改：2026-11-08  初版
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MD.Models;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ThemeDefinition))]
[JsonSerializable(typeof(ThemePalette))]
[JsonSerializable(typeof(ThemeTypography))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<string>))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
