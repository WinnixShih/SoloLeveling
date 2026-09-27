using System.Text.Json;
using System.Text.Json.Serialization;
using SoloLeveling.Domain;

namespace SoloLeveling.Api.Contracts;

/// <summary>
/// <see cref="StatType"/> 在 API 上以代碼（STR／VIT／INT／WIL／SPI）表示。
/// </summary>
public class StatTypeJsonConverter : JsonConverter<StatType>
{
    private static readonly Dictionary<StatType, string> Codes = new()
    {
        [StatType.Strength] = "STR",
        [StatType.Vitality] = "VIT",
        [StatType.Intelligence] = "INT",
        [StatType.Willpower] = "WIL",
        [StatType.Spirit] = "SPI",
    };

    private static readonly Dictionary<string, StatType> ByCode = Codes.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 把代碼字串讀成 <see cref="StatType"/>。
    /// </summary>
    /// <param name="reader">JSON reader。</param>
    /// <param name="typeToConvert">目標型別。</param>
    /// <param name="options">序列化選項。</param>
    /// <returns>屬性。</returns>
    /// <exception cref="JsonException">不是合法代碼。</exception>
    public override StatType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var code = reader.GetString();
        if (code is null || !ByCode.TryGetValue(code, out var stat))
        {
            throw new JsonException($"未知的屬性代碼：{code}");
        }

        return stat;
    }

    /// <summary>
    /// 把 <see cref="StatType"/> 寫成代碼字串。
    /// </summary>
    /// <param name="writer">JSON writer。</param>
    /// <param name="value">屬性。</param>
    /// <param name="options">序列化選項。</param>
    public override void Write(Utf8JsonWriter writer, StatType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(Codes[value]);
    }
}
