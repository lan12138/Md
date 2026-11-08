// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Models/FiniteNullableDoubleConverter.cs
//  说明：double? 版本的非有限浮点保护。窗口 X/Y 用 double? 表示「未记录」，
//        源生成序列化会按属性上的 [JsonConverter] 选择这个转换器，
//        把 NaN / ±Infinity（例如 Win32 的最小化哨兵）安全地写成 null，
//        而不是让 System.Text.Json 抛异常导致整份 settings.json 保存失败。
//  作者：MD
//  创建：2026-11-08
//  修改：2026-11-08  初版
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MD.Models;

public sealed class FiniteNullableDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : reader.GetDouble();

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value is { } v && double.IsFinite(v))
            writer.WriteNumberValue(v);
        else
            writer.WriteNullValue();
    }
}
