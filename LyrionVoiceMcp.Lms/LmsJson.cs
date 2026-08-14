using System.Globalization;
using System.Text.Json;

namespace LyrionVoiceMcp.Lms;

internal static class LmsJson
{
    public static string ReadRequiredString(
        JsonElement element,
        string name,
        string responseName)
    {
        var value = ReadString(element, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"LMS {responseName} response contained an item without {name}.");
        }

        return value;
    }

    public static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    public static int? ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var numberValue))
        {
            return numberValue;
        }

        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var stringValue))
        {
            return stringValue;
        }

        return null;
    }

    public static double? ReadDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out var numberValue))
        {
            return numberValue;
        }

        if (property.ValueKind == JsonValueKind.String
            && double.TryParse(
                property.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var stringValue))
        {
            return stringValue;
        }

        return null;
    }

    public static bool ReadRequiredBoolean(
        JsonElement element,
        string name,
        string responseName)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            throw new InvalidOperationException(
                $"LMS {responseName} response contained an item without {name}.");
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        var value = ReadString(element, name);
        return value switch
        {
            "1" => true,
            "0" => false,
            _ => throw new InvalidOperationException(
                $"LMS {responseName} response contained an invalid {name} value.")
        };
    }
}
