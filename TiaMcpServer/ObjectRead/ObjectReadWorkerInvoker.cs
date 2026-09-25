using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.ObjectRead;

/// <summary>Dispatches validated <c>object_read</c> operations to their worker methods.</summary>
public static class ObjectReadWorkerInvoker
{
    public static Task<WorkerCallResult> InvokeAsync(OpennessWorkerClient client, ObjectReadOperationRequest operation)
        => operation.Operation switch
        {
            "describe_object" => client.ReadDomainAsync(operation.Operation, operation.ProjectPath, request => MapPath(request, operation)),
            "list_object_children" => client.ReadDomainAsync(operation.Operation, operation.ProjectPath, request =>
            {
                MapPath(request, operation);
                request.ObjectCompositionNames = operation.CompositionNames?.ToList();
                request.ObjectPageSize = operation.PageSize;
                request.ObjectCursor = operation.Cursor;
            }),
            "read_object_attributes" => client.ReadDomainAsync(operation.Operation, operation.ProjectPath, request =>
            {
                MapPath(request, operation);
                request.ObjectAttributeNames = operation.AttributeNames?.ToList();
            }),
            "export_object" => client.ReadDomainAsync(operation.Operation, operation.ProjectPath, request =>
            {
                MapPath(request, operation);
                request.ObjectExportOptions = operation.ExportOptions?.ToList();
                request.ObjectExportOffset = operation.Offset;
                request.ObjectExportMaxChars = operation.MaxChars;
            }),
            "list_capabilities" => client.ReadDomainAsync(operation.Operation, operation.ProjectPath, _ => { }),
            _ => Task.FromResult(WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                $"Unsupported object_read operation '{operation.Operation}'.")),
        };

    private static void MapPath(WorkerRequest request, ObjectReadOperationRequest operation)
    {
        request.ObjectRoot = operation.Root;
        request.ObjectPath = ObjectPathSegmentMapper.Map(operation.ObjectPath);
    }
}
