using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.ObjectModel;

namespace TiaMcpServer.OpennessWorker.HardwareRead;

/// <summary>Siemens-free builders of the R2 hardware reads that the generic object model can serve.</summary>
public static class HardwareReadBuilder
{
    public static DeviceGroupTreeInfo ListDeviceGroups(IObjectNode project)
    {
        var diagnostics = new List<string>();
        var devices = DeviceWalker.Devices(project, diagnostics);
        var result = new DeviceGroupTreeInfo
        {
            UngroupedDevices = devices.Where(device => device.GroupPath.Count == 0).Select(Reference).ToList(),
            Diagnostics = diagnostics,
        };

        foreach (var (group, groupPath, objectPath) in DeviceWalker.Groups(project, diagnostics))
        {
            result.Groups.Add(new DeviceGroupInfo
            {
                Name = group.TryReadName() ?? string.Empty,
                GroupPath = groupPath,
                ObjectPath = objectPath,
                Devices = devices
                    .Where(device => device.GroupPath.SequenceEqual(groupPath, StringComparer.Ordinal))
                    .Select(Reference)
                    .ToList(),
            });
        }

        return result;
    }

    public static HwIdentifiersInfo ListHwIdentifiers(IObjectNode project, string deviceName, int? pageSize, string? cursor)
    {
        var diagnostics = new List<string>();
        var device = DeviceWalker.Select(project, deviceName, diagnostics);
        var identifiers = new List<HwIdentifierInfo>();
        AddIdentifiers(device.Node, device.ObjectPath, Array.Empty<string>(), identifiers, diagnostics);
        foreach (var item in DeviceWalker.Items(device, diagnostics))
        {
            AddIdentifiers(item.Node, item.ObjectPath, item.ItemPath, identifiers, diagnostics);
        }

        var page = ListPager.Page(
            identifiers,
            new[] { "hw-identifiers-v1", device.Name },
            identifier => identifier.OwnerPath.Concat(new[] { "|", identifier.Identifier.ToString(CultureInfo.InvariantCulture) }),
            pageSize,
            cursor);
        return new HwIdentifiersInfo
        {
            DeviceName = device.Name,
            Identifiers = page.Items.ToList(),
            TotalCount = page.TotalCount,
            Offset = page.Offset,
            NextCursor = page.NextCursor,
            Diagnostics = diagnostics,
        };
    }

    private static void AddIdentifiers(
        IObjectNode owner,
        IReadOnlyList<ObjectPathSegmentInfo> ownerPath,
        IReadOnlyList<string> ownerNames,
        List<HwIdentifierInfo> output,
        List<string> diagnostics)
    {
        var elements = DeviceWalker.Elements(owner, "HwIdentifiers", diagnostics);
        for (var index = 0; index < elements.Count; index++)
        {
            var read = elements[index].ReadValue("Identifier");
            if (!read.Succeeded || !ObjectScalarNormalizer.TryNormalize(read.Value, out var json) || json is not long value)
            {
                diagnostics.Add($"A hardware identifier of '{string.Join("/", ownerNames)}' could not be read.");
                continue;
            }

            output.Add(new HwIdentifierInfo
            {
                Identifier = value,
                OwnerPath = ownerNames.ToList(),
                ObjectPath = DeviceWalker.Append(ownerPath, "HwIdentifiers", index, null),
            });
        }
    }

    private static DeviceReferenceInfo Reference(WalkedDevice device)
    {
        var read = device.Node.ReadValue("TypeIdentifier");
        return new DeviceReferenceInfo
        {
            Name = device.Name,
            TypeIdentifier = read.Succeeded ? read.Value as string : null,
            ObjectPath = device.ObjectPath,
        };
    }
}
