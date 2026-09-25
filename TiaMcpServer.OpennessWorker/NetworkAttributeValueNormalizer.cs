using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.ObjectModel;

namespace TiaMcpServer.OpennessWorker;

public sealed class NetworkAttributeNormalizationResult
{
    public bool IsRepresentable { get; set; }
    public NetworkAttributeValueInfo? Value { get; set; }
    public string? ClrTypeName { get; set; }
}

public static class NetworkAttributeValueNormalizer
{
    /// <summary>Largest array published as kind <c>array</c>; longer arrays are unrepresentable.</summary>
    public const int MaxArrayLength = 100;

    public static NetworkAttributeNormalizationResult Normalize(object? input)
        => Normalize(input, allowArray: true);

    private static NetworkAttributeNormalizationResult Normalize(object? input, bool allowArray)
    {
        if (input is null)
        {
            return new NetworkAttributeNormalizationResult
            {
                IsRepresentable = true,
                Value = new NetworkAttributeValueInfo
                {
                    Kind = "null",
                    Value = null,
                    TypeName = null,
                },
            };
        }

        var typeName = input.GetType().FullName;
        if (input is string text)
        {
            return Representable("string", text, typeName);
        }

        if (input is char character)
        {
            return Representable("string", character.ToString(), typeName);
        }

        if (input is bool boolean)
        {
            return Representable("boolean", boolean, typeName);
        }

        if (input is Enum enumValue)
        {
            return NormalizeEnum(enumValue, typeName);
        }

        if (input is sbyte || input is byte || input is short || input is ushort || input is int || input is uint || input is long)
        {
            return Representable("integer", Convert.ToInt64(input), typeName);
        }

        if (input is ulong unsignedLong)
        {
            return unsignedLong <= long.MaxValue
                ? Representable("integer", (long)unsignedLong, typeName)
                : Unrepresentable(typeName);
        }

        if (input is float single)
        {
            return float.IsNaN(single) || float.IsInfinity(single)
                ? Unrepresentable(typeName)
                : Representable("number", single, typeName);
        }

        if (input is double dbl)
        {
            return double.IsNaN(dbl) || double.IsInfinity(dbl)
                ? Unrepresentable(typeName)
                : Representable("number", dbl, typeName);
        }

        if (input is decimal decimalValue)
        {
            return Representable("number", decimalValue, typeName);
        }

        if (input is DateTime dateTime)
        {
            return Representable("dateTime", dateTime.ToString("o", CultureInfo.InvariantCulture), typeName);
        }

        if (input is DateTimeOffset dateTimeOffset)
        {
            return Representable("dateTime", dateTimeOffset.ToString("o", CultureInfo.InvariantCulture), typeName);
        }

        if (input is TimeSpan timeSpan)
        {
            return Representable("duration", timeSpan.ToString("c", CultureInfo.InvariantCulture), typeName);
        }

        if (input is Guid guid)
        {
            return Representable("string", guid.ToString("D"), typeName);
        }

        if (input is Version version)
        {
            return Representable("string", version.ToString(), typeName);
        }

        if (RichValueReader.TryReadColor(input, out var hex, out var alpha))
        {
            return Representable("color", new NetworkColorValueInfo { Hex = hex, Alpha = alpha }, typeName);
        }

        if (RichValueReader.TryReadMultilingualText(input, out var texts))
        {
            return Representable("multilingualText", texts, typeName);
        }

        if (allowArray && input is IEnumerable sequence && input.GetType().IsArray)
        {
            return NormalizeArray(sequence, typeName);
        }

        return Unrepresentable(typeName);
    }

    private static NetworkAttributeNormalizationResult NormalizeArray(IEnumerable sequence, string? typeName)
    {
        var items = new List<NetworkAttributeValueInfo>();
        foreach (var item in sequence)
        {
            if (items.Count == MaxArrayLength)
            {
                return Unrepresentable(typeName);
            }

            var normalized = Normalize(item, allowArray: false);
            if (!normalized.IsRepresentable)
            {
                return Unrepresentable(typeName);
            }

            items.Add(normalized.Value!);
        }

        return Representable("array", items, typeName);
    }

    private static NetworkAttributeNormalizationResult NormalizeEnum(Enum value, string? typeName)
    {
        try
        {
            var numericValue = Convert.ToInt64(value);
            return Representable(
                "enum",
                new NetworkEnumValueInfo
                {
                    TypeName = typeName ?? string.Empty,
                    Symbol = Enum.GetName(value.GetType(), value) ?? string.Empty,
                    NumericValue = numericValue,
                },
                typeName);
        }
        catch (OverflowException)
        {
            return Unrepresentable(typeName);
        }
    }

    private static NetworkAttributeNormalizationResult Representable(string kind, object value, string? typeName)
        => new()
        {
            IsRepresentable = true,
            Value = new NetworkAttributeValueInfo
            {
                Kind = kind,
                Value = value,
                TypeName = typeName,
            },
        };

    private static NetworkAttributeNormalizationResult Unrepresentable(string? typeName)
        => new() { ClrTypeName = typeName };
}
