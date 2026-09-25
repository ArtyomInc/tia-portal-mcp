using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace TiaMcpServer.OpennessWorker.ObjectModel;

/// <summary>
/// Recognizes the non-primitive Openness values that still have a plain, loss-free JSON shape:
/// <c>System.Drawing.Color</c> (WinCC colors) and <c>Siemens.Engineering.MultilingualText</c>
/// (comments, titles, captions). Both are matched by exact CLR type name and read through public
/// properties, so this stays free of any Siemens or System.Drawing compile-time dependency.
/// </summary>
public static class RichValueReader
{
    public const string ColorTypeName = "System.Drawing.Color";
    public const string MultilingualTextTypeName = "Siemens.Engineering.MultilingualText";

    /// <summary>
    /// Reads a <c>System.Drawing.Color</c> as <c>#RRGGBB</c> plus its alpha channel (0–255).
    /// </summary>
    public static bool TryReadColor(object? value, out string hex, out int alpha)
    {
        hex = string.Empty;
        alpha = 0;
        if (value is null || !string.Equals(value.GetType().FullName, ColorTypeName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryReadByte(value, "A", out var a) || !TryReadByte(value, "R", out var r)
            || !TryReadByte(value, "G", out var g) || !TryReadByte(value, "B", out var b))
        {
            return false;
        }

        hex = string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", r, g, b);
        alpha = a;
        return true;
    }

    /// <summary>
    /// Reads a <c>MultilingualText</c> as a culture-name → text map (for example
    /// <c>{"en-US": "Start"}</c>). Items whose language or text cannot be read are skipped; the
    /// read fails only when the item collection itself is unreadable.
    /// </summary>
    public static bool TryReadMultilingualText(object? value, out IReadOnlyDictionary<string, string> texts)
    {
        texts = new Dictionary<string, string>();
        if (value is null || !string.Equals(value.GetType().FullName, MultilingualTextTypeName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryReadProperty(value, "Items", out var items) || items is not IEnumerable sequence)
        {
            return false;
        }

        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var item in sequence)
            {
                if (item is null
                    || !TryReadProperty(item, "Text", out var text)
                    || !TryReadProperty(item, "Language", out var language)
                    || language is null
                    || !TryReadProperty(language, "Culture", out var culture)
                    || culture is not CultureInfo cultureInfo
                    || string.IsNullOrEmpty(cultureInfo.Name))
                {
                    continue;
                }

                map[cultureInfo.Name] = text as string ?? string.Empty;
            }
        }
        catch (Exception)
        {
            return false;
        }

        texts = new Dictionary<string, string>(map, StringComparer.Ordinal);
        return true;
    }

    private static bool TryReadByte(object value, string name, out byte result)
    {
        result = 0;
        if (!TryReadProperty(value, name, out var raw) || raw is not byte channel)
        {
            return false;
        }

        result = channel;
        return true;
    }

    private static bool TryReadProperty(object value, string name, out object? result)
    {
        result = null;
        PropertyInfo? property;
        try
        {
            property = value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        }
        catch (AmbiguousMatchException)
        {
            return false;
        }

        if (property is null || !property.CanRead || property.GetIndexParameters().Length != 0)
        {
            return false;
        }

        try
        {
            result = property.GetValue(value);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
