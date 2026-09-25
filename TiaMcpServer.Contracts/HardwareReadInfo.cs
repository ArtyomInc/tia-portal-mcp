using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>A device reference with the R0 path that addresses it.</summary>
public sealed class DeviceReferenceInfo
{
    public string Name { get; set; } = string.Empty;
    public string? TypeIdentifier { get; set; }
    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();
}

/// <summary>Result of <c>list_device_groups</c>.</summary>
public sealed class DeviceGroupTreeInfo
{
    /// <summary>Devices that belong to no group.</summary>
    public List<DeviceReferenceInfo> UngroupedDevices { get; set; } = new List<DeviceReferenceInfo>();

    /// <summary>Every device group, depth-first.</summary>
    public List<DeviceGroupInfo> Groups { get; set; } = new List<DeviceGroupInfo>();

    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class DeviceGroupInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Group names from the outermost group down to this one.</summary>
    public List<string> GroupPath { get; set; } = new List<string>();

    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();

    /// <summary>Devices directly in this group.</summary>
    public List<DeviceReferenceInfo> Devices { get; set; } = new List<DeviceReferenceInfo>();
}

/// <summary>Result of <c>list_unplugged_items</c>.</summary>
public sealed class UnpluggedItemsInfo
{
    public List<DeviceUnpluggedItemsInfo> Devices { get; set; } = new List<DeviceUnpluggedItemsInfo>();
    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class DeviceUnpluggedItemsInfo
{
    public string DeviceName { get; set; } = string.Empty;
    public List<UnpluggedItemInfo> Items { get; set; } = new List<UnpluggedItemInfo>();
}

public sealed class UnpluggedItemInfo
{
    public string? Name { get; set; }
    public string? TypeIdentifier { get; set; }
    public string? OrderNumber { get; set; }
    public long? PositionNumber { get; set; }
}

/// <summary>Result of <c>list_hw_identifiers</c>.</summary>
public sealed class HwIdentifiersInfo
{
    public string DeviceName { get; set; } = string.Empty;
    public List<HwIdentifierInfo> Identifiers { get; set; } = new List<HwIdentifierInfo>();
    public int TotalCount { get; set; }
    public int Offset { get; set; }
    public string? NextCursor { get; set; }
    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class HwIdentifierInfo
{
    /// <summary>The hardware identifier (HW ID) value.</summary>
    public long Identifier { get; set; }

    /// <summary>Device-item names from the device down to the owner; empty for the device itself.</summary>
    public List<string> OwnerPath { get; set; } = new List<string>();

    /// <summary>R0 path of the identifier object.</summary>
    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();
}

/// <summary>Result of <c>read_port_topology</c>.</summary>
public sealed class PortTopologyInfo
{
    public List<PortInfo> Ports { get; set; } = new List<PortInfo>();
    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class PortInfo
{
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Device-item names from the device down to the port.</summary>
    public List<string> ItemPath { get; set; } = new List<string>();

    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();

    /// <summary>Scalar attributes of the port's <c>NetworkPort</c> service.</summary>
    public Dictionary<string, object?> Values { get; set; } = new Dictionary<string, object?>();

    /// <summary>Configured partner ports (offline topology).</summary>
    public List<PortPartnerInfo> Partners { get; set; } = new List<PortPartnerInfo>();
}

public sealed class PortPartnerInfo
{
    public string? DeviceName { get; set; }
    public List<string> ItemPath { get; set; } = new List<string>();
}

/// <summary>Result of <c>compare_hardware</c>.</summary>
public sealed class HardwareCompareInfo
{
    public string LeftDeviceName { get; set; } = string.Empty;
    public string RightDeviceName { get; set; } = string.Empty;
    public bool IncludeIdentical { get; set; }
    public List<CompareElementInfo> Elements { get; set; } = new List<CompareElementInfo>();
    public int TotalCount { get; set; }
    public int Offset { get; set; }
    public string? NextCursor { get; set; }
}
