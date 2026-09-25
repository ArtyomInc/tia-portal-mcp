using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.PlcRead;

/// <summary>
/// Strict request shape of one <c>plc_read</c> operation. Only the fields declared for the selected
/// operation are accepted by <see cref="PlcReadCatalog"/>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcReadOperationRequest : IOperationBatchItem
{
    [Description("Client-supplied unique identifier for this operation; returned results are keyed by it.")]
    public string OperationId { get; set; } = string.Empty;

    [Description("Operation to run: list_plcs, list_blocks, list_types, list_watch_tables, read_watch_table, list_technology_objects, read_technology_object, list_external_sources, list_software_units, list_alarm_text_lists, list_opcua_server_interfaces, read_block_fingerprints, read_checksums, or compare_software.")]
    public string Operation { get; set; } = string.Empty;

    [Description("Optional absolute project path (.ap21). When omitted, the active project is used.")]
    public string? ProjectPath { get; set; }

    [Description("Exact PLC software name as returned by list_plcs. Optional for every operation except list_plcs (where it is not valid); when omitted, the project must contain exactly one PLC.")]
    public string? PlcName { get; set; }

    [Description("read_watch_table, read_technology_object, read_block_fingerprints: exact name of the table, technology object, or block.")]
    public string? Name { get; set; }

    [Description("read_watch_table, read_technology_object, read_block_fingerprints: optional group names from the listing root to the object's group (as returned in groupPath). Use it when several objects share the name.")]
    public IReadOnlyList<string>? GroupPath { get; set; }

    [Description("Listings only: optional case-insensitive substring filter on object names.")]
    public string? NameContains { get; set; }

    [Description("read_technology_object only: 1-200 unique parameter names to return. When omitted, every parameter is returned.")]
    public IReadOnlyList<string>? ParameterNames { get; set; }

    [Description("compare_software only: exact name of the PLC software to compare against (the right-hand side).")]
    public string? ComparePlcName { get; set; }

    [Description("compare_software only: when true, identical elements are returned too. Defaults to false (differences only).")]
    public bool? IncludeIdentical { get; set; }

    [Description("Paged operations (listings, read_watch_table, read_technology_object, compare_software): items per page, 1-200 (default 50).")]
    public int? PageSize { get; set; }

    [Description("Paged operations: opaque cursor from the previous page's nextCursor. Resend the same other fields.")]
    public string? Cursor { get; set; }
}
