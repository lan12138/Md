// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Models/FiniteDoubleConverter.cs
//  说明：把 NaN / ±Infinity 写成 null 的 double 转换器。
//
//        为什么必须有它：System.Text.Json 默认**拒绝**写出非有限浮点数，会抛
//        ArgumentException（"positive and negative infinity cannot be written
//        as valid JSON"）。而「窗口还没定位」这类值天然可能就是 NaN，
//        一旦序列化抛异常，整份 settings.json 就一次都写不成功 ——
//        用户看到的现象是「设置怎么改都不保存」，极难定位。
//        有了它，任何环节漏进来的非有限值都只会降级成 null，不会拖垮整次保存。
//
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MD.Models;

public sealed class FiniteDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // null 读回 NaN，保证 double? 之外的旧字段（double）行为不变
        return reader.TokenType == JsonTokenType.Null ? double.NaN : reader.GetDouble();
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (double.IsFinite(value))
            writer.WriteNumberValue(value);
        else
            writer.WriteNullValue();
    }
}
