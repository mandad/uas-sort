// src/UasSort.Core/Json/Converters.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UasSort.Core.Json;

/// <summary>GeoPoint as [lon,lat] (GeoJSON order, shared with map.js).</summary>
public sealed class GeoPointJsonConverter : JsonConverter<GeoPoint>
{
    public override GeoPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("GeoPoint must be [lon,lat]");
        reader.Read();
        var lon = reader.GetDouble();
        reader.Read();
        var lat = reader.GetDouble();
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("GeoPoint must be [lon,lat]");
        return new GeoPoint(lat, lon);
    }

    public override void Write(Utf8JsonWriter writer, GeoPoint value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Lon);
        writer.WriteNumberValue(value.Lat);
        writer.WriteEndArray();
    }
}

/// <summary>ItemId as its card-relative path string.</summary>
public sealed class ItemIdJsonConverter : JsonConverter<ItemId>
{
    public override ItemId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? throw new JsonException("ItemId must be a string"));

    public override void Write(Utf8JsonWriter writer, ItemId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.CardRelPath);
    }
}
