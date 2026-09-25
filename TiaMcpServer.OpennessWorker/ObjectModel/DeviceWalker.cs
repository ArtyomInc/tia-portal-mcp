using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.ObjectModel;

/// <summary>A device found in the project, with its group chain and R0 path.</summary>
public sealed class WalkedDevice
{
    public WalkedDevice(IObjectNode node, string name, IReadOnlyList<string> groupPath, List<ObjectPathSegmentInfo> objectPath)
    {
        Node = node;
        Name = name;
        GroupPath = groupPath;
        ObjectPath = objectPath;
    }

    public IObjectNode Node { get; }
    public string Name { get; }
    public IReadOnlyList<string> GroupPath { get; }
    public List<ObjectPathSegmentInfo> ObjectPath { get; }
}

/// <summary>A device item found below a device, with its name chain and R0 path.</summary>
public sealed class WalkedDeviceItem
{
    public WalkedDeviceItem(IObjectNode node, IReadOnlyList<string> itemPath, List<ObjectPathSegmentInfo> objectPath)
    {
        Node = node;
        ItemPath = itemPath;
        ObjectPath = objectPath;
    }

    public IObjectNode Node { get; }

    /// <summary>Device-item names from the device's first level down to this item.</summary>
    public IReadOnlyList<string> ItemPath { get; }

    public List<ObjectPathSegmentInfo> ObjectPath { get; }
}

/// <summary>
/// Enumerates the project's devices in the same order as the worker's project enumeration:
/// ungrouped <c>Devices</c> first, then a depth-first walk of <c>DeviceGroups</c> and their
/// <c>Groups</c>. Each result carries the R0 object path that addresses it.
/// </summary>
public static class DeviceWalker
{
    public const int MaxDeviceItemDepth = 16;

    public static List<WalkedDevice> Devices(IObjectNode project, List<string> diagnostics)
    {
        var devices = new List<WalkedDevice>();
        AddDevices(project, new List<ObjectPathSegmentInfo>(), new List<string>(), devices, diagnostics);
        WalkGroups(project, new List<ObjectPathSegmentInfo>(), new List<string>(), "DeviceGroups", devices, diagnostics, null);
        return devices;
    }

    /// <summary>Every device group depth-first, as (group node, group-name chain, path).</summary>
    public static List<(IObjectNode Group, List<string> GroupPath, List<ObjectPathSegmentInfo> ObjectPath)> Groups(
        IObjectNode project,
        List<string> diagnostics)
    {
        var groups = new List<(IObjectNode, List<string>, List<ObjectPathSegmentInfo>)>();
        WalkGroups(project, new List<ObjectPathSegmentInfo>(), new List<string>(), "DeviceGroups", new List<WalkedDevice>(), diagnostics, groups);
        return groups;
    }

    /// <summary>Selects exactly one device by name (ordinal, case-insensitive).</summary>
    public static WalkedDevice Select(IObjectNode project, string deviceName, List<string> diagnostics)
    {
        var matches = Devices(project, diagnostics)
            .Where(device => string.Equals(device.Name, deviceName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count switch
        {
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No device is named '{deviceName}'."),
            1 => matches[0],
            _ => throw new WorkerOperationException(
                WorkerFailureCategories.TargetAmbiguous,
                $"Several devices are named '{deviceName}' (ignoring case); the name does not identify one device."),
        };
    }

    /// <summary>Every device item below a device, depth-first, up to <see cref="MaxDeviceItemDepth"/> levels.</summary>
    public static List<WalkedDeviceItem> Items(WalkedDevice device, List<string> diagnostics)
    {
        var items = new List<WalkedDeviceItem>();
        WalkItems(device.Node, device.ObjectPath, new List<string>(), 0, items, diagnostics);
        return items;
    }

    public static List<ObjectPathSegmentInfo> Append(
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

    public static IReadOnlyList<IObjectNode> Elements(IObjectNode owner, string composition, List<string> diagnostics)
    {
        try
        {
            if (!owner.GetCompositions().Any(c => string.Equals(c.Name, composition, StringComparison.Ordinal)))
            {
                return Array.Empty<IObjectNode>();
            }

            var elements = owner.GetCompositionElements(composition, ObjectReadLimits.MaxChildrenSnapshot);
            if (elements.Count > ObjectReadLimits.MaxChildrenSnapshot)
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.SnapshotTooLarge,
                    $"Composition '{composition}' holds more than {ObjectReadLimits.MaxChildrenSnapshot} objects.");
            }

            return elements;
        }
        catch (Exception ex) when (ex is not WorkerOperationException)
        {
            diagnostics.Add($"Skipped composition '{composition}' of '{owner.TryReadName() ?? ObjectPathRules.SimpleName(owner.TypeName)}': {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<IObjectNode>();
        }
    }

    private static void AddDevices(
        IObjectNode owner,
        List<ObjectPathSegmentInfo> ownerPath,
        List<string> groupNames,
        List<WalkedDevice> devices,
        List<string> diagnostics)
    {
        var elements = Elements(owner, "Devices", diagnostics);
        for (var index = 0; index < elements.Count; index++)
        {
            var name = elements[index].TryReadName() ?? string.Empty;
            devices.Add(new WalkedDevice(elements[index], name, groupNames.ToList(), Append(ownerPath, "Devices", index, name)));
        }
    }

    private static void WalkGroups(
        IObjectNode owner,
        List<ObjectPathSegmentInfo> ownerPath,
        List<string> groupNames,
        string composition,
        List<WalkedDevice> devices,
        List<string> diagnostics,
        List<(IObjectNode, List<string>, List<ObjectPathSegmentInfo>)>? groups)
    {
        var elements = Elements(owner, composition, diagnostics);
        for (var index = 0; index < elements.Count; index++)
        {
            var group = elements[index];
            var name = group.TryReadName();
            var path = Append(ownerPath, composition, index, name);
            var names = groupNames.Append(name ?? string.Empty).ToList();
            groups?.Add((group, names, path));
            AddDevices(group, path, names, devices, diagnostics);
            WalkGroups(group, path, names, "Groups", devices, diagnostics, groups);
        }
    }

    private static void WalkItems(
        IObjectNode owner,
        List<ObjectPathSegmentInfo> ownerPath,
        List<string> itemNames,
        int depth,
        List<WalkedDeviceItem> items,
        List<string> diagnostics)
    {
        if (depth == MaxDeviceItemDepth)
        {
            return;
        }

        var elements = Elements(owner, "DeviceItems", diagnostics);
        for (var index = 0; index < elements.Count; index++)
        {
            var item = elements[index];
            var name = item.TryReadName();
            var path = Append(ownerPath, "DeviceItems", index, name);
            var names = itemNames.Append(name ?? string.Empty).ToList();
            items.Add(new WalkedDeviceItem(item, names, path));
            WalkItems(item, path, names, depth + 1, items, diagnostics);
        }
    }
}
