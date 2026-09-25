using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.DomainReads;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.ObjectRead;

[McpServerToolType]
public class ObjectReadTools
{
    public const string ToolName = "object_read";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DomainReadResponse))]
    [Description("Generic read access to any TIA Portal Openness object of the open project, for objects no dedicated tool covers (module parameters, technology objects, HMI items, subnet or device properties, and so on). Run up to 50 operations: describe_object (type, compositions, attributes, services, exportable), list_object_children (paged composition elements, each with the exact objectPath to send back), read_object_attributes (typed attribute values), export_object (SimaticML text in character windows with a whole-document sha256), and list_capabilities (installed TIA products and optional Openness assemblies). Start with list_object_children and an empty objectPath, then follow the returned objectPath values; a PLC's software is reached with a device-item path followed by { kind: service, name: SoftwareContainer } and { kind: attribute, name: Software }. Stale, ambiguous, or unknown path steps fail explicitly and never resolve to another object. Reads run independently; nothing is modified.")]
    public static Task<CallToolResult> ObjectRead(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of object_read operations. Each item is { operationId, operation, projectPath?, ...operation fields }.")] ObjectReadOperationRequest[] operations)
        => DomainReadToolRunner.RunAsync(
            ToolName,
            workerClient,
            ObjectReadCatalog.Instance,
            operations,
            async operation => ObjectReadPayloadContract.Project(
                operation,
                await ObjectReadWorkerInvoker.InvokeAsync(workerClient, operation).ConfigureAwait(false)),
            RetryGuidance);

    internal static string RetryGuidance(StructuredOperationItem item) => item.Operation switch
    {
        "export_object" => "Request a smaller window with maxChars, continuing from nextOffset, or re-run this "
            + $"operationId in its own {ToolName} call.",
        "list_object_children" => "Lower pageSize or restrict compositionNames, or re-run this operationId in its own "
            + $"{ToolName} call.",
        "read_object_attributes" => "Request fewer attributeNames, or re-run this operationId in its own "
            + $"{ToolName} call.",
        _ => $"Split the batch: re-run this operationId in its own {ToolName} call.",
    };
}
