using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TiaMcpServer.OpennessWorker.ObjectModel;

/// <summary>
/// Converts an Openness value into a plain JSON value for domain listings: strings, booleans,
/// integers, finite numbers, enum symbols, ISO-8601 dates, version strings, colors
/// (<c>{"hex": "#RRGGBB", "alpha": 255}</c>), multilingual texts (culture → text), and short
/// arrays of scalars. Anything else — engineering objects, structures — is not representable and
/// is reported by name instead of being rendered through <c>ToString()</c>.
/// </summary>
public static class ObjectScalarNormalizer
{
    public const int MaxArrayLength = 100;

    public static bool TryNormalize(object? value, out object? json)
    {
        switch (value)
        {
            case null:
                json = null;
                return true;
            case string text:
                json = text;
                return true;
            case char character:
                json = character.ToString();
                return true;
            case bool boolean:
                json = boolean;
                return true;
            case Enum enumValue:
                json = enumValue.ToString();
                return true;
            case sbyte or byte or short or ushort or int or uint or long:
                json = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return true;
            case ulong unsignedLong:
                json = unsignedLong <= long.MaxValue ? (object)(long)unsignedLong : unsignedLong.ToString(CultureInfo.InvariantCulture);
                return true;
            case float single when !float.IsNaN(single) && !float.IsInfinity(single):
                json = (double)single;
                return true;
            case double number when !double.IsNaN(number) && !double.IsInfinity(number):
                json = number;
                return true;
            case decimal decimalValue:
                json = decimalValue;
                return true;
            case DateTime dateTime:
                json = dateTime.ToString("o", CultureInfo.InvariantCulture);
                return true;
            case DateTimeOffset dateTimeOffset:
                json = dateTimeOffset.ToString("o", CultureInfo.InvariantCulture);
                return true;
            case TimeSpan timeSpan:
                json = timeSpan.ToString("c", CultureInfo.InvariantCulture);
                return true;
            case Version version:
                json = version.ToString();
                return true;
            case Guid guid:
                json = guid.ToString("D");
                return true;
            case System.IO.FileSystemInfo fileSystemInfo:
                json = fileSystemInfo.FullName;
                return true;
            case not null when RichValueReader.TryReadColor(value, out var hex, out var alpha):
                json = new Dictionary<string, object?>(StringComparer.Ordinal) { ["hex"] = hex, ["alpha"] = (long)alpha };
                return true;
            case not null when RichValueReader.TryReadMultilingualText(value, out var texts):
                json = texts;
                return true;
            case IEnumerable sequence when value.GetType().IsArray:
                return TryNormalizeArray(sequence, out json);
            default:
                json = null;
                return false;
        }
    }

    private static bool TryNormalizeArray(IEnumerable sequence, out object? json)
    {
        var items = new List<object?>();
        foreach (var item in sequence)
        {
            if (items.Count == MaxArrayLength || item is IEnumerable and not string || !TryNormalize(item, out var normalized))
            {
                json = null;
                return false;
            }

            items.Add(normalized);
        }

        json = items;
        return true;
    }

    /// <summary>
    /// Reads <paramref name="names"/> from <paramref name="node"/> into a value map. Undeclared
    /// names are skipped; declared ones that fail or cannot be represented go to
    /// <paramref name="unavailable"/>. A null name list reads every declared attribute.
    /// </summary>
    public static Dictionary<string, object?> ReadValues(
        IObjectNode node,
        IReadOnlyList<string>? names,
        List<string> unavailable)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        IEnumerable<string> selected;
        if (names is not null)
        {
            selected = names;
        }
        else
        {
            var declared = new List<string>();
            try
            {
                foreach (var attribute in node.GetAttributes())
                {
                    // A multilingual text is an engineering object, but it has a plain JSON shape.
                    var listable = !attribute.Navigable
                        || attribute.SupportedTypes.Contains(RichValueReader.MultilingualTextTypeName, StringComparer.Ordinal);
                    if (listable && !string.Equals(attribute.Access, "writeOnly", StringComparison.Ordinal))
                    {
                        declared.Add(attribute.Name);
                    }
                }
            }
            catch (Exception)
            {
                unavailable.Add("*");
            }

            declared.Sort(StringComparer.Ordinal);
            selected = declared;
        }

        foreach (var name in selected)
        {
            var read = node.ReadValue(name);
            if (!read.IsDeclared)
            {
                continue;
            }

            if (read.Succeeded && TryNormalize(read.Value, out var json))
            {
                values[name] = json;
            }
            else if (names is not null || !read.Succeeded)
            {
                // A requested name, or any failed read, is reported. When every declared attribute
                // is read, a readable but non-scalar value (a collection, an association) is simply
                // not part of a scalar listing and is skipped.
                unavailable.Add(name);
            }
        }

        return values;
    }
}
