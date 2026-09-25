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
/// Finds PLC software over the generic object model: every device from <see cref="DeviceWalker"/>
/// (ungrouped, then grouped), then every nested device item's <c>SoftwareContainer</c>. Selection
/// is exact and fails closed: a name is matched ordinally, and an omitted name requires exactly one
/// PLC in the project.
/// </summary>
public static class PlcLocator
{
    public const string PlcSoftwareType = "Siemens.Engineering.SW.PlcSoftware";
    public const string SoftwareContainerType = "Siemens.Engineering.HW.Features.SoftwareContainer";

    public static List<LocatedPlc> FindAll(IObjectNode project, List<string> diagnostics)
    {
        var found = new List<LocatedPlc>();
        foreach (var device in DeviceWalker.Devices(project, diagnostics))
        {
            foreach (var item in DeviceWalker.Items(device, diagnostics))
            {
                TryAddPlc(item.Node, item.ObjectPath, device.Name, device.GroupPath, found, diagnostics);
            }
        }

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

    private static void TryAddPlc(
        IObjectNode item,
        List<ObjectPathSegmentInfo> itemPath,
        string deviceName,
        IReadOnlyList<string> groupNames,
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
}
