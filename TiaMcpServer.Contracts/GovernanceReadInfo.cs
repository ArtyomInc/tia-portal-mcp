using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>Result of <c>list_portal_processes</c>.</summary>
public sealed class PortalProcessListInfo
{
    public List<PortalProcessInfo> Processes { get; set; } = new List<PortalProcessInfo>();
    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class PortalProcessInfo
{
    public int Id { get; set; }
    public string? Mode { get; set; }
    public string? ProjectPath { get; set; }
    public string? AcquisitionTime { get; set; }

    /// <summary>True for the TIA Portal process this server is attached to.</summary>
    public bool IsAttached { get; set; }
}

/// <summary>
/// Result of the sectioned governance reads (<c>read_umac</c>, <c>read_safety</c>,
/// <c>list_test_suite</c>, <c>list_multiuser</c>, <c>list_vci_workspaces</c>).
/// </summary>
public sealed class GovernanceInfo
{
    /// <summary>What was read: <c>project</c>, <c>portal</c>, or the PLC name for <c>read_safety</c>.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>The <c>object_read</c> root every object path starts from.</summary>
    public string Root { get; set; } = ObjectRoots.Project;

    /// <summary>Scalar values of the scope object itself (for example Safety login state).</summary>
    public Dictionary<string, object?> Values { get; set; } = new Dictionary<string, object?>();

    public List<GovernanceSectionInfo> Sections { get; set; } = new List<GovernanceSectionInfo>();
    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class GovernanceSectionInfo
{
    public string Name { get; set; } = string.Empty;
    public List<GovernanceItemInfo> Items { get; set; } = new List<GovernanceItemInfo>();
}

public sealed class GovernanceItemInfo
{
    public string? Name { get; set; }
    public string Kind { get; set; } = string.Empty;
    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();
    public Dictionary<string, object?> Values { get; set; } = new Dictionary<string, object?>();
    public List<string> Unavailable { get; set; } = new List<string>();

    /// <summary>Names of associated objects, for example a user's roles or a role's rights.</summary>
    public Dictionary<string, List<string>> References { get; set; } = new Dictionary<string, List<string>>();
}
