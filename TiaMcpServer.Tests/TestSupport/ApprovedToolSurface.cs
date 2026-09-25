using TiaMcpServer.Batch;
using TiaMcpServer.Network;
using TiaMcpServer.ObjectRead;
using TiaMcpServer.PlcRead;
using TiaMcpServer.Tools;

namespace TiaMcpServer.Tests;

/// <summary>
/// The single approved MCP tool census the surface tests compare against. Adding a tool means
/// adding it here — and to <c>Program.cs</c> and the protocol harness — deliberately, in one place.
/// </summary>
internal static class ApprovedToolSurface
{
    /// <summary>Tool classes registered in every access mode, in Program.cs order.</summary>
    public static readonly Type[] ReadOnlyToolTypes =
    {
        typeof(ProjectReadTools),
        typeof(ReadBatchTools),
        typeof(NetworkReadTools),
        typeof(ObjectReadTools),
        typeof(PlcReadTools),
    };

    /// <summary>Tool names exposed in read-only mode, ordinal-sorted.</summary>
    public static readonly string[] ReadOnlyToolNames =
    {
        "browse_project_tree",
        "execute_read_batch",
        "get_project_status",
        "network_read",
        "object_read",
        "plc_read",
    };

    /// <summary>Tool names exposed only in read-write mode.</summary>
    public static readonly string[] WriteOnlyToolNames =
    {
        "apply_write_batch",
        "archive_project",
        "close_project",
        "compile_check",
        "create_project",
        "network_write",
        "open_project",
        "preview_write_batch",
        "save_project",
        "save_project_as",
    };

    /// <summary>Tool names exposed in read-write mode, ordinal-sorted.</summary>
    public static string[] ReadWriteToolNames
        => ReadOnlyToolNames.Concat(WriteOnlyToolNames).OrderBy(name => name, StringComparer.Ordinal).ToArray();
}
