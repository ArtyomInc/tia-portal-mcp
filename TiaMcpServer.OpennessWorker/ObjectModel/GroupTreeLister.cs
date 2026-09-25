using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.ObjectModel;

/// <summary>Which compositions of a group hierarchy hold items and subgroups, and what to read.</summary>
public sealed class GroupTreeSpec
{
    public GroupTreeSpec(
        IReadOnlyList<string> itemCompositions,
        IReadOnlyList<string> groupCompositions,
        IReadOnlyList<string>? attributes)
    {
        ItemCompositions = itemCompositions;
        GroupCompositions = groupCompositions;
        Attributes = attributes;
    }

    public IReadOnlyList<string> ItemCompositions { get; }
    public IReadOnlyList<string> GroupCompositions { get; }

    /// <summary>Attributes read into each item's values; null reads every declared scalar attribute.</summary>
    public IReadOnlyList<string>? Attributes { get; }
}

/// <summary>An item found in a group hierarchy, with the node kept for detail reads.</summary>
public sealed class GroupTreeItem
{
    public GroupTreeItem(DomainObjectInfo info, IObjectNode node)
    {
        Info = info;
        Node = node;
    }

    public DomainObjectInfo Info { get; }
    public IObjectNode Node { get; }
}

/// <summary>
/// Flattens a group hierarchy (block groups, type groups, table groups, …) depth-first: a group's
/// own items first, in the spec's composition order, then each subgroup in composition order.
/// Every item carries its group path and the complete R0 object path that addresses it.
/// </summary>
public static class GroupTreeLister
{
    public static List<GroupTreeItem> List(
        IObjectNode root,
        IReadOnlyList<ObjectPathSegmentInfo> rootPath,
        GroupTreeSpec spec,
        List<string> diagnostics,
        Func<IObjectNode, bool>? nameFilter = null,
        bool readValues = true)
    {
        var items = new List<GroupTreeItem>();
        Walk(root, rootPath, new List<string>(), spec, diagnostics, nameFilter, readValues, items);
        return items;
    }

    private static void Walk(
        IObjectNode group,
        IReadOnlyList<ObjectPathSegmentInfo> groupPath,
        List<string> groupNames,
        GroupTreeSpec spec,
        List<string> diagnostics,
        Func<IObjectNode, bool>? nameFilter,
        bool readValues,
        List<GroupTreeItem> items)
    {
        var declared = new HashSet<string>(
            Guard(group.GetCompositions, diagnostics, groupNames, "compositions")?.Select(c => c.Name)
                ?? Enumerable.Empty<string>(),
            StringComparer.Ordinal);

        foreach (var composition in spec.ItemCompositions.Where(declared.Contains))
        {
            var elements = Elements(group, composition, diagnostics, groupNames);
            for (var index = 0; index < elements.Count; index++)
            {
                var element = elements[index];
                if (nameFilter is not null && !nameFilter(element))
                {
                    continue;
                }

                if (items.Count == ObjectReadLimits.MaxChildrenSnapshot)
                {
                    throw new WorkerOperationException(
                        WorkerFailureCategories.SnapshotTooLarge,
                        $"The listing holds more than {ObjectReadLimits.MaxChildrenSnapshot} objects; narrow it with nameContains.");
                }

                var name = element.TryReadName();
                var unavailable = new List<string>();
                items.Add(new GroupTreeItem(
                    new DomainObjectInfo
                    {
                        Name = name ?? string.Empty,
                        Kind = ObjectPathRules.SimpleName(element.TypeName),
                        TypeName = element.TypeName,
                        GroupPath = groupNames.ToList(),
                        ObjectPath = Step(groupPath, composition, index, name),
                        Values = readValues
                            ? ObjectScalarNormalizer.ReadValues(element, spec.Attributes, unavailable)
                            : new Dictionary<string, object?>(StringComparer.Ordinal),
                        Unavailable = unavailable,
                    },
                    element));
            }
        }

        foreach (var composition in spec.GroupCompositions.Where(declared.Contains))
        {
            var groups = Elements(group, composition, diagnostics, groupNames);
            for (var index = 0; index < groups.Count; index++)
            {
                var subgroup = groups[index];
                var name = subgroup.TryReadName();
                Walk(
                    subgroup,
                    Step(groupPath, composition, index, name),
                    groupNames.Append(name ?? string.Empty).ToList(),
                    spec,
                    diagnostics,
                    nameFilter,
                    readValues,
                    items);
            }
        }
    }

    private static IReadOnlyList<IObjectNode> Elements(
        IObjectNode group,
        string composition,
        List<string> diagnostics,
        List<string> groupNames)
    {
        var elements = Guard(
            () => group.GetCompositionElements(composition, ObjectReadLimits.MaxChildrenSnapshot),
            diagnostics,
            groupNames,
            $"composition '{composition}'") ?? Array.Empty<IObjectNode>();
        if (elements.Count > ObjectReadLimits.MaxChildrenSnapshot)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.SnapshotTooLarge,
                $"Composition '{composition}' holds more than {ObjectReadLimits.MaxChildrenSnapshot} objects.");
        }

        return elements;
    }

    private static List<ObjectPathSegmentInfo> Step(
        IReadOnlyList<ObjectPathSegmentInfo> basePath,
        string composition,
        int index,
        string? name)
    {
        var path = basePath.Select(ObjectChildrenPager.Copy).ToList();
        path.Add(new ObjectPathSegmentInfo
        {
            Kind = ObjectPathSegmentKinds.Composition,
            Name = composition,
            ElementName = string.IsNullOrEmpty(name) ? null : name,
            Index = index,
        });
        return path;
    }

    private static T? Guard<T>(Func<T> read, List<string> diagnostics, List<string> groupNames, string what)
        where T : class
    {
        try
        {
            return read();
        }
        catch (WorkerOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var where = groupNames.Count == 0 ? "the root group" : $"group '{string.Join("/", groupNames)}'";
            diagnostics.Add($"Skipped {what} of {where}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
