using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.DomainReads;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.LibraryRead;

/// <summary>Dispatches validated <c>library_read</c> operations to their worker methods.</summary>
public static class LibraryReadWorkerInvoker
{
    public static Task<WorkerCallResult> InvokeAsync(OpennessWorkerClient client, LibraryReadOperationRequest operation)
    {
        if (!LibraryReadCatalog.Instance.TryGetSpec(operation.Operation, out _))
        {
            return Task.FromResult(WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                $"Unsupported library_read operation '{operation.Operation}'."));
        }

        // Worker methods carry the public operation names; the catalog already rejected every
        // field the operation does not declare.
        return client.ReadDomainAsync(operation.Operation, operation.ProjectPath, request =>
        {
            request.LibraryName = operation.LibraryName;
            request.PlcObjectName = operation.Name;
            request.PlcGroupPath = operation.FolderPath?.ToList();
            request.PlcName = operation.PlcName;
            request.LibraryVersion = operation.Version;
            request.LibraryIncludeUpToDate = operation.IncludeUpToDate;
            request.ObjectPageSize = operation.PageSize;
            request.ObjectCursor = operation.Cursor;
        });
    }
}

/// <summary>The only decoder of <c>library_read</c> worker success payloads.</summary>
public static class LibraryReadPayloadContract
{
    public static StructuredOperationItem Project(LibraryReadOperationRequest operation, WorkerCallResult workerResult)
        => DomainPayloadProjector.Project(operation, workerResult, payload => Decode(operation.Operation, payload));

    public static JsonElement Decode(string operation, string payload) => operation switch
    {
        "list_libraries" => DomainPayloadProjector.Decode<LibraryListInfo>(payload, ValidateLibraries),
        "list_library_types" or "list_master_copies" => DomainPayloadProjector.Decode<LibraryObjectListInfo>(payload, ValidateList),
        "read_library_type" => DomainPayloadProjector.Decode<LibraryTypeInfo>(payload, ValidateType),
        "check_library_updates" => DomainPayloadProjector.Decode<LibraryUpdateCheckInfo>(payload, ValidateUpdates),
        "find_type_instances" => DomainPayloadProjector.Decode<LibraryTypeInstancesInfo>(payload, ValidateInstances),
        _ => throw new JsonException($"No declared result contract for library_read operation '{operation}'."),
    };

    private static void ValidateLibraries(LibraryListInfo value)
    {
        Require(value.Libraries, "libraries");
        Require(value.Diagnostics, "diagnostics");
        foreach (var library in value.Libraries)
        {
            Require(library?.Name, "libraries[].name");
            Kind(library!.Kind);
            Root(library.Root);
            Require(library.Values, "libraries[].values");
            Path(library.ObjectPath, "libraries[].objectPath");
        }
    }

    private static void ValidateList(LibraryObjectListInfo value)
    {
        Require(value.LibraryName, "libraryName");
        Kind(value.LibraryKind);
        Root(value.Root);
        Require(value.Items, "items");
        Require(value.Diagnostics, "diagnostics");
        Page(value.TotalCount, value.Offset, value.Items.Count, value.NextCursor);
        value.Items.ForEach(item => Object(item, "items[]"));
    }

    private static void ValidateType(LibraryTypeInfo value)
    {
        Require(value.LibraryName, "libraryName");
        Kind(value.LibraryKind);
        Root(value.Root);
        Object(value.Type, "type");
        Require(value.Versions, "versions");
        Require(value.Diagnostics, "diagnostics");
        foreach (var version in value.Versions)
        {
            Require(version, "versions[]");
            Path(version.ObjectPath, "versions[].objectPath");
            Require(version.Values, "versions[].values");
            Require(version.Unavailable, "versions[].unavailable");
            Require(version.Dependencies, "versions[].dependencies");
            Require(version.Dependents, "versions[].dependents");
        }
    }

    private static void ValidateUpdates(LibraryUpdateCheckInfo value)
    {
        Require(value.LibraryName, "libraryName");
        Kind(value.LibraryKind);
        Require(value.Messages, "messages");
        Page(value.TotalCount, value.Offset, value.Messages.Count, value.NextCursor);
        if (value.Messages.Any(message => message is null || message.Depth < 0))
        {
            throw new JsonException("An update-check message is inconsistent.");
        }
    }

    private static void ValidateInstances(LibraryTypeInstancesInfo value)
    {
        Require(value.LibraryName, "libraryName");
        Kind(value.LibraryKind);
        Require(value.TypeName, "typeName");
        Require(value.PlcName, "plcName");
        Require(value.Instances, "instances");
        Require(value.Diagnostics, "diagnostics");
        foreach (var instance in value.Instances)
        {
            Require(instance, "instances[]");
            Require(instance.Kind, "instances[].kind");
            Require(instance.TypeName, "instances[].typeName");
            Require(instance.GroupPath, "instances[].groupPath");
        }
    }

    private static void Object(DomainObjectInfo? item, string member)
    {
        Require(item, member);
        Require(item!.Name, member + ".name");
        Require(item.Kind, member + ".kind");
        Require(item.TypeName, member + ".typeName");
        Require(item.GroupPath, member + ".groupPath");
        Require(item.Values, member + ".values");
        Require(item.Unavailable, member + ".unavailable");
        Path(item.ObjectPath, member + ".objectPath");
        if (item.Values.Values.Any(v => v is JsonElement { ValueKind: JsonValueKind.Object }))
        {
            throw new JsonException($"'{member}.values' must hold scalar values.");
        }
    }

    private static void Path(List<ObjectPathSegmentInfo>? path, string member)
    {
        Require(path, member);
        if (path!.Count == 0 || path.Any(segment => segment is null || !ObjectPathSegmentKinds.IsKnown(segment.Kind) || string.IsNullOrEmpty(segment.Name)))
        {
            throw new JsonException($"'{member}' is not a valid object path.");
        }
    }

    private static void Kind(string? kind)
    {
        if (kind is not (LibraryKinds.Project or LibraryKinds.Global))
        {
            throw new JsonException("'libraryKind' is not a known library kind.");
        }
    }

    private static void Root(string? root)
    {
        if (!ObjectRoots.IsKnown(root))
        {
            throw new JsonException("'root' is not a known object root.");
        }
    }

    private static void Page(int totalCount, int offset, int count, string? nextCursor)
    {
        if (totalCount < 0 || offset < 0 || offset + count > totalCount || (nextCursor is null) != (offset + count >= totalCount))
        {
            throw new JsonException("The page position is inconsistent.");
        }
    }

    private static void Require(object? value, string member) => DomainPayloadProjector.RequireNotNull(value, member);
}

[McpServerToolType]
public class LibraryReadTools
{
    public const string ToolName = "library_read";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DomainReadResponse))]
    [Description("Read the project library and the global libraries already open in TIA Portal (libraries are never opened, updated, or modified). Run up to 50 operations: list_libraries; list_library_types (types across folders with status, guid, author); read_library_type (every version with number, state, default flag, dates, dependencies, and dependents); list_master_copies; check_library_updates (the library update-check report, out-of-date types by default); and find_type_instances (instances of a type in one PLC). libraryName selects an open global library; omit it for the project library. Results carry objectPath values for object_read — with root 'portal' for global libraries.")]
    public static Task<CallToolResult> LibraryRead(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of library_read operations. Each item is { operationId, operation, projectPath?, libraryName?, ...operation fields }.")] LibraryReadOperationRequest[] operations)
        => DomainReadToolRunner.RunAsync(
            ToolName,
            workerClient,
            LibraryReadCatalog.Instance,
            operations,
            async operation => LibraryReadPayloadContract.Project(
                operation,
                await LibraryReadWorkerInvoker.InvokeAsync(workerClient, operation).ConfigureAwait(false)),
            RetryGuidance);

    internal static string RetryGuidance(StructuredOperationItem item) => item.Operation switch
    {
        "list_library_types" or "list_master_copies" or "check_library_updates" =>
            $"Lower pageSize and follow nextCursor, or re-run this operationId in its own {ToolName} call.",
        _ => $"Split the batch: re-run this operationId in its own {ToolName} call.",
    };
}
