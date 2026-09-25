using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.LibraryRead;

/// <summary>Strict request shape of one <c>library_read</c> operation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LibraryReadOperationRequest : IOperationBatchItem
{
    [Description("Client-supplied unique identifier for this operation; returned results are keyed by it.")]
    public string OperationId { get; set; } = string.Empty;

    [Description("Operation to run: list_libraries, list_library_types, read_library_type, list_master_copies, check_library_updates, or find_type_instances.")]
    public string Operation { get; set; } = string.Empty;

    [Description("Optional absolute project path (.ap21). When omitted, the active project is used.")]
    public string? ProjectPath { get; set; }

    [Description("Every operation except list_libraries: exact name of an already open global library, as returned by list_libraries. Omit for the project library. Libraries are never opened by this tool.")]
    public string? LibraryName { get; set; }

    [Description("read_library_type and find_type_instances: exact library type name.")]
    public string? Name { get; set; }

    [Description("read_library_type and find_type_instances: optional folder names from the type folder root to the type (as returned in groupPath). Use it when several types share the name.")]
    public IReadOnlyList<string>? FolderPath { get; set; }

    [Description("find_type_instances: exact PLC software name to search; optional when the project has exactly one PLC.")]
    public string? PlcName { get; set; }

    [Description("find_type_instances: optional exact version number (for example \"1.0.2\"); all versions when omitted.")]
    public string? Version { get; set; }

    [Description("check_library_updates: when true, up-to-date types are reported too. Defaults to false (out-of-date only).")]
    public bool? IncludeUpToDate { get; set; }

    [Description("Paged operations (list_library_types, list_master_copies, check_library_updates): items per page, 1-200 (default 50).")]
    public int? PageSize { get; set; }

    [Description("Paged operations: opaque cursor from the previous page's nextCursor. Resend the same other fields.")]
    public string? Cursor { get; set; }
}
