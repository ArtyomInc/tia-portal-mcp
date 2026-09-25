using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.ObjectModel;

/// <summary>One page of an ordered listing, with its continuation.</summary>
public sealed class ListPage<T>
{
    public ListPage(IReadOnlyList<T> items, int totalCount, int offset, string? nextCursor)
    {
        Items = items;
        TotalCount = totalCount;
        Offset = offset;
        NextCursor = nextCursor;
    }

    public IReadOnlyList<T> Items { get; }
    public int TotalCount { get; }
    public int Offset { get; }
    public string? NextCursor { get; }
}

/// <summary>
/// Pages a complete ordered listing with an <see cref="OffsetCursorCodec"/> cursor bound to the
/// query and to the identity of every listed item, so a continuation never mixes two different
/// listings.
/// </summary>
public static class ListPager
{
    public static ListPage<T> Page<T>(
        IReadOnlyList<T> ordered,
        IEnumerable<string?> queryParts,
        Func<T, IEnumerable<string?>> identity,
        int? pageSize,
        string? cursor)
    {
        var size = pageSize ?? ObjectReadLimits.DefaultPageSize;
        if (size < 1 || size > ObjectReadLimits.MaxPageSize)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"Page size must be between 1 and {ObjectReadLimits.MaxPageSize}.");
        }

        var queryHash = OffsetCursorCodec.Hash(queryParts);
        var snapshotHash = OffsetCursorCodec.Hash(ordered.SelectMany(item => identity(item).Append("|")));
        var offset = cursor is null ? 0 : OffsetCursorCodec.Decode(cursor, queryHash, snapshotHash, ordered.Count);
        var items = ordered.Skip(offset).Take(size).ToList();
        var next = offset + items.Count;
        return new ListPage<T>(
            items,
            ordered.Count,
            offset,
            next < ordered.Count ? OffsetCursorCodec.Encode(next, queryHash, snapshotHash) : null);
    }
}
