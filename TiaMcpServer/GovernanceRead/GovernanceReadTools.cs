using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.DomainReads;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.GovernanceRead;

/// <summary>Strict request shape of one <c>governance_read</c> operation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GovernanceReadOperationRequest : IOperationBatchItem
{
    [Description("Client-supplied unique identifier for this operation; returned results are keyed by it.")]
    public string OperationId { get; set; } = string.Empty;

    [Description("Operation to run: list_portal_processes, read_umac, read_safety, list_test_suite, list_multiuser, or list_vci_workspaces.")]
    public string Operation { get; set; } = string.Empty;

    [Description("Optional absolute project path (.ap21). When omitted, the active project is used.")]
    public string? ProjectPath { get; set; }

    [Description("read_safety only: exact PLC software name; optional when the project has exactly one PLC.")]
    public string? PlcName { get; set; }
}

/// <summary>Whitelists, dispatches, and decodes the <c>governance_read</c> operations.</summary>
public static class GovernanceReadCatalog
{
    public static readonly IReadOnlyList<string> SectionedOperations = new[]
    {
        "read_umac", "read_safety", "list_test_suite", "list_multiuser", "list_vci_workspaces",
    };

    public static DomainReadCatalog<GovernanceReadOperationRequest> Instance { get; } = new(
        "governance_read",
        new[]
        {
            new DomainReadOperationSpec<GovernanceReadOperationRequest>("list_portal_processes", Array.Empty<string>(), Array.Empty<string>()),
            new DomainReadOperationSpec<GovernanceReadOperationRequest>("read_umac", Array.Empty<string>(), Array.Empty<string>()),
            new DomainReadOperationSpec<GovernanceReadOperationRequest>("read_safety", Array.Empty<string>(), new[] { "plcName" }, ValidatePlc),
            new DomainReadOperationSpec<GovernanceReadOperationRequest>("list_test_suite", Array.Empty<string>(), Array.Empty<string>()),
            new DomainReadOperationSpec<GovernanceReadOperationRequest>("list_multiuser", Array.Empty<string>(), Array.Empty<string>()),
            new DomainReadOperationSpec<GovernanceReadOperationRequest>("list_vci_workspaces", Array.Empty<string>(), Array.Empty<string>()),
        },
        new[] { new DomainReadField<GovernanceReadOperationRequest>("plcName", o => o.PlcName is not null) });

    public static IReadOnlyList<string> OperationNames => Instance.OperationNames;

    public static Task<WorkerCallResult> InvokeAsync(OpennessWorkerClient client, GovernanceReadOperationRequest operation)
        => Instance.TryGetSpec(operation.Operation, out _)
            ? client.ReadDomainAsync(operation.Operation, operation.ProjectPath, request => request.PlcName = operation.PlcName)
            : Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, $"Unsupported governance_read operation '{operation.Operation}'."));

    public static StructuredOperationItem Project(GovernanceReadOperationRequest operation, WorkerCallResult workerResult)
        => DomainPayloadProjector.Project(operation, workerResult, payload => Decode(operation.Operation, payload));

    public static JsonElement Decode(string operation, string payload)
    {
        if (operation == "list_portal_processes")
        {
            return DomainPayloadProjector.Decode<PortalProcessListInfo>(payload, value =>
            {
                Require(value.Processes, "processes");
                Require(value.Diagnostics, "diagnostics");
                if (value.Processes.Any(process => process is null) || value.Processes.Count(process => process.IsAttached) > 1)
                {
                    throw new JsonException("The process list is inconsistent.");
                }
            });
        }

        if (SectionedOperations.Contains(operation))
        {
            return DomainPayloadProjector.Decode<GovernanceInfo>(payload, ValidateSections);
        }

        throw new JsonException($"No declared result contract for governance_read operation '{operation}'.");
    }

    private static void ValidateSections(GovernanceInfo value)
    {
        Require(value.Scope, "scope");
        if (!ObjectRoots.IsKnown(value.Root))
        {
            throw new JsonException("'root' is not a known object root.");
        }

        Require(value.Values, "values");
        Require(value.Sections, "sections");
        Require(value.Diagnostics, "diagnostics");
        foreach (var section in value.Sections)
        {
            Require(section?.Name, "sections[].name");
            Require(section!.Items, "sections[].items");
            foreach (var item in section.Items)
            {
                Require(item, "sections[].items[]");
                Require(item.Kind, "sections[].items[].kind");
                Require(item.Values, "sections[].items[].values");
                Require(item.Unavailable, "sections[].items[].unavailable");
                Require(item.References, "sections[].items[].references");
                Require(item.ObjectPath, "sections[].items[].objectPath");
                if (item.ObjectPath.Count == 0 || item.ObjectPath.Any(s => s is null || !ObjectPathSegmentKinds.IsKnown(s.Kind)))
                {
                    throw new JsonException("A governance object path is invalid.");
                }

                if (item.Values.Values.Any(v => v is JsonElement { ValueKind: JsonValueKind.Object }))
                {
                    throw new JsonException("Governance values must be scalars.");
                }
            }
        }
    }

    private static void ValidatePlc(GovernanceReadOperationRequest operation, List<string> errors)
    {
        if (operation.PlcName is not null && (string.IsNullOrWhiteSpace(operation.PlcName) || operation.PlcName.Length > 512))
        {
            errors.Add("'plcName' must be nonblank and at most 512 characters when supplied.");
        }
    }

    private static void Require(object? value, string member) => DomainPayloadProjector.RequireNotNull(value, member);
}

[McpServerToolType]
public class GovernanceReadTools
{
    public const string ToolName = "governance_read";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DomainReadResponse))]
    [Description("Read governance, security, and collaboration state offline (nothing is modified, no password is read or needed). Run up to 50 operations: list_portal_processes (running TIA Portal processes and their open projects, without attaching); read_umac (project users, custom and system roles with their engineering rights, UMC users and groups, function rights); read_safety (a fail-safe PLC's Safety administration: login and password-set flags, settings, program signatures, runtime groups; plcName optional with one PLC); list_test_suite (application test cases and sets, style-guide rule sets, system tests — nothing is executed); list_multiuser (project servers and local sessions); list_vci_workspaces (version-control workspaces). A product the project does not use fails capability_unavailable.")]
    public static Task<CallToolResult> GovernanceRead(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of governance_read operations. Each item is { operationId, operation, projectPath?, plcName? }.")] GovernanceReadOperationRequest[] operations)
        => DomainReadToolRunner.RunAsync(
            ToolName,
            workerClient,
            GovernanceReadCatalog.Instance,
            operations,
            async operation => GovernanceReadCatalog.Project(operation, await GovernanceReadCatalog.InvokeAsync(workerClient, operation).ConfigureAwait(false)),
            _ => $"Split the batch: re-run this operationId in its own {ToolName} call.");
}
