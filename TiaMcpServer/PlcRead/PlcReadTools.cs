using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.DomainReads;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.PlcRead;

[McpServerToolType]
public class PlcReadTools
{
    public const string ToolName = "plc_read";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DomainReadResponse))]
    [Description("Typed PLC program reads (offline, nothing is modified). Run up to 50 operations: list_plcs; listings across nested groups with metadata — list_blocks (number, language, consistency, protection, dates, sizes), list_types, list_watch_tables (watch and force tables), list_technology_objects, list_external_sources, list_software_units, list_alarm_text_lists, list_opcua_server_interfaces; detail reads — read_watch_table (entries), read_technology_object (parameters), read_block_fingerprints; read_checksums (program and text-list checksums); and compare_software (offline differences between two PLCs of the project). plcName selects the PLC (optional when the project has exactly one). Every listed object carries groupPath and an objectPath that object_read accepts for any other attribute. Listings accept nameContains and are paged with pageSize/cursor. Block and UDT source content stays with execute_read_batch.")]
    public static Task<CallToolResult> PlcRead(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of plc_read operations. Each item is { operationId, operation, projectPath?, plcName?, ...operation fields }.")] PlcReadOperationRequest[] operations)
        => DomainReadToolRunner.RunAsync(
            ToolName,
            workerClient,
            PlcReadCatalog.Instance,
            operations,
            async operation => PlcReadPayloadContract.Project(
                operation,
                await PlcReadWorkerInvoker.InvokeAsync(workerClient, operation).ConfigureAwait(false)),
            RetryGuidance);

    internal static string RetryGuidance(StructuredOperationItem item) => item.Operation switch
    {
        "compare_software" or "read_watch_table" or "read_technology_object" =>
            $"Lower pageSize and follow nextCursor, or re-run this operationId in its own {ToolName} call.",
        _ when PlcReadCatalog.ListingOperations.Contains(item.Operation) =>
            $"Lower pageSize or narrow with nameContains, or re-run this operationId in its own {ToolName} call.",
        _ => $"Split the batch: re-run this operationId in its own {ToolName} call.",
    };
}
