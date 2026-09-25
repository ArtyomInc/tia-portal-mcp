using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.DomainReads;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.HmiRead;

/// <summary>Strict request shape of one <c>hmi_read</c> operation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HmiReadOperationRequest : IOperationBatchItem
{
    [Description("Client-supplied unique identifier for this operation; returned results are keyed by it.")]
    public string OperationId { get; set; } = string.Empty;

    [Description("Operation to run: list_hmis, list_screens, list_screen_items, read_screen_scripts, list_hmi_tags, list_hmi_connections, list_hmi_alarms, list_hmi_logs, list_hmi_text_lists, or list_hmi_scripts.")]
    public string Operation { get; set; } = string.Empty;

    [Description("Optional absolute project path (.ap21). When omitted, the active project is used.")]
    public string? ProjectPath { get; set; }

    [Description("Exact HMI software name as returned by list_hmis. Optional for every operation except list_hmis (where it is not valid); when omitted, the project must contain exactly one HMI.")]
    public string? HmiName { get; set; }

    [Description("list_screen_items and read_screen_scripts: exact screen name.")]
    public string? Screen { get; set; }

    [Description("list_screen_items and read_screen_scripts: optional screen group names (as returned in groupPath) when several screens share the name.")]
    public IReadOnlyList<string>? GroupPath { get; set; }

    [Description("Listings only: optional case-insensitive substring filter on object names.")]
    public string? NameContains { get; set; }

    [Description("Paged operations (listings, list_screen_items, read_screen_scripts): items per page, 1-200 (default 50).")]
    public int? PageSize { get; set; }

    [Description("Paged operations: opaque cursor from the previous page's nextCursor. Resend the same other fields.")]
    public string? Cursor { get; set; }
}

/// <summary>Whitelists and validates the <c>hmi_read</c> operations. Pure and Siemens-free.</summary>
public static class HmiReadCatalog
{
    public const int MaxNameLength = 512;

    public static readonly IReadOnlyList<string> ListingOperations = new[]
    {
        "list_screens", "list_hmi_tags", "list_hmi_connections", "list_hmi_alarms", "list_hmi_logs",
        "list_hmi_text_lists", "list_hmi_scripts",
    };

    private static readonly string[] Paging = { "pageSize", "cursor" };

    public static DomainReadCatalog<HmiReadOperationRequest> Instance { get; } = new(
        "hmi_read",
        new[] { new DomainReadOperationSpec<HmiReadOperationRequest>("list_hmis", Array.Empty<string>(), Array.Empty<string>()) }
            .Concat(ListingOperations.Take(1).Select(Listing))
            .Concat(new[]
            {
                new DomainReadOperationSpec<HmiReadOperationRequest>(
                    "list_screen_items", new[] { "screen" }, new[] { "hmiName", "groupPath" }.Concat(Paging).ToArray(), Validate),
                new DomainReadOperationSpec<HmiReadOperationRequest>(
                    "read_screen_scripts", new[] { "screen" }, new[] { "hmiName", "groupPath" }.Concat(Paging).ToArray(), Validate),
            })
            .Concat(ListingOperations.Skip(1).Select(Listing)),
        new[]
        {
            new DomainReadField<HmiReadOperationRequest>("hmiName", o => o.HmiName is not null),
            new DomainReadField<HmiReadOperationRequest>("screen", o => o.Screen is not null, o => !string.IsNullOrWhiteSpace(o.Screen)),
            new DomainReadField<HmiReadOperationRequest>("groupPath", o => o.GroupPath is not null),
            new DomainReadField<HmiReadOperationRequest>("nameContains", o => o.NameContains is not null),
            new DomainReadField<HmiReadOperationRequest>("pageSize", o => o.PageSize is not null),
            new DomainReadField<HmiReadOperationRequest>("cursor", o => o.Cursor is not null),
        });

    public static IReadOnlyList<string> OperationNames => Instance.OperationNames;

    private static DomainReadOperationSpec<HmiReadOperationRequest> Listing(string name)
        => new(name, Array.Empty<string>(), new[] { "hmiName", "nameContains" }.Concat(Paging).ToArray(), Validate);

    private static void Validate(HmiReadOperationRequest operation, List<string> errors)
    {
        Text(operation.HmiName, "hmiName", errors);
        Text(operation.NameContains, "nameContains", errors);
        if (operation.Screen is not null && operation.Screen.Length > MaxNameLength)
        {
            errors.Add($"'screen' must be at most {MaxNameLength} characters.");
        }

        if (operation.GroupPath is not null && (operation.GroupPath.Count > 32 || operation.GroupPath.Any(segment => segment is null || segment.Length > MaxNameLength)))
        {
            errors.Add("'groupPath' must contain at most 32 non-null group names.");
        }

        if (operation.PageSize is < 1 or > ObjectReadLimits.MaxPageSize)
        {
            errors.Add($"'pageSize' must be between 1 and {ObjectReadLimits.MaxPageSize}.");
        }

        if (operation.Cursor is not null && string.IsNullOrWhiteSpace(operation.Cursor))
        {
            errors.Add("'cursor' must not be blank.");
        }
    }

    private static void Text(string? value, string field, List<string> errors)
    {
        if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > MaxNameLength))
        {
            errors.Add($"'{field}' must be nonblank and at most {MaxNameLength} characters when supplied.");
        }
    }
}

/// <summary>Dispatches and decodes <c>hmi_read</c> operations.</summary>
public static class HmiReadWorker
{
    public static Task<WorkerCallResult> InvokeAsync(OpennessWorkerClient client, HmiReadOperationRequest operation)
    {
        if (!HmiReadCatalog.Instance.TryGetSpec(operation.Operation, out _))
        {
            return Task.FromResult(WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                $"Unsupported hmi_read operation '{operation.Operation}'."));
        }

        return client.ReadDomainAsync(operation.Operation, operation.ProjectPath, request =>
        {
            request.HmiName = operation.HmiName;
            request.PlcObjectName = operation.Screen;
            request.PlcGroupPath = operation.GroupPath?.ToList();
            request.PlcNameContains = operation.NameContains;
            request.ObjectPageSize = operation.PageSize;
            request.ObjectCursor = operation.Cursor;
        });
    }

    public static StructuredOperationItem Project(HmiReadOperationRequest operation, WorkerCallResult workerResult)
        => DomainPayloadProjector.Project(operation, workerResult, payload => Decode(operation.Operation, payload));

    public static JsonElement Decode(string operation, string payload)
    {
        if (operation == "list_hmis")
        {
            return DomainPayloadProjector.Decode<HmiListInfo>(payload, ValidateHmis);
        }

        if (operation == "read_screen_scripts")
        {
            return DomainPayloadProjector.Decode<HmiScreenScriptsInfo>(payload, ValidateScripts);
        }

        if (operation == "list_screen_items" || HmiReadCatalog.ListingOperations.Contains(operation))
        {
            return DomainPayloadProjector.Decode<HmiObjectListInfo>(payload, value => ValidateList(value, operation == "list_screen_items"));
        }

        throw new JsonException($"No declared result contract for hmi_read operation '{operation}'.");
    }

    private static void ValidateHmis(HmiListInfo value)
    {
        Require(value.Hmis, "hmis");
        Require(value.Diagnostics, "diagnostics");
        foreach (var hmi in value.Hmis)
        {
            Require(hmi?.Name, "hmis[].name");
            Runtime(hmi!.Runtime);
            Require(hmi.DeviceName, "hmis[].deviceName");
            Require(hmi.DeviceGroupPath, "hmis[].deviceGroupPath");
            Path(hmi.ObjectPath, "hmis[].objectPath");
        }
    }

    private static void ValidateList(HmiObjectListInfo value, bool requiresTarget)
    {
        Require(value.HmiName, "hmiName");
        Runtime(value.Runtime);
        Require(value.Items, "items");
        Require(value.Diagnostics, "diagnostics");
        if (requiresTarget != (value.Target is not null))
        {
            throw new JsonException("'target' is present exactly for list_screen_items.");
        }

        if (value.Target is not null)
        {
            Object(value.Target, "target");
        }

        Page(value.TotalCount, value.Offset, value.Items.Count, value.NextCursor);
        value.Items.ForEach(item => Object(item, "items[]"));
    }

    private static void ValidateScripts(HmiScreenScriptsInfo value)
    {
        Require(value.HmiName, "hmiName");
        Object(value.Screen, "screen");
        Require(value.Scripts, "scripts");
        Require(value.Diagnostics, "diagnostics");
        Page(value.TotalCount, value.Offset, value.Scripts.Count, value.NextCursor);
        foreach (var script in value.Scripts)
        {
            Require(script?.Owner, "scripts[].owner");
            if (script!.Kind is not ("event" or "propertyEvent" or "dynamization"))
            {
                throw new JsonException("'scripts[].kind' is not a known script kind.");
            }

            Path(script.ObjectPath, "scripts[].objectPath");
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

    private static void Runtime(string? runtime)
    {
        if (runtime is not (HmiRuntimes.Unified or HmiRuntimes.Classic))
        {
            throw new JsonException("'runtime' is not a known HMI runtime.");
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
public class HmiReadTools
{
    public const string ToolName = "hmi_read";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DomainReadResponse))]
    [Description("Read WinCC Unified and WinCC Classic HMI configuration offline (nothing is modified). Run up to 50 operations: list_hmis (HMI software with runtime unified/classic); listings across groups — list_screens, list_hmi_tags, list_hmi_connections, list_hmi_alarms and list_hmi_logs (Unified), list_hmi_text_lists, list_hmi_scripts; list_screen_items (Unified: widgets of one screen with their scalar properties); and read_screen_scripts (Unified: JavaScript of the screen's and its items' events and script dynamizations). hmiName selects the HMI (optional when there is exactly one). A listing a runtime does not expose fails capability_unavailable. Every object carries an objectPath for object_read (for example to export a Classic screen).")]
    public static Task<CallToolResult> HmiRead(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of hmi_read operations. Each item is { operationId, operation, projectPath?, hmiName?, ...operation fields }.")] HmiReadOperationRequest[] operations)
        => DomainReadToolRunner.RunAsync(
            ToolName,
            workerClient,
            HmiReadCatalog.Instance,
            operations,
            async operation => HmiReadWorker.Project(operation, await HmiReadWorker.InvokeAsync(workerClient, operation).ConfigureAwait(false)),
            RetryGuidance);

    internal static string RetryGuidance(StructuredOperationItem item) => item.Operation switch
    {
        "read_screen_scripts" => $"Lower pageSize and follow nextCursor (scripts can be long), or re-run this operationId in its own {ToolName} call.",
        "list_hmis" => $"Split the batch: re-run this operationId in its own {ToolName} call.",
        _ => $"Lower pageSize, narrow with nameContains, or re-run this operationId in its own {ToolName} call.",
    };
}
