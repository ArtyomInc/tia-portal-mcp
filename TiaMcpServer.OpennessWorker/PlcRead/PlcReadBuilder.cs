using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.ObjectModel;

namespace TiaMcpServer.OpennessWorker.PlcRead;

/// <summary>Where a PLC listing starts below the PLC software, and how its hierarchy is shaped.</summary>
public sealed class PlcListingSpec
{
    public PlcListingSpec(IReadOnlyList<ObjectPathSegmentInfo> rootSteps, GroupTreeSpec tree)
    {
        RootSteps = rootSteps;
        Tree = tree;
    }

    /// <summary>Steps from the PLC software to the listing's root group.</summary>
    public IReadOnlyList<ObjectPathSegmentInfo> RootSteps { get; }

    public GroupTreeSpec Tree { get; }
}

/// <summary>
/// Siemens-free builders of the <c>plc_read</c> results. Every listing is a
/// <see cref="GroupTreeLister"/> walk below one PLC software, so each result item carries an R0
/// object path that <c>object_read</c> can follow.
/// </summary>
public static class PlcReadBuilder
{
    private static ObjectPathSegmentInfo Attribute(string name) => new() { Kind = ObjectPathSegmentKinds.Attribute, Name = name };

    private static ObjectPathSegmentInfo Service(string name) => new() { Kind = ObjectPathSegmentKinds.Service, Name = name };

    private static readonly string[] BlockAttributes =
    {
        "Number", "ProgrammingLanguage", "SecondaryType", "EventClass", "InstanceOfName", "InstanceOfType",
        "IsConsistent", "IsKnowHowProtected", "IsWriteProtected", "MemoryLayout", "Namespace",
        "HeaderAuthor", "HeaderFamily", "HeaderName", "HeaderVersion",
        "CreationDate", "ModifiedDate", "CodeModifiedDate", "InterfaceModifiedDate", "CompileDate",
        "LoadMemoryLength", "WorkMemoryLength",
    };

    private static readonly string[] TypeAttributes =
    {
        "IsConsistent", "IsKnowHowProtected", "IsFailsafeCompliant", "LibraryConformanceStatus", "Namespace",
        "CreationDate", "ModifiedDate", "InterfaceModifiedDate",
    };

    private static readonly string[] TechnologyObjectAttributes =
    {
        "Number", "OfSystemLibElement", "OfSystemLibVersion", "InstanceOfName", "IsConsistent", "IsKnowHowProtected",
        "ProgrammingLanguage", "Namespace", "CreationDate", "ModifiedDate", "CompileDate",
    };

    /// <summary>Watch- and force-table entry attributes; each entry reports those it declares.</summary>
    public static readonly string[] TableEntryAttributes =
    {
        "Address", "DisplayFormat", "MonitorTrigger", "ModifyIntention", "ModifyTrigger", "ModifyValue",
        "ForceIntention", "ForceValue",
    };

    /// <summary>Listing definitions per <c>plc_read</c> listing operation.</summary>
    public static readonly IReadOnlyDictionary<string, PlcListingSpec> Listings =
        new Dictionary<string, PlcListingSpec>(StringComparer.Ordinal)
        {
            ["list_blocks"] = new(
                new[] { Attribute("BlockGroup") },
                new GroupTreeSpec(new[] { "Blocks" }, new[] { "Groups", "SystemBlockGroups" }, BlockAttributes)),
            ["list_types"] = new(
                new[] { Attribute("TypeGroup") },
                new GroupTreeSpec(new[] { "Types" }, new[] { "Groups", "SystemTypeGroups" }, TypeAttributes)),
            ["list_watch_tables"] = new(
                new[] { Attribute("WatchAndForceTableGroup") },
                new GroupTreeSpec(new[] { "WatchTables", "ForceTables" }, new[] { "Groups" }, new[] { "IsConsistent" })),
            ["list_technology_objects"] = new(
                new[] { Attribute("TechnologicalObjectGroup") },
                new GroupTreeSpec(new[] { "TechnologicalObjects" }, new[] { "Groups" }, TechnologyObjectAttributes)),
            ["list_external_sources"] = new(
                new[] { Attribute("ExternalSourceGroup") },
                new GroupTreeSpec(new[] { "ExternalSources" }, new[] { "Groups" }, attributes: null)),
            ["list_software_units"] = new(
                new[] { Service("PlcUnitProvider"), Attribute("UnitGroup") },
                new GroupTreeSpec(new[] { "Units", "SafetyUnits" }, Array.Empty<string>(), attributes: null)),
            ["list_alarm_text_lists"] = new(
                new[] { Attribute("PlcAlarmTextlistGroup") },
                new GroupTreeSpec(new[] { "PlcAlarmUserTextlists", "PlcAlarmSystemTextlists" }, Array.Empty<string>(), attributes: null)),
            ["list_opcua_server_interfaces"] = new(
                new[] { Service("OpcUaProvider"), Attribute("CommunicationGroup"), Attribute("ServerInterfaceGroup") },
                new GroupTreeSpec(new[] { "ServerInterfaces" }, new[] { "Groups" }, attributes: null)),
        };

    public static PlcListInfo ListPlcs(IObjectNode project)
    {
        var diagnostics = new List<string>();
        var plcs = PlcLocator.FindAll(project, diagnostics);
        return new PlcListInfo
        {
            Plcs = plcs.Select(plc => plc.Summary).ToList(),
            Diagnostics = diagnostics,
        };
    }

    public static PlcObjectListInfo List(
        IObjectNode project,
        string operation,
        string? plcName,
        string? nameContains,
        int? pageSize,
        string? cursor)
    {
        if (!Listings.TryGetValue(operation, out var listing))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{operation}' is not a plc_read listing.");
        }

        var diagnostics = new List<string>();
        var plc = PlcLocator.Select(project, plcName, diagnostics);
        var items = ListItems(plc, listing, nameContains, diagnostics, readValues: true);
        var page = ListPager.Page(
            items.Select(item => item.Info).ToList(),
            new[] { "plc-list-v1", operation, plc.Summary.Name, nameContains },
            Identity,
            pageSize,
            cursor);
        return new PlcObjectListInfo
        {
            PlcName = plc.Summary.Name,
            Items = page.Items.ToList(),
            TotalCount = page.TotalCount,
            Offset = page.Offset,
            NextCursor = page.NextCursor,
            Diagnostics = diagnostics,
        };
    }

    public static PlcObjectEntriesInfo ReadWatchTable(
        IObjectNode project,
        string? plcName,
        string name,
        IReadOnlyList<string>? groupPath,
        int? pageSize,
        string? cursor)
    {
        var diagnostics = new List<string>();
        var plc = PlcLocator.Select(project, plcName, diagnostics);
        var table = SelectOne(plc, Listings["list_watch_tables"], name, groupPath, "watch or force table", diagnostics);
        var entries = Entries(table.Node, "Entries", TableEntryAttributes, parameterNames: null, diagnostics);
        return EntriesResult(plc, table, entries, "watch-table-v1", pageSize, cursor, diagnostics);
    }

    public static PlcObjectEntriesInfo ReadTechnologyObject(
        IObjectNode project,
        string? plcName,
        string name,
        IReadOnlyList<string>? groupPath,
        IReadOnlyList<string>? parameterNames,
        int? pageSize,
        string? cursor)
    {
        var diagnostics = new List<string>();
        var plc = PlcLocator.Select(project, plcName, diagnostics);
        var technologyObject = SelectOne(plc, Listings["list_technology_objects"], name, groupPath, "technology object", diagnostics);
        var entries = Entries(technologyObject.Node, "Parameters", new[] { "Value" }, parameterNames, diagnostics);
        if (parameterNames is not null)
        {
            var found = new HashSet<string>(entries.Select(entry => entry.Name ?? string.Empty), StringComparer.Ordinal);
            diagnostics.AddRange(parameterNames
                .Where(parameter => !found.Contains(parameter))
                .Select(parameter => $"Parameter '{parameter}' does not exist on this technology object."));
        }

        return EntriesResult(
            plc,
            technologyObject,
            entries,
            "technology-object-v1|" + string.Join("|", parameterNames ?? Array.Empty<string>()),
            pageSize,
            cursor,
            diagnostics);
    }

    /// <summary>Selects exactly one object of a listing by exact name and, optionally, group path.</summary>
    public static GroupTreeItem SelectOne(
        LocatedPlc plc,
        PlcListingSpec listing,
        string name,
        IReadOnlyList<string>? groupPath,
        string what,
        List<string> diagnostics)
    {
        var matches = ListItems(plc, listing, nameContains: null, diagnostics, readValues: false, exactName: name)
            .Where(item => groupPath is null || item.Info.GroupPath.SequenceEqual(groupPath, StringComparer.Ordinal))
            .ToList();
        return matches.Count switch
        {
            0 => throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound,
                $"PLC '{plc.Summary.Name}' has no {what} named '{name}'"
                    + (groupPath is null ? "." : $" in group '{string.Join("/", groupPath)}'.")),
            1 => WithValues(matches[0], listing),
            _ => throw new WorkerOperationException(
                WorkerFailureCategories.TargetAmbiguous,
                $"PLC '{plc.Summary.Name}' has several {what}s named '{name}' (groups: "
                    + string.Join(", ", matches.Select(m => "'" + string.Join("/", m.Info.GroupPath) + "'"))
                    + "); add groupPath."),
        };
    }

    public static PlcChecksumsInfo ReadChecksums(IObjectNode project, string? plcName)
    {
        var diagnostics = new List<string>();
        var plc = PlcLocator.Select(project, plcName, diagnostics);
        IObjectNode provider;
        try
        {
            provider = ObjectPathResolver.Resolve(plc.Software, ObjectRoots.Project, new[] { Service("PlcChecksumProvider") });
        }
        catch (WorkerOperationException ex) when (ex.FailureCategory is WorkerFailureCategories.TargetNotFound)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.CapabilityUnavailable,
                $"PLC '{plc.Summary.Name}' offers no checksum provider.");
        }

        return new PlcChecksumsInfo
        {
            PlcName = plc.Summary.Name,
            Software = ReadText(provider, "Software", diagnostics),
            TextLists = ReadText(provider, "TextLists", diagnostics),
            Diagnostics = diagnostics,
        };
    }

    private static string? ReadText(IObjectNode node, string name, List<string> diagnostics)
    {
        var read = node.ReadValue(name);
        if (read.Succeeded && ObjectScalarNormalizer.TryNormalize(read.Value, out var json) && json is string text)
        {
            return text;
        }

        diagnostics.Add($"Checksum '{name}' could not be read.");
        return null;
    }

    private static List<GroupTreeItem> ListItems(
        LocatedPlc plc,
        PlcListingSpec listing,
        string? nameContains,
        List<string> diagnostics,
        bool readValues,
        string? exactName = null)
    {
        var rootPath = plc.Path.Select(ObjectChildrenPager.Copy).ToList();
        IObjectNode root;
        try
        {
            rootPath.AddRange(listing.RootSteps.Select(ObjectChildrenPager.Copy));
            root = ObjectPathResolver.Resolve(plc.Software, ObjectRoots.Project, listing.RootSteps);
        }
        catch (WorkerOperationException ex) when (ex.FailureCategory is WorkerFailureCategories.TargetNotFound)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.CapabilityUnavailable,
                $"PLC '{plc.Summary.Name}' does not offer this listing: {ex.Message}");
        }

        Func<IObjectNode, bool>? filter = null;
        if (exactName is not null)
        {
            filter = node => string.Equals(node.TryReadName(), exactName, StringComparison.Ordinal);
        }
        else if (!string.IsNullOrEmpty(nameContains))
        {
            filter = node => (node.TryReadName() ?? string.Empty).IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        return GroupTreeLister.List(root, rootPath, listing.Tree, diagnostics, filter, readValues);
    }

    private static GroupTreeItem WithValues(GroupTreeItem item, PlcListingSpec listing)
    {
        var unavailable = new List<string>();
        item.Info.Values = ObjectScalarNormalizer.ReadValues(item.Node, listing.Tree.Attributes, unavailable);
        item.Info.Unavailable = unavailable;
        return item;
    }

    private static List<DomainEntryInfo> Entries(
        IObjectNode owner,
        string composition,
        IReadOnlyList<string> attributes,
        IReadOnlyList<string>? parameterNames,
        List<string> diagnostics)
    {
        if (!owner.GetCompositions().Any(c => string.Equals(c.Name, composition, StringComparison.Ordinal)))
        {
            diagnostics.Add($"'{ObjectPathRules.SimpleName(owner.TypeName)}' declares no '{composition}' composition.");
            return new List<DomainEntryInfo>();
        }

        var elements = owner.GetCompositionElements(composition, ObjectReadLimits.MaxChildrenSnapshot);
        if (elements.Count > ObjectReadLimits.MaxChildrenSnapshot)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.SnapshotTooLarge,
                $"'{composition}' holds more than {ObjectReadLimits.MaxChildrenSnapshot} entries.");
        }

        var selected = parameterNames is null ? null : new HashSet<string>(parameterNames, StringComparer.Ordinal);
        var entries = new List<DomainEntryInfo>();
        foreach (var element in elements)
        {
            var name = element.TryReadName();
            if (selected is not null && (name is null || !selected.Contains(name)))
            {
                continue;
            }

            var unavailable = new List<string>();
            entries.Add(new DomainEntryInfo
            {
                Name = name,
                Values = ObjectScalarNormalizer.ReadValues(element, attributes, unavailable),
                Unavailable = unavailable,
            });
        }

        return entries;
    }

    private static PlcObjectEntriesInfo EntriesResult(
        LocatedPlc plc,
        GroupTreeItem target,
        List<DomainEntryInfo> entries,
        string query,
        int? pageSize,
        string? cursor,
        List<string> diagnostics)
    {
        var page = ListPager.Page(
            entries,
            new[] { query, plc.Summary.Name }.Concat(target.Info.GroupPath).Append(target.Info.Name),
            entry => new[] { entry.Name }.Concat(entry.Values.OrderBy(v => v.Key, StringComparer.Ordinal)
                .SelectMany(v => new[] { v.Key, Convert.ToString(v.Value, CultureInfo.InvariantCulture) })),
            pageSize,
            cursor);
        return new PlcObjectEntriesInfo
        {
            PlcName = plc.Summary.Name,
            Target = target.Info,
            Entries = page.Items.ToList(),
            TotalCount = page.TotalCount,
            Offset = page.Offset,
            NextCursor = page.NextCursor,
            Diagnostics = diagnostics,
        };
    }

    private static IEnumerable<string?> Identity(DomainObjectInfo item)
        => item.GroupPath.Concat(new[] { "/", item.Name, item.TypeName });
}
