using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.ObjectModel;

/// <summary>
/// Opaque offset cursor bound to one query and one snapshot of an ordered result. Shared by every
/// paged generic and domain read so each listing does not invent its own continuation format.
///
/// <para>
/// A cursor is not authenticated: it only carries hashes and an offset, and every one of them is
/// re-derived from the live project and checked on decode. Replaying a cursor against another
/// query fails <c>cursor_filter_mismatch</c>; replaying it after the listed objects changed fails
/// <c>cursor_snapshot_mismatch</c>. A caller can therefore never be handed a page of a different
/// listing than the one the cursor was issued for.
/// </para>
/// </summary>
public static class OffsetCursorCodec
{
    private static readonly Regex LowercaseSha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex UnpaddedBase64Url = new("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant);

    public const int MaxCursorLength = 1024;

    /// <summary>Hashes length-prefixed parts so no two part lists share a hash input.</summary>
    public static string Hash(IEnumerable<string?> parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            if (part is null)
            {
                builder.Append("-1;");
                continue;
            }

            builder.Append(part.Length).Append(':').Append(part).Append(';');
        }

        using var algorithm = SHA256.Create();
        return ToHex(algorithm.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    public static string Encode(int offset, string queryHash, string snapshotHash)
    {
        var json = "{\"v\":1,\"o\":" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"q\":\"" + queryHash + "\",\"s\":\"" + snapshotHash + "\"}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>Returns the offset carried by <paramref name="cursor"/> after every check passed.</summary>
    public static int Decode(string cursor, string expectedQueryHash, string expectedSnapshotHash, int totalCount)
    {
        int version;
        int offset;
        string? queryHash;
        string? snapshotHash;
        try
        {
            if (string.IsNullOrEmpty(cursor)
                || cursor.Length > MaxCursorLength
                || !UnpaddedBase64Url.IsMatch(cursor)
                || cursor.Length % 4 == 1)
            {
                throw new FormatException();
            }

            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(normalized)));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException();
            }

            var count = 0;
            foreach (var _ in root.EnumerateObject())
            {
                count++;
            }

            if (count != 4)
            {
                throw new FormatException();
            }

            version = root.GetProperty("v").GetInt32();
            offset = root.GetProperty("o").GetInt32();
            queryHash = root.GetProperty("q").GetString();
            snapshotHash = root.GetProperty("s").GetString();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException
            or InvalidOperationException or KeyNotFoundException)
        {
            throw Cursor(WorkerFailureCategories.InvalidCursor);
        }

        if (version != 1 || offset < 0 || !IsHash(queryHash) || !IsHash(snapshotHash))
        {
            throw Cursor(WorkerFailureCategories.InvalidCursor);
        }

        if (!string.Equals(queryHash, expectedQueryHash, StringComparison.Ordinal))
        {
            throw Cursor(WorkerFailureCategories.CursorFilterMismatch);
        }

        if (!string.Equals(snapshotHash, expectedSnapshotHash, StringComparison.Ordinal))
        {
            throw Cursor(WorkerFailureCategories.CursorSnapshotMismatch);
        }

        if (offset >= totalCount && !(offset == 0 && totalCount == 0))
        {
            throw Cursor(WorkerFailureCategories.CursorOutOfRange);
        }

        return offset;
    }

    private static bool IsHash(string? value) => value is not null && LowercaseSha256.IsMatch(value);

    private static WorkerOperationException Cursor(string category)
        => new WorkerOperationException(category, category switch
        {
            WorkerFailureCategories.CursorFilterMismatch =>
                "The cursor was issued for a different query; resend the original query fields or start without a cursor.",
            WorkerFailureCategories.CursorSnapshotMismatch =>
                "The listed objects changed since the cursor was issued; start again without a cursor.",
            WorkerFailureCategories.CursorOutOfRange =>
                "The cursor points past the end of the listing; start again without a cursor.",
            _ => "The supplied cursor is not valid.",
        });
}
