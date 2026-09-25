using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.ObjectModel;

namespace TiaMcpServer.OpennessWorker.HmiRead;

/// <summary>An HMI software found in the project, with its runtime and R0 path.</summary>
public sealed class LocatedHmi
{
    public LocatedHmi(HmiSummaryInfo summary, IObjectNode software)
    {
        Summary = summary;
        Software = software;
    }

    public HmiSummaryInfo Summary { get; }
    public IObjectNode Software { get; }
}

/// <summary>Where an HMI listing starts below the HMI software, and its hierarchy shape.</summary>
public sealed class HmiListingSpec
{
    public HmiListingSpec(IReadOnlyList<string> rootAttributes, GroupTreeSpec tree)
    {
        RootAttributes = rootAttributes;
        Tree = tree;
    }

    /// <summary>Attribute steps from the HMI software to the listing root (empty: the software itself).</summary>
    public IReadOnlyList<string> RootAttributes { get; }

    public GroupTreeSpec Tree { get; }
}

/// <summary>
/// Siemens-free builders of the <c>hmi_read</c> results for WinCC Unified (<c>HmiSoftware</c>) and
/// WinCC Classic (<c>HmiTarget</c>). A listing a runtime does not expose through Openness fails
/// with <c>capability_unavailable</c> instead of returning an empty, misleading result.
/// </summary>
public static class HmiReadBuilder
{
    public const string UnifiedType = "Siemens.Engineering.HmiUnified.HmiSoftware";
    public const string ClassicType = "Siemens.Engineering.Hmi.HmiTarget";

    private static readonly string[] Empty = Array.Empty<string>();

    private static readonly string[] UnifiedTagAttributes =
    {
        "TagTableName", "DataType", "HmiDataType", "Connection", "PlcName", "PlcTag", "Address", "AccessMode",
        "AcquisitionMode", "AcquisitionCycle", "Persistent", "Scope", "TagType",
    };

    private static readonly string[] UnifiedAlarmAttributes =
    {
        "Id", "AlarmClass", "Area", "Origin", "Priority", "RaisedStateTag", "RaisedStateTagBitNumber",
        "TriggerMode", "TriggerBitAddress", "AcknowledgmentStateTag", "AcknowledgmentControlTag",
    };

    /// <summary>Listing definitions per operation and runtime; a missing entry means "not exposed".</summary>
    public static readonly IReadOnlyDictionary<(string Operation, string Runtime), HmiListingSpec> Listings =
        new Dictionary<(string, string), HmiListingSpec>
        {
            [("list_screens", HmiRuntimes.Unified)] = new(Empty, new GroupTreeSpec(new[] { "Screens" }, new[] { "ScreenGroups", "Groups" }, new[] { "ScreenNumber", "Width", "Height", "Enabled" })),
            [("list_screens", HmiRuntimes.Classic)] = new(new[] { "ScreenFolder" }, new GroupTreeSpec(new[] { "Screens" }, new[] { "Folders" }, attributes: null)),
            [("list_hmi_tags", HmiRuntimes.Unified)] = new(Empty, new GroupTreeSpec(new[] { "Tags" }, Empty, UnifiedTagAttributes)),
            [("list_hmi_tags", HmiRuntimes.Classic)] = new(new[] { "TagFolder" }, new GroupTreeSpec(new[] { "Tags" }, new[] { "Folders", "TagTables" }, attributes: null)),
            [("list_hmi_connections", HmiRuntimes.Unified)] = new(Empty, new GroupTreeSpec(new[] { "Connections" }, Empty, attributes: null)),
            [("list_hmi_connections", HmiRuntimes.Classic)] = new(Empty, new GroupTreeSpec(new[] { "Connections" }, Empty, attributes: null)),
            [("list_hmi_alarms", HmiRuntimes.Unified)] = new(Empty, new GroupTreeSpec(new[] { "DiscreteAlarms", "AnalogAlarms" }, Empty, UnifiedAlarmAttributes)),
            [("list_hmi_logs", HmiRuntimes.Unified)] = new(Empty, new GroupTreeSpec(new[] { "DataLogs", "AlarmLogs" }, Empty, attributes: null)),
            [("list_hmi_text_lists", HmiRuntimes.Unified)] = new(Empty, new GroupTreeSpec(new[] { "HmiTextLists", "HmiGraphicLists" }, Empty, attributes: null)),
            [("list_hmi_text_lists", HmiRuntimes.Classic)] = new(Empty, new GroupTreeSpec(new[] { "TextLists", "GraphicLists" }, Empty, attributes: null)),
            [("list_hmi_scripts", HmiRuntimes.Unified)] = new(Empty, new GroupTreeSpec(new[] { "Scripts" }, Empty, attributes: null)),
            [("list_hmi_scripts", HmiRuntimes.Classic)] = new(new[] { "VBScriptFolder" }, new GroupTreeSpec(new[] { "VBScripts" }, new[] { "Folders" }, attributes: null)),
        };

    public static readonly IReadOnlyList<string> ListingOperations =
        Listings.Keys.Select(key => key.Operation).Distinct(StringComparer.Ordinal).ToArray();

    public static List<LocatedHmi> FindAll(IObjectNode project, List<string> diagnostics)
    {
        var found = new List<LocatedHmi>();
        foreach (var device in DeviceWalker.Devices(project, diagnostics))
        {
            foreach (var item in DeviceWalker.Items(device, diagnostics))
            {
                try
                {
                    if (!item.Node.GetServices().Any(s => string.Equals(ObjectPathRules.SimpleName(s.Name), "SoftwareContainer", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    var containerType = item.Node.GetServices()
                        .First(s => string.Equals(ObjectPathRules.SimpleName(s.Name), "SoftwareContainer", StringComparison.Ordinal)).Name;
                    var software = item.Node.GetService(containerType)?.FollowAttribute("Software").Node;
                    var runtime = software?.TypeName switch
                    {
                        UnifiedType => HmiRuntimes.Unified,
                        ClassicType => HmiRuntimes.Classic,
                        _ => null,
                    };
                    if (software is null || runtime is null)
                    {
                        continue;
                    }

                    var path = item.ObjectPath.Select(ObjectChildrenPager.Copy).ToList();
                    path.Add(new ObjectPathSegmentInfo { Kind = ObjectPathSegmentKinds.Service, Name = "SoftwareContainer" });
                    path.Add(new ObjectPathSegmentInfo { Kind = ObjectPathSegmentKinds.Attribute, Name = "Software" });
                    found.Add(new LocatedHmi(
                        new HmiSummaryInfo
                        {
                            Name = software.TryReadName() ?? string.Empty,
                            Runtime = runtime,
                            DeviceName = device.Name,
                            DeviceGroupPath = device.GroupPath.ToList(),
                            ObjectPath = path,
                        },
                        software));
                }
                catch (Exception ex) when (ex is not WorkerOperationException)
                {
                    diagnostics.Add($"Skipped a device item of '{device.Name}' while locating HMIs: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        return found;
    }

    public static LocatedHmi Select(IObjectNode project, string? hmiName, List<string> diagnostics)
    {
        var all = FindAll(project, diagnostics);
        var matches = hmiName is null
            ? all
            : all.Where(hmi => string.Equals(hmi.Summary.Name, hmiName, StringComparison.Ordinal)).ToList();
        if (matches.Count == 1)
        {
            return matches[0];
        }

        var available = all.Count == 0 ? "none" : string.Join(", ", all.Select(hmi => $"'{hmi.Summary.Name}'"));
        throw matches.Count == 0
            ? new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound,
                hmiName is null ? "The project contains no HMI software." : $"No HMI software is named '{hmiName}'. HMIs in the project: {available}.")
            : new WorkerOperationException(
                WorkerFailureCategories.TargetAmbiguous,
                hmiName is null ? $"The project contains several HMIs ({available}); specify hmiName." : $"Several HMI software objects are named '{hmiName}'.");
    }

    public static HmiListInfo ListHmis(IObjectNode project)
    {
        var diagnostics = new List<string>();
        return new HmiListInfo { Hmis = FindAll(project, diagnostics).Select(h => h.Summary).ToList(), Diagnostics = diagnostics };
    }

    public static HmiObjectListInfo List(
        IObjectNode project,
        string operation,
        string? hmiName,
        string? nameContains,
        int? pageSize,
        string? cursor)
    {
        var diagnostics = new List<string>();
        var hmi = Select(project, hmiName, diagnostics);
        var items = Items(hmi, operation, nameContains, null, diagnostics, readValues: true);
        var page = ListPager.Page(
            items.Select(item => item.Info).ToList(),
            new[] { "hmi-list-v1", operation, hmi.Summary.Name, nameContains },
            item => item.GroupPath.Concat(new[] { "/", item.Name, item.TypeName }),
            pageSize,
            cursor);
        return new HmiObjectListInfo
        {
            HmiName = hmi.Summary.Name,
            Runtime = hmi.Summary.Runtime,
            Items = page.Items.ToList(),
            TotalCount = page.TotalCount,
            Offset = page.Offset,
            NextCursor = page.NextCursor,
            Diagnostics = diagnostics,
        };
    }

    public static HmiObjectListInfo ListScreenItems(
        IObjectNode project,
        string? hmiName,
        string screenName,
        IReadOnlyList<string>? groupPath,
        int? pageSize,
        string? cursor)
    {
        var diagnostics = new List<string>();
        var hmi = Select(project, hmiName, diagnostics);
        if (hmi.Summary.Runtime != HmiRuntimes.Unified)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.CapabilityUnavailable,
                "WinCC Classic screen items are not exposed by Openness; export the screen with object_read export_object.");
        }

        var screen = SelectScreen(hmi, screenName, groupPath, diagnostics);
        var items = GroupTreeLister.List(screen.Node, screen.Info.ObjectPath, new GroupTreeSpec(new[] { "ScreenItems" }, Empty, attributes: null), diagnostics);
        var page = ListPager.Page(
            items.Select(item => item.Info).ToList(),
            new[] { "hmi-screen-items-v1", hmi.Summary.Name }.Concat(screen.Info.GroupPath).Append(screen.Info.Name),
            item => new[] { item.Name, item.TypeName },
            pageSize,
            cursor);
        return new HmiObjectListInfo
        {
            HmiName = hmi.Summary.Name,
            Runtime = hmi.Summary.Runtime,
            Target = screen.Info,
            Items = page.Items.ToList(),
            TotalCount = page.TotalCount,
            Offset = page.Offset,
            NextCursor = page.NextCursor,
            Diagnostics = diagnostics,
        };
    }

    public static HmiScreenScriptsInfo ReadScreenScripts(
        IObjectNode project,
        string? hmiName,
        string screenName,
        IReadOnlyList<string>? groupPath,
        int? pageSize,
        string? cursor)
    {
        var diagnostics = new List<string>();
        var hmi = Select(project, hmiName, diagnostics);
        if (hmi.Summary.Runtime != HmiRuntimes.Unified)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.CapabilityUnavailable,
                "Screen scripts are read for WinCC Unified only; WinCC Classic scripts are VB scripts (list_hmi_scripts).");
        }

        var screen = SelectScreen(hmi, screenName, groupPath, diagnostics);
        var scripts = new List<HmiScriptInfo>();
        CollectScripts(screen.Node, screen.Info.Name, screen.Info.ObjectPath, scripts, diagnostics);
        var items = DeviceWalker.Elements(screen.Node, "ScreenItems", diagnostics);
        for (var index = 0; index < items.Count; index++)
        {
            var name = items[index].TryReadName() ?? string.Empty;
            CollectScripts(items[index], name, DeviceWalker.Append(screen.Info.ObjectPath, "ScreenItems", index, name), scripts, diagnostics);
        }

        var page = ListPager.Page(
            scripts,
            new[] { "hmi-screen-scripts-v1", hmi.Summary.Name }.Concat(screen.Info.GroupPath).Append(screen.Info.Name),
            script => new[] { script.Owner, script.Kind, script.Trigger, script.ScriptCode },
            pageSize,
            cursor);
        return new HmiScreenScriptsInfo
        {
            HmiName = hmi.Summary.Name,
            Screen = screen.Info,
            Scripts = page.Items.ToList(),
            TotalCount = page.TotalCount,
            Offset = page.Offset,
            NextCursor = page.NextCursor,
            Diagnostics = diagnostics,
        };
    }

    private static GroupTreeItem SelectScreen(LocatedHmi hmi, string screenName, IReadOnlyList<string>? groupPath, List<string> diagnostics)
    {
        var matches = Items(hmi, "list_screens", null, screenName, diagnostics, readValues: true)
            .Where(item => groupPath is null || item.Info.GroupPath.SequenceEqual(groupPath, StringComparer.Ordinal))
            .ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"HMI '{hmi.Summary.Name}' has no screen named '{screenName}'."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"HMI '{hmi.Summary.Name}' has several screens named '{screenName}'; add groupPath."),
        };
    }

    private static List<GroupTreeItem> Items(
        LocatedHmi hmi,
        string operation,
        string? nameContains,
        string? exactName,
        List<string> diagnostics,
        bool readValues)
    {
        if (!Listings.TryGetValue((operation, hmi.Summary.Runtime), out var listing))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.CapabilityUnavailable,
                $"'{operation}' is not exposed by Openness for {hmi.Summary.Runtime} HMIs.");
        }

        var root = hmi.Software;
        var rootPath = hmi.Summary.ObjectPath.Select(ObjectChildrenPager.Copy).ToList();
        foreach (var attribute in listing.RootAttributes)
        {
            root = root.FollowAttribute(attribute).Node
                ?? throw new WorkerOperationException(
                    WorkerFailureCategories.CapabilityUnavailable,
                    $"HMI '{hmi.Summary.Name}' exposes no '{attribute}'.");
            rootPath.Add(new ObjectPathSegmentInfo { Kind = ObjectPathSegmentKinds.Attribute, Name = attribute });
        }

        Func<IObjectNode, bool>? filter = exactName is not null
            ? node => string.Equals(node.TryReadName(), exactName, StringComparison.Ordinal)
            : string.IsNullOrEmpty(nameContains)
                ? null
                : node => (node.TryReadName() ?? string.Empty).IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
        return GroupTreeLister.List(root, rootPath, listing.Tree, diagnostics, filter, readValues);
    }

    private static void CollectScripts(
        IObjectNode owner,
        string ownerName,
        List<ObjectPathSegmentInfo> ownerPath,
        List<HmiScriptInfo> scripts,
        List<string> diagnostics)
    {
        foreach (var (composition, kind, triggerAttribute) in new[]
        {
            ("EventHandlers", "event", "EventType"),
            ("PropertyEventHandlers", "propertyEvent", "PropertyName"),
            ("Dynamizations", "dynamization", "PropertyName"),
        })
        {
            var handlers = DeviceWalker.Elements(owner, composition, diagnostics);
            for (var index = 0; index < handlers.Count; index++)
            {
                var handler = handlers[index];
                // Event handlers keep their code on a Script object; script dynamizations carry it directly.
                var script = handler.ReadObject("Script") ?? handler;
                var code = Text(script, "ScriptCode");
                if (code is null && kind == "dynamization")
                {
                    continue;
                }

                scripts.Add(new HmiScriptInfo
                {
                    Owner = ownerName,
                    Kind = kind,
                    Trigger = Text(handler, triggerAttribute),
                    ScriptCode = code,
                    GlobalDefinitionAreaScriptCode = Text(script, "GlobalDefinitionAreaScriptCode"),
                    ObjectPath = DeviceWalker.Append(ownerPath, composition, index, null),
                });
            }
        }
    }

    private static string? Text(IObjectNode node, string name)
    {
        var read = node.ReadValue(name);
        return read.Succeeded && ObjectScalarNormalizer.TryNormalize(read.Value, out var json) ? json as string : null;
    }
}
