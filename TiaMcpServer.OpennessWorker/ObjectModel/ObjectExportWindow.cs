using System.Security.Cryptography;
using System.Text;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.ObjectModel;

/// <summary>
/// Cuts one character window out of an exported document. The SHA-256 covers the whole document so
/// a caller reassembling several windows can prove it received one consistent export.
/// </summary>
public static class ObjectExportWindow
{
    public static void Apply(ObjectExportInfo target, string document, int? offset, int? maxChars)
    {
        var start = offset ?? 0;
        var length = maxChars ?? ObjectReadLimits.DefaultExportMaxChars;
        if (length < 1 || length > ObjectReadLimits.MaxExportMaxChars)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"ObjectExportMaxChars must be between 1 and {ObjectReadLimits.MaxExportMaxChars}.");
        }

        if (start < 0 || (start >= document.Length && !(start == 0 && document.Length == 0)))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"ObjectExportOffset {start} is outside the exported document ({document.Length} characters).");
        }

        var end = start + length < document.Length ? start + length : document.Length;

        // Never split a UTF-16 surrogate pair across two windows.
        if (end < document.Length && end > start + 1 && char.IsHighSurrogate(document[end - 1]))
        {
            end--;
        }

        using var algorithm = SHA256.Create();
        target.TotalChars = document.Length;
        target.Sha256 = OffsetCursorCodec.ToHex(algorithm.ComputeHash(Encoding.UTF8.GetBytes(document)));
        target.Offset = start;
        target.Content = document.Substring(start, end - start);
        target.NextOffset = end < document.Length ? end : null;
    }
}
