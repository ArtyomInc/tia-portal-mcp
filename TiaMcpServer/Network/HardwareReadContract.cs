using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.DomainReads;

namespace TiaMcpServer.Network;

/// <summary>
/// Validators for the R2 hardware-read payloads decoded by <see cref="NetworkPayloadContract"/>.
/// Explicit JSON nulls in non-nullable members, malformed object paths, and inconsistent page
/// positions reject the payload as <c>protocol_error</c>.
/// </summary>
internal static class HardwareReadContract
{
    public static void ValidateDeviceGroups(DeviceGroupTreeInfo value)
    {
        Require(value.UngroupedDevices, "ungroupedDevices");
        Require(value.Groups, "groups");
        Require(value.Diagnostics, "diagnostics");
        value.UngroupedDevices.ForEach(device => ValidateDevice(device, "ungroupedDevices[]"));
        foreach (var group in value.Groups)
        {
            Require(group, "groups[]");
            Require(group.Name, "groups[].name");
            Require(group.GroupPath, "groups[].groupPath");
            Require(group.Devices, "groups[].devices");
            ValidatePath(group.ObjectPath, "groups[].objectPath");
            if (group.GroupPath.Count == 0)
            {
                throw new JsonException("A device group must have a group path.");
            }

            group.Devices.ForEach(device => ValidateDevice(device, "groups[].devices[]"));
        }
    }

    public static void ValidateUnplugged(UnpluggedItemsInfo value)
    {
        Require(value.Devices, "devices");
        Require(value.Diagnostics, "diagnostics");
        foreach (var device in value.Devices)
        {
            Require(device?.DeviceName, "devices[].deviceName");
            Require(device!.Items, "devices[].items");
            device.Items.ForEach(item => Require(item, "devices[].items[]"));
        }
    }

    public static void ValidateHwIdentifiers(HwIdentifiersInfo value)
    {
        Require(value.DeviceName, "deviceName");
        Require(value.Identifiers, "identifiers");
        Require(value.Diagnostics, "diagnostics");
        ValidatePage(value.TotalCount, value.Offset, value.Identifiers.Count, value.NextCursor);
        foreach (var identifier in value.Identifiers)
        {
            Require(identifier, "identifiers[]");
            Require(identifier.OwnerPath, "identifiers[].ownerPath");
            ValidatePath(identifier.ObjectPath, "identifiers[].objectPath");
        }
    }

    public static void ValidatePortTopology(PortTopologyInfo value)
    {
        Require(value.Ports, "ports");
        Require(value.Diagnostics, "diagnostics");
        foreach (var port in value.Ports)
        {
            Require(port, "ports[]");
            Require(port.DeviceName, "ports[].deviceName");
            Require(port.ItemPath, "ports[].itemPath");
            Require(port.Values, "ports[].values");
            Require(port.Partners, "ports[].partners");
            ValidatePath(port.ObjectPath, "ports[].objectPath");
            if (!port.Values.Values.All(DomainPayloadProjector.IsListingValue))
            {
                throw new JsonException("Port values must be scalars, colors, texts, or arrays of those.");
            }

            port.Partners.ForEach(partner => Require(partner?.ItemPath, "ports[].partners[].itemPath"));
        }
    }

    public static void ValidateCompare(HardwareCompareInfo value)
    {
        Require(value.LeftDeviceName, "leftDeviceName");
        Require(value.RightDeviceName, "rightDeviceName");
        Require(value.Elements, "elements");
        ValidatePage(value.TotalCount, value.Offset, value.Elements.Count, value.NextCursor);
        foreach (var element in value.Elements)
        {
            Require(element, "elements[]");
            Require(element.Path, "elements[].path");
            if (string.IsNullOrEmpty(element.State) || element.Depth != element.Path.Count)
            {
                throw new JsonException("A comparison element is inconsistent.");
            }
        }
    }

    private static void ValidateDevice(DeviceReferenceInfo? device, string member)
    {
        Require(device, member);
        Require(device!.Name, member + ".name");
        ValidatePath(device.ObjectPath, member + ".objectPath");
    }

    private static void ValidatePath(List<ObjectPathSegmentInfo>? path, string member)
    {
        Require(path, member);
        if (path!.Count == 0 || path.Any(segment => segment is null || !ObjectPathSegmentKinds.IsKnown(segment.Kind) || string.IsNullOrEmpty(segment.Name)))
        {
            throw new JsonException($"'{member}' is not a valid object path.");
        }
    }

    private static void ValidatePage(int totalCount, int offset, int count, string? nextCursor)
    {
        if (totalCount < 0 || offset < 0 || offset + count > totalCount || (nextCursor is null) != (offset + count >= totalCount))
        {
            throw new JsonException("The page position is inconsistent.");
        }
    }

    private static void Require(object? value, string member) => DomainPayloadProjector.RequireNotNull(value, member);
}
