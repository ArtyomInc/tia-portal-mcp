using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.ObjectModel;

/// <summary>
/// Builds one page of <c>list_object_children</c>: compositions in ordinal name order, elements in
/// enumeration order, each child carrying the complete path that addresses it.
/// </summary>
public static class ObjectChildrenPager
{
    private const string OrderingVersion = "object-children-v1";

    public static ObjectChildrenPageInfo Build(
        IObjectNode node,
        string? root,
        IReadOnlyList<ObjectPathSegmentInfo>? path,
        IReadOnlyList<string>? compositionNames,
        int? pageSize,
        string? cursor)
    {
        var effectiveRoot = root ?? ObjectRoots.Project;
        var basePath = path ?? Array.Empty<ObjectPathSegmentInfo>();
        var size = pageSize ?? ObjectReadLimits.DefaultPageSize;
        if (size < 1 || size > ObjectReadLimits.MaxPageSize)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"ObjectPageSize must be between 1 and {ObjectReadLimits.MaxPageSize}.");
        }

        var declared = node.GetCompositions()
            .Where(c => !ObjectPathRules.IsCompositionHidden(node, c.Name))
            .Select(c => c.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var selected = SelectCompositions(declared, compositionNames, node);
        var children = new List<ObjectChildInfo>();
        var diagnostics = new List<string>();
        foreach (var composition in selected)
        {
            var remaining = ObjectReadLimits.MaxChildrenSnapshot - children.Count;
            IReadOnlyList<IObjectNode> elements;
            try
            {
                elements = node.GetCompositionElements(composition, remaining);
            }
            catch (WorkerOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Composition '{composition}' could not be enumerated: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (elements.Count > remaining)
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.SnapshotTooLarge,
                    $"The selected compositions hold more than {ObjectReadLimits.MaxChildrenSnapshot} children. "
                        + "Restrict compositionNames or descend one level.");
            }

            for (var index = 0; index < elements.Count; index++)
            {
                var element = elements[index];
                var name = element.TryReadName();
                children.Add(new ObjectChildInfo
                {
                    Composition = composition,
                    Index = index,
                    Name = name,
                    TypeName = element.TypeName,
                    ObjectPath = Append(basePath, composition, index, name),
                });
            }
        }

        var queryHash = OffsetCursorCodec.Hash(QueryParts(effectiveRoot, basePath, compositionNames));
        var snapshotHash = OffsetCursorCodec.Hash(children.SelectMany(child => new[]
        {
            child.Composition,
            child.Index.ToString(CultureInfo.InvariantCulture),
            child.Name,
            child.TypeName,
        }));
        var offset = cursor is null
            ? 0
            : OffsetCursorCodec.Decode(cursor, queryHash, snapshotHash, children.Count);
        var page = children.Skip(offset).Take(size).ToList();
        var next = offset + page.Count;
        return new ObjectChildrenPageInfo
        {
            Root = effectiveRoot,
            ObjectPath = basePath.Select(Copy).ToList(),
            Children = page,
            TotalCount = children.Count,
            Offset = offset,
            NextCursor = next < children.Count ? OffsetCursorCodec.Encode(next, queryHash, snapshotHash) : null,
            Diagnostics = diagnostics,
        };
    }

    private static List<string> SelectCompositions(
        IReadOnlyList<string> declared,
        IReadOnlyList<string>? requested,
        IObjectNode node)
    {
        if (requested is null)
        {
            return declared.ToList();
        }

        if (requested.Count == 0 || requested.Count > ObjectReadLimits.MaxCompositionNames
            || requested.Any(string.IsNullOrWhiteSpace)
            || requested.Distinct(StringComparer.Ordinal).Count() != requested.Count)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"ObjectCompositionNames must contain between 1 and {ObjectReadLimits.MaxCompositionNames} unique, nonblank names when supplied.");
        }

        foreach (var name in requested)
        {
            if (ObjectPathRules.IsCompositionHidden(node, name))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.AccessDenied,
                    $"The Portal's '{name}' composition is not readable; use root 'project' for the bound project.");
            }

            if (!declared.Contains(name, StringComparer.Ordinal))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.TargetNotFound,
                    $"'{ObjectPathRules.SimpleName(node.TypeName)}' declares no composition '{name}'.");
            }
        }

        return declared.Where(name => requested.Contains(name, StringComparer.Ordinal)).ToList();
    }

    private static IEnumerable<string?> QueryParts(
        string root,
        IReadOnlyList<ObjectPathSegmentInfo> path,
        IReadOnlyList<string>? compositionNames)
    {
        yield return OrderingVersion;
        yield return root;
        foreach (var segment in path)
        {
            yield return segment.Kind;
            yield return segment.Name;
            yield return segment.ElementName;
            yield return segment.Index?.ToString(CultureInfo.InvariantCulture);
        }

        yield return compositionNames is null ? null : "filter";
        foreach (var name in (compositionNames ?? Array.Empty<string>()).OrderBy(n => n, StringComparer.Ordinal))
        {
            yield return name;
        }
    }

    private static List<ObjectPathSegmentInfo> Append(
        IReadOnlyList<ObjectPathSegmentInfo> basePath,
        string composition,
        int index,
        string? name)
    {
        var result = basePath.Select(Copy).ToList();
        result.Add(new ObjectPathSegmentInfo
        {
            Kind = ObjectPathSegmentKinds.Composition,
            Name = composition,
            ElementName = string.IsNullOrEmpty(name) ? null : name,
            Index = index,
        });
        return result;
    }

    internal static ObjectPathSegmentInfo Copy(ObjectPathSegmentInfo segment) => new ObjectPathSegmentInfo
    {
        Kind = segment.Kind,
        Name = segment.Name,
        ElementName = segment.ElementName,
        Index = segment.Index,
    };
}
