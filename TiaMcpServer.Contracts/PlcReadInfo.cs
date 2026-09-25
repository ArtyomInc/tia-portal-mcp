using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>Result of <c>list_plcs</c>.</summary>
public sealed class PlcListInfo
{
    public List<PlcSummaryInfo> Plcs { get; set; } = new List<PlcSummaryInfo>();
    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class PlcSummaryInfo
{
    /// <summary>PLC software name, the value <c>plcName</c> selects.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Name of the device that hosts the PLC.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Names of the device groups that contain the device, outermost first.</summary>
    public List<string> DeviceGroupPath { get; set; } = new List<string>();

    /// <summary>R0 path to the PLC software, usable with <c>object_read</c>.</summary>
    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();
}

/// <summary>
/// One listed domain object (PLC, library, HMI, …). <see cref="Values"/> holds the operation's documented attributes the
/// object declares, as JSON scalars; <see cref="Unavailable"/> names declared ones that could not
/// be read or represented.
/// </summary>
public sealed class DomainObjectInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Simple type name, for example <c>OB</c>, <c>GlobalDB</c>, <c>PlcStruct</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    /// <summary>Group names from the listing root down to the object's group.</summary>
    public List<string> GroupPath { get; set; } = new List<string>();

    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();
    public Dictionary<string, object?> Values { get; set; } = new Dictionary<string, object?>();
    public List<string> Unavailable { get; set; } = new List<string>();
}

/// <summary>Result of every <c>plc_read</c> listing operation.</summary>
public sealed class PlcObjectListInfo
{
    public string PlcName { get; set; } = string.Empty;
    public List<DomainObjectInfo> Items { get; set; } = new List<DomainObjectInfo>();
    public int TotalCount { get; set; }
    public int Offset { get; set; }
    public string? NextCursor { get; set; }
    public List<string> Diagnostics { get; set; } = new List<string>();
}

/// <summary>One entry of a listed object: a watch/force table entry, a technology-object parameter, …</summary>
public sealed class DomainEntryInfo
{
    public string? Name { get; set; }
    public Dictionary<string, object?> Values { get; set; } = new Dictionary<string, object?>();
    public List<string> Unavailable { get; set; } = new List<string>();
}

/// <summary>Result of <c>read_watch_table</c> and <c>read_technology_object</c>.</summary>
public sealed class PlcObjectEntriesInfo
{
    public string PlcName { get; set; } = string.Empty;
    public DomainObjectInfo Target { get; set; } = new DomainObjectInfo();
    public List<DomainEntryInfo> Entries { get; set; } = new List<DomainEntryInfo>();
    public int TotalCount { get; set; }
    public int Offset { get; set; }
    public string? NextCursor { get; set; }
    public List<string> Diagnostics { get; set; } = new List<string>();
}

/// <summary>Result of <c>read_block_fingerprints</c>.</summary>
public sealed class PlcFingerprintsInfo
{
    public string PlcName { get; set; } = string.Empty;
    public DomainObjectInfo Target { get; set; } = new DomainObjectInfo();
    public List<PlcFingerprintInfo> Fingerprints { get; set; } = new List<PlcFingerprintInfo>();
}

public sealed class PlcFingerprintInfo
{
    /// <summary>Fingerprint category, for example <c>Code</c>, <c>Interface</c>, <c>Comments</c>.</summary>
    public string Id { get; set; } = string.Empty;

    public string? Value { get; set; }
}

/// <summary>Result of <c>read_checksums</c>.</summary>
public sealed class PlcChecksumsInfo
{
    public string PlcName { get; set; } = string.Empty;
    public string? Software { get; set; }
    public string? TextLists { get; set; }
    public List<string> Diagnostics { get; set; } = new List<string>();
}

/// <summary>Result of <c>compare_software</c>.</summary>
public sealed class PlcCompareInfo
{
    public string LeftPlcName { get; set; } = string.Empty;
    public string RightPlcName { get; set; } = string.Empty;
    public bool IncludeIdentical { get; set; }
    public List<CompareElementInfo> Elements { get; set; } = new List<CompareElementInfo>();
    public int TotalCount { get; set; }
    public int Offset { get; set; }
    public string? NextCursor { get; set; }
}

public sealed class CompareElementInfo
{
    /// <summary>Element names (left name, else right name) from below the root to this element.</summary>
    public List<string> Path { get; set; } = new List<string>();
    public int Depth { get; set; }
    public string? LeftName { get; set; }
    public string? RightName { get; set; }

    /// <summary><c>CompareResultState</c> symbol, for example <c>ObjectsDifferent</c> or <c>LeftMissing</c>.</summary>
    public string State { get; set; } = string.Empty;

    public string? Detail { get; set; }
}
