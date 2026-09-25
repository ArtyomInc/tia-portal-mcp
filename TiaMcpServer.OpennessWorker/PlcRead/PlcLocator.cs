using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.ObjectModel;

namespace TiaMcpServer.OpennessWorker.PlcRead;

/// <summary>A PLC software found in the project, with the R0 path that addresses it.</summary>
public sealed class LocatedPlc
{
    public LocatedPlc(PlcSummaryInfo summary, IObjectNode software)
    {
        Summary = summary;
        Software = software;
    }

    public PlcSummaryInfo Summary { get; }
    public IObjectNode Software { get; }
    public IReadOnlyList<ObjectPathSegmentInfo> Path => Summary.ObjectPath;
}

/// <summary>
/// Finds PLC software over the generic object model: project <c>Devices</c>, then every nested
/// <c>DeviceGroups</c>/<c>Groups</c> device, then every nested device item's
/// <c>SoftwareContainer</c>. Selection is exact and fails closed: a name is matched ordinally,
/// and an omitted name requires exactly one PLC in the project.
/// </summary>
public static class PlcLocator
{
    public const string PlcSoftwareType = "Siemens.Engineering.SW.PlcSoftware";
    public const string SoftwareContainerType = "Siemens.Engineering.HW.Features.SoftwareContainer";
    private const int MaxDeviceItemDepth = 16;

    public static List<LocatedPlc> FindAll(IObjectNode project, List<string> diagnostics)
    {
        var found = new List<LocatedPlc>();
        WalkDevices(project, new List<ObjectPathSegmentInfo>(), new List<string>(), "Devices", found, diagnostics);
        WalkGroups(project, new List<ObjectPathSegmentInfo>(), new List<string>(), "DeviceGroups", found, diagnostics);
        return found;
    }

    public static LocatedPlc Select(IObjectNode project, string? plcName, List<string> diagnostics)
    {
        var all = FindAll(project, diagnostics);
        var matches = plcName is null
            ? all
            : all.Where(plc => string.Equals(plc.Summary.Name, plcName, StringComparison.Ordinal)).ToList();

        if (matches.Count == 1)
        {
            return matches[0];
        }

        var available = all.Count == 0 ? "none" : string.Join(", ", all.Select(plc => $"'{plc.Summary.Name}'"));
        if (matches.Count == 0)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound,
                plcName is null
                    ? "The project contains no PLC software."
                    : $"No PLC software is named '{plcName}'. PLCs in the project: {available}.");
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.TargetAmbiguous,
            plcName is null
                ? $"The project contains several PLCs ({available}); specify plcName."
                : $"Several PLC software objects are named '{plcName}'; the name does not identify one PLC.");
    }

    private static void WalkGroups(
        IObjectNode owner,
        List<ObjectPathSegmentInfo> ownerPath,
        List<string> groupNames,
        string composition,
        List<LocatedPlc> found,
        List<string> diagnostics)
    {
        var groups = Elements(owner, composition, diagnostics);
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var name = group.TryReadName();
            var path = Append(ownerPath, composition, index, name);
            var names = groupNames.Append(name ?? string.Empty).ToList();
            WalkDevices(group, path, names, "Devices", found, diagnostics);
            WalkGroups(group, path, names, "Groups", found, diagnostics);
        }
    }

    private static void WalkDevices(
        IObjectNode owner,
        List<ObjectPathSegmentInfo> ownerPath,
        List<string> groupNames,
        string composition,
        List<LocatedPlc> found,
        List<string> diagnostics)
    {
        var devices = Elements(owner, composition, diagnostics);
        for (var index = 0; index < devices.Count; index++)
        {
            var device = devices[index];
            var deviceName = device.TryReadName() ?? string.Empty;
            WalkItems(device, Append(ownerPath, composition, index, deviceName), deviceName, groupNames, 0, found, diagnostics);
        }
    }

    private static void WalkItems(
        IObjectNode owner,
        List<ObjectPathSegmentInfo> ownerPath,
        string deviceName,
        List<string> groupNames,
        int depth,
        List<LocatedPlc> found,
        List<string> diagnostics)
    {
        if (depth == MaxDeviceItemDepth)
        {
            return;
        }

        var items = Elements(owner, "DeviceItems", diagnostics);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var itemPath = Append(ownerPath, "DeviceItems", index, item.TryReadName());
            TryAddPlc(item, itemPath, deviceName, groupNames, found, diagnostics);
            WalkItems(item, itemPath, deviceName, groupNames, depth + 1, found, diagnostics);
        }
    }

    private static void TryAddPlc(
        IObjectNode item,
        List<ObjectPathSegmentInfo> itemPath,
        string deviceName,
        List<string> groupNames,
        List<LocatedPlc> found,
        List<string> diagnostics)
    {
        try
        {
            if (!item.GetServices().Any(service => string.Equals(service.Name, SoftwareContainerType, StringComparison.Ordinal)))
            {
                return;
            }

            var software = item.GetService(SoftwareContainerType)?.FollowAttribute("Software").Node;
            if (software is null || !string.Equals(software.TypeName, PlcSoftwareType, StringComparison.Ordinal))
            {
                return;
            }

            var path = itemPath.Select(ObjectChildrenPager.Copy).ToList();
            path.Add(new ObjectPathSegmentInfo { Kind = ObjectPathSegmentKinds.Service, Name = "SoftwareContainer" });
            path.Add(new ObjectPathSegmentInfo { Kind = ObjectPathSegmentKinds.Attribute, Name = "Software" });
            found.Add(new LocatedPlc(
                new PlcSummaryInfo
                {
                    Name = software.TryReadName() ?? string.Empty,
                    DeviceName = deviceName,
                    DeviceGroupPath = groupNames.ToList(),
                    ObjectPath = path,
                },
                software));
        }
        catch (WorkerOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Skipped a device item of '{deviceName}' while locating PLCs: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IReadOnlyList<IObjectNode> Elements(IObjectNode owner, string composition, List<string> diagnostics)
    {
        try
        {
            return owner.GetCompositions().Any(c => string.Equals(c.Name, composition, StringComparison.Ordinal))
                ? owner.GetCompositionElements(composition, ObjectReadLimits.MaxChildrenSnapshot)
                : Array.Empty<IObjectNode>();
        }
        catch (Exception ex) when (ex is not WorkerOperationException)
        {
            diagnostics.Add($"Skipped composition '{composition}' while locating PLCs: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<IObjectNode>();
        }
    }

    private static List<ObjectPathSegmentInfo> Append(
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
}
