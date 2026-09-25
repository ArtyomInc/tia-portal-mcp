using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.DomainReads;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.ObjectRead;

/// <summary>
/// The only decoder of <c>object_read</c> worker success payloads. Each operation declares exactly
/// one result type; anything else is rejected as <c>protocol_error</c> without being echoed.
/// </summary>
public static class ObjectReadPayloadContract
{
    private static readonly HashSet<string> AttributeAccess = new(StringComparer.Ordinal)
    {
        "none", "readOnly", "writeOnly", "readWrite", "unknown",
    };

    public static StructuredOperationItem Project(ObjectReadOperationRequest operation, WorkerCallResult workerResult)
        => DomainPayloadProjector.Project(operation, workerResult, payload => Decode(operation.Operation, payload));

    public static JsonElement Decode(string operation, string payload) => operation switch
    {
        "describe_object" => DomainPayloadProjector.Decode<ObjectDescriptionInfo>(payload, ValidateDescription),
        "list_object_children" => DomainPayloadProjector.Decode<ObjectChildrenPageInfo>(payload, ValidateChildren),
        "read_object_attributes" => DomainPayloadProjector.Decode<ObjectAttributesInfo>(payload, ValidateAttributes),
        "export_object" => DomainPayloadProjector.Decode<ObjectExportInfo>(payload, ValidateExport),
        "list_capabilities" => DomainPayloadProjector.Decode<OpennessCapabilitiesInfo>(payload, ValidateCapabilities),
        _ => throw new JsonException($"No declared result contract for object_read operation '{operation}'."),
    };

    private static void ValidatePath(string root, List<ObjectPathSegmentInfo> path, string member)
    {
        if (!ObjectRoots.IsKnown(root))
        {
            throw new JsonException($"'{member}.root' is not a known root.");
        }

        DomainPayloadProjector.RequireNotNull(path, member + ".objectPath");
        foreach (var segment in path)
        {
            DomainPayloadProjector.RequireNotNull(segment, member + ".objectPath[]");
            if (!ObjectPathSegmentKinds.IsKnown(segment.Kind) || string.IsNullOrEmpty(segment.Name))
            {
                throw new JsonException($"'{member}.objectPath[]' is not a valid segment.");
            }
        }
    }

    private static void ValidateDescription(ObjectDescriptionInfo value)
    {
        ValidatePath(value.Root, value.ObjectPath, "description");
        DomainPayloadProjector.RequireNotNull(value.TypeName, "typeName");
        DomainPayloadProjector.RequireNotNull(value.Compositions, "compositions");
        DomainPayloadProjector.RequireNotNull(value.Attributes, "attributes");
        DomainPayloadProjector.RequireNotNull(value.Services, "services");
        DomainPayloadProjector.RequireNotNull(value.Diagnostics, "diagnostics");
        foreach (var composition in value.Compositions)
        {
            DomainPayloadProjector.RequireNotNull(composition?.Name, "compositions[].name");
        }

        foreach (var attribute in value.Attributes)
        {
            DomainPayloadProjector.RequireNotNull(attribute?.Name, "attributes[].name");
            DomainPayloadProjector.RequireNotNull(attribute!.SupportedTypes, "attributes[].supportedTypes");
            if (!AttributeAccess.Contains(attribute.Access))
            {
                throw new JsonException("'attributes[].access' is not a known access value.");
            }
        }

        foreach (var service in value.Services)
        {
            DomainPayloadProjector.RequireNotNull(service?.Name, "services[].name");
            DomainPayloadProjector.RequireNotNull(service!.TypeName, "services[].typeName");
        }
    }

    private static void ValidateChildren(ObjectChildrenPageInfo value)
    {
        ValidatePath(value.Root, value.ObjectPath, "page");
        DomainPayloadProjector.RequireNotNull(value.Children, "children");
        DomainPayloadProjector.RequireNotNull(value.Diagnostics, "diagnostics");
        if (value.TotalCount < 0 || value.Offset < 0 || value.Offset + value.Children.Count > value.TotalCount)
        {
            throw new JsonException("The page counts are inconsistent.");
        }

        if ((value.NextCursor is null) != (value.Offset + value.Children.Count >= value.TotalCount))
        {
            throw new JsonException("'nextCursor' does not match the page position.");
        }

        foreach (var child in value.Children)
        {
            DomainPayloadProjector.RequireNotNull(child, "children[]");
            DomainPayloadProjector.RequireNotNull(child.Composition, "children[].composition");
            DomainPayloadProjector.RequireNotNull(child.TypeName, "children[].typeName");
            ValidatePath(value.Root, child.ObjectPath, "children[]");
            if (child.ObjectPath.Count != value.ObjectPath.Count + 1)
            {
                throw new JsonException("'children[].objectPath' must extend the listed object's path by one step.");
            }
        }
    }

    private static void ValidateAttributes(ObjectAttributesInfo value)
    {
        ValidatePath(value.Root, value.ObjectPath, "attributes");
        DomainPayloadProjector.RequireNotNull(value.TypeName, "typeName");
        DomainPayloadProjector.RequireNotNull(value.Attributes, "attributes");
        DomainPayloadProjector.RequireNotNull(value.Diagnostics, "diagnostics");
        foreach (var attribute in value.Attributes)
        {
            DomainPayloadProjector.RequireNotNull(attribute, "attributes[]");
            DomainPayloadProjector.RequireNotNull(attribute.Name, "attributes[].name");
            DomainPayloadProjector.RequireNotNull(attribute.SupportedTypes, "attributes[].supportedTypes");
            if (string.Equals(attribute.Availability, "available", StringComparison.Ordinal) && attribute.Value is null)
            {
                throw new JsonException("An available attribute must carry a typed value.");
            }
        }
    }

    private static void ValidateExport(ObjectExportInfo value)
    {
        ValidatePath(value.Root, value.ObjectPath, "export");
        DomainPayloadProjector.RequireNotNull(value.TypeName, "typeName");
        DomainPayloadProjector.RequireNotNull(value.Content, "content");
        DomainPayloadProjector.RequireNotNull(value.ExportOptions, "exportOptions");
        if (value.Sha256 is null || value.Sha256.Length != 64)
        {
            throw new JsonException("'sha256' must be a SHA-256 hex digest.");
        }

        var end = value.Offset + value.Content.Length;
        if (value.Offset < 0 || value.TotalChars < 0 || end > value.TotalChars
            || value.Content.Length > ObjectReadLimits.MaxExportMaxChars
            || (value.NextOffset is null ? end != value.TotalChars : value.NextOffset != end))
        {
            throw new JsonException("The export window is inconsistent.");
        }
    }

    private static void ValidateCapabilities(OpennessCapabilitiesInfo value)
    {
        DomainPayloadProjector.RequireNotNull(value.Products, "products");
        DomainPayloadProjector.RequireNotNull(value.Assemblies, "assemblies");
        DomainPayloadProjector.RequireNotNull(value.Diagnostics, "diagnostics");
        foreach (var product in value.Products)
        {
            DomainPayloadProjector.RequireNotNull(product?.Name, "products[].name");
            DomainPayloadProjector.RequireNotNull(product!.Options, "products[].options");
        }

        foreach (var assembly in value.Assemblies)
        {
            DomainPayloadProjector.RequireNotNull(assembly?.Name, "assemblies[].name");
        }
    }
}
