using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.DomainReads;

/// <summary>A tool-level failure that prevented a domain read batch from running at all.</summary>
public sealed record DomainToolError(string Category, string Message);

/// <summary>
/// Declared output schema of every structured domain read tool. Exactly one of
/// <see cref="Batch"/> and <see cref="Error"/> is populated: <see cref="Error"/> when validation or
/// access control rejected the call before any worker ran, otherwise <see cref="Batch"/>.
/// <see cref="Success"/> describes the whole call — a batch with failed items reports
/// <c>false</c> while remaining a successful MCP result.
/// </summary>
public sealed record DomainReadResponse(
    string Tool,
    bool Success,
    StructuredOperationBatch? Batch,
    DomainToolError? Error);

/// <summary>
/// The one execution path of a structured domain read tool: validate, check access, run every
/// operation independently in request order, bound the document, and render it once.
/// </summary>
public static class DomainReadToolRunner
{
    public static async Task<CallToolResult> RunAsync<TRequest>(
        string toolName,
        OpennessWorkerClient workerClient,
        DomainReadCatalog<TRequest> catalog,
        IReadOnlyList<TRequest>? operations,
        Func<TRequest, Task<StructuredOperationItem>> execute,
        Func<StructuredOperationItem, string> retryGuidance)
        where TRequest : class, IOperationBatchItem
    {
        var validation = catalog.Validate(operations);
        if (!validation.IsValid)
        {
            return Error(toolName, WorkerFailureCategories.ValidationError, validation.Error);
        }

        var mode = workerClient.AccessPolicy?.Mode ?? McpAccessMode.ReadWrite;
        var accessErrors = catalog.ValidateAccessMode(operations!, mode);
        if (accessErrors.Count != 0)
        {
            return Error(toolName, WorkerFailureCategories.AccessDenied, string.Join("\n", accessErrors));
        }

        var batch = await StructuredOperationBatchExecutionEngine
            .ExecuteReadsAsync(operations!, execute)
            .ConfigureAwait(false);

        // A batch that ran is a successful MCP call even when items inside it failed: isError is
        // reserved for "the tool could not run", so the caller can tell those two cases apart.
        return StructuredToolResult.Create(Compose(toolName, ApplyBudget(toolName, batch, retryGuidance)), isError: false);
    }

    internal static StructuredOperationBatch ApplyBudget(
        string toolName,
        StructuredOperationBatch batch,
        Func<StructuredOperationItem, string> retryGuidance,
        int maxItemChars = StructuredOperationBatchPayloadBudget.MaxItemChars,
        int maxDocumentChars = StructuredOperationBatchPayloadBudget.MaxDocumentChars)
        => StructuredOperationBatchPayloadBudget.Apply(
            batch,
            b => Compose(toolName, b),
            toolName,
            retryGuidance,
            maxItemChars,
            maxDocumentChars);

    internal static DomainReadResponse Compose(string toolName, StructuredOperationBatch batch)
        => new(toolName, batch.IsFullySuccessful, batch, Error: null);

    internal static CallToolResult Error(string toolName, string category, string message)
        => StructuredToolResult.Create(
            new DomainReadResponse(toolName, false, Batch: null, new DomainToolError(category, message)),
            isError: true);
}
