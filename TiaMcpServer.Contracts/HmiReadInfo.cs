using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>Closed vocabulary of HMI runtimes.</summary>
public static class HmiRuntimes
{
    /// <summary>WinCC Unified (<c>HmiSoftware</c>).</summary>
    public const string Unified = "unified";

    /// <summary>WinCC Basic/Comfort/Advanced (<c>HmiTarget</c>).</summary>
    public const string Classic = "classic";
}

/// <summary>Result of <c>list_hmis</c>.</summary>
public sealed class HmiListInfo
{
    public List<HmiSummaryInfo> Hmis { get; set; } = new List<HmiSummaryInfo>();
    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class HmiSummaryInfo
{
    /// <summary>HMI software name, the value <c>hmiName</c> selects.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>One of <see cref="HmiRuntimes"/>.</summary>
    public string Runtime { get; set; } = HmiRuntimes.Unified;

    public string DeviceName { get; set; } = string.Empty;
    public List<string> DeviceGroupPath { get; set; } = new List<string>();
    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();
}

/// <summary>Result of every <c>hmi_read</c> listing, including <c>list_screen_items</c>.</summary>
public sealed class HmiObjectListInfo
{
    public string HmiName { get; set; } = string.Empty;
    public string Runtime { get; set; } = HmiRuntimes.Unified;

    /// <summary>The screen whose items are listed (<c>list_screen_items</c> only).</summary>
    public DomainObjectInfo? Target { get; set; }

    public List<DomainObjectInfo> Items { get; set; } = new List<DomainObjectInfo>();
    public int TotalCount { get; set; }
    public int Offset { get; set; }
    public string? NextCursor { get; set; }
    public List<string> Diagnostics { get; set; } = new List<string>();
}

/// <summary>Result of <c>read_screen_scripts</c>.</summary>
public sealed class HmiScreenScriptsInfo
{
    public string HmiName { get; set; } = string.Empty;
    public DomainObjectInfo Screen { get; set; } = new DomainObjectInfo();
    public List<HmiScriptInfo> Scripts { get; set; } = new List<HmiScriptInfo>();
    public int TotalCount { get; set; }
    public int Offset { get; set; }
    public string? NextCursor { get; set; }
    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class HmiScriptInfo
{
    /// <summary>Screen name, or the screen item name that owns the script.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary><c>event</c>, <c>propertyEvent</c>, or <c>dynamization</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Event type, or the property the script is attached to.</summary>
    public string? Trigger { get; set; }

    public string? ScriptCode { get; set; }
    public string? GlobalDefinitionAreaScriptCode { get; set; }
    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();
}
