using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.PlcRead;

/// <summary>Dispatches validated <c>plc_read</c> operations to their worker methods.</summary>
public static class PlcReadWorkerInvoker
{
    public static Task<WorkerCallResult> InvokeAsync(OpennessWorkerClient client, PlcReadOperationRequest operation)
    {
        if (!PlcReadCatalog.Instance.TryGetSpec(operation.Operation, out _))
        {
            return Task.FromResult(WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                $"Unsupported plc_read operation '{operation.Operation}'."));
        }

        // Every plc_read worker method has the public operation name, and the catalog has already
        // rejected any field the operation does not declare, so forwarding each field is exact.
        return client.ReadDomainAsync(operation.Operation, operation.ProjectPath, request =>
        {
            request.PlcName = operation.PlcName;
            request.PlcObjectName = operation.Name;
            request.PlcGroupPath = operation.GroupPath?.ToList();
            request.PlcNameContains = operation.NameContains;
            request.PlcParameterNames = operation.ParameterNames?.ToList();
            request.PlcComparePlcName = operation.ComparePlcName;
            request.PlcIncludeIdentical = operation.IncludeIdentical;
            request.ObjectPageSize = operation.PageSize;
            request.ObjectCursor = operation.Cursor;
        });
    }
}
