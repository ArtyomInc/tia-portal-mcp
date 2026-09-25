using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.ObjectModel;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Siemens-facing R2 hardware reads that need typed Openness members: unplugged items (a CLR
/// collection, not a composition), port partners (an association of <see cref="NetworkPort"/>),
/// and offline hardware comparison (<see cref="HardwareObject.CompareTo"/>). Devices and device
/// items are still located through the shared <see cref="DeviceWalker"/>.
/// </summary>
internal static class HardwareReadService
{
    public static UnpluggedItemsInfo ListUnpluggedItems(Project project, string? deviceName)
    {
        var diagnostics = new List<string>();
        var root = new EngineeringObjectNode(project);
        var devices = deviceName is null
            ? DeviceWalker.Devices(root, diagnostics)
            : new List<WalkedDevice> { DeviceWalker.Select(root, deviceName, diagnostics) };

        var result = new UnpluggedItemsInfo { Diagnostics = diagnostics };
        foreach (var device in devices)
        {
            var typed = (Device)Engineering(device.Node);
            var items = new List<UnpluggedItemInfo>();
            foreach (var item in OpennessReflection.Enumerate(OpennessReflection.ReadProperty(typed, "UnpluggedItems", "unplugged items"), "unplugged items"))
            {
                if (item is not IEngineeringObject engineering)
                {
                    continue;
                }

                var node = new EngineeringObjectNode(engineering);
                items.Add(new UnpluggedItemInfo
                {
                    Name = node.TryReadName(),
                    TypeIdentifier = Text(node, "TypeIdentifier"),
                    OrderNumber = Text(node, "OrderNumber"),
                    PositionNumber = Number(node, "PositionNumber"),
                });
            }

            if (deviceName is not null || items.Count > 0)
            {
                result.Devices.Add(new DeviceUnpluggedItemsInfo { DeviceName = device.Name, Items = items });
            }
        }

        return result;
    }

    public static PortTopologyInfo ReadPortTopology(Project project, string? deviceName)
    {
        var diagnostics = new List<string>();
        var root = new EngineeringObjectNode(project);
        var devices = deviceName is null
            ? DeviceWalker.Devices(root, diagnostics)
            : new List<WalkedDevice> { DeviceWalker.Select(root, deviceName, diagnostics) };

        var result = new PortTopologyInfo { Diagnostics = diagnostics };
        foreach (var device in devices)
        {
            foreach (var item in DeviceWalker.Items(device, diagnostics))
            {
                if (Engineering(item.Node) is not DeviceItem deviceItem)
                {
                    continue;
                }

                NetworkPort? port;
                try
                {
                    port = deviceItem.GetService<NetworkPort>();
                }
                catch (EngineeringException ex)
                {
                    diagnostics.Add($"Skipped port service of '{device.Name}/{string.Join("/", item.ItemPath)}': {ex.Message}");
                    continue;
                }

                if (port is null)
                {
                    continue;
                }

                var unavailable = new List<string>();
                var info = new PortInfo
                {
                    DeviceName = device.Name,
                    ItemPath = item.ItemPath.ToList(),
                    ObjectPath = item.ObjectPath,
                    Values = ObjectScalarNormalizer.ReadValues(new EngineeringObjectNode(port), null, unavailable),
                };
                if (unavailable.Count > 0)
                {
                    diagnostics.Add($"Port '{device.Name}/{string.Join("/", item.ItemPath)}': unreadable attributes {string.Join(", ", unavailable)}.");
                }

                foreach (var partner in OpennessReflection.Enumerate(port.ConnectedPorts, "connected ports"))
                {
                    if (partner is NetworkPort partnerPort)
                    {
                        info.Partners.Add(Partner(partnerPort));
                    }
                }

                result.Ports.Add(info);
            }
        }

        return result;
    }

    public static HardwareCompareInfo CompareHardware(Project project, WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceName) || string.IsNullOrWhiteSpace(request.CompareDeviceName))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "DeviceName and CompareDeviceName are required.");
        }

        var diagnostics = new List<string>();
        var root = new EngineeringObjectNode(project);
        var left = DeviceWalker.Select(root, request.DeviceName!, diagnostics);
        var right = DeviceWalker.Select(root, request.CompareDeviceName!, diagnostics);
        var includeIdentical = request.PlcIncludeIdentical ?? false;
        var elements = CompareResultFlattener.Flatten(
            ((Device)Engineering(left.Node)).CompareTo((Device)Engineering(right.Node)),
            includeIdentical);

        var page = ListPager.Page(
            elements,
            new[] { "hardware-compare-v1", left.Name, right.Name, includeIdentical.ToString() },
            element => element.Path.Concat(new[] { "|", element.State, element.Detail }),
            request.ObjectPageSize,
            request.ObjectCursor);
        return new HardwareCompareInfo
        {
            LeftDeviceName = left.Name,
            RightDeviceName = right.Name,
            IncludeIdentical = includeIdentical,
            Elements = page.Items.ToList(),
            TotalCount = page.TotalCount,
            Offset = page.Offset,
            NextCursor = page.NextCursor,
        };
    }

    /// <summary>Walks a partner port's owner chain up to its device to name it.</summary>
    private static PortPartnerInfo Partner(NetworkPort port)
    {
        var partner = new PortPartnerInfo();
        var names = new List<string>();
        IEngineeringObject? current = port.Parent as IEngineeringObject;
        for (var depth = 0; current is not null && depth < DeviceWalker.MaxDeviceItemDepth + 2; depth++)
        {
            if (current is Device device)
            {
                partner.DeviceName = device.Name;
                break;
            }

            if (current is DeviceItem deviceItem)
            {
                names.Insert(0, deviceItem.Name);
            }

            current = current.Parent as IEngineeringObject;
        }

        partner.ItemPath = names;
        return partner;
    }

    private static IEngineeringObject Engineering(IObjectNode node) => ((EngineeringObjectNode)node).EngineeringObject;

    private static string? Text(IObjectNode node, string name)
    {
        var read = node.ReadValue(name);
        return read.Succeeded ? read.Value as string : null;
    }

    private static long? Number(IObjectNode node, string name)
    {
        var read = node.ReadValue(name);
        return read.Succeeded && ObjectScalarNormalizer.TryNormalize(read.Value, out var json) && json is long value ? value : null;
    }
}
