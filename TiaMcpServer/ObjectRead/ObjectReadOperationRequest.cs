using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.ObjectRead;

/// <summary>
/// Strict request shape of one generic <c>object_read</c> operation. Only the fields declared for
/// the selected operation are accepted by <see cref="ObjectReadCatalog"/>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ObjectReadOperationRequest : IOperationBatchItem
{
    [Description("Client-supplied unique identifier for this operation; returned results are keyed by it.")]
    public string OperationId { get; set; } = string.Empty;

    [Description("Operation to run: describe_object, list_object_children, read_object_attributes, export_object, or list_capabilities.")]
    public string Operation { get; set; } = string.Empty;

    [Description("Optional absolute project path (.ap21). When omitted, the active project is used.")]
    public string? ProjectPath { get; set; }

    [Description("Where objectPath starts: 'project' (default, the bound project) or 'portal' (the attached TIA Portal; its Projects composition is not readable). Not valid for list_capabilities.")]
    public string? Root { get; set; }

    [Description("Ordered path from the root to the object; omit or send [] for the root itself. Copy paths from list_object_children results rather than building them by hand. Not valid for list_capabilities.")]
    public IReadOnlyList<ObjectPathSegment>? ObjectPath { get; set; }

    [Description("read_object_attributes only: 1-200 unique attribute names to read. When omitted, every declared attribute is read.")]
    public IReadOnlyList<string>? AttributeNames { get; set; }

    [Description("list_object_children only: 1-50 unique composition names to list. When omitted, every composition is listed.")]
    public IReadOnlyList<string>? CompositionNames { get; set; }

    [Description("list_object_children only: children per page, 1-200 (default 50).")]
    public int? PageSize { get; set; }

    [Description("list_object_children only: opaque cursor from a previous page's nextCursor. Resend the same root, objectPath, and compositionNames.")]
    public string? Cursor { get; set; }

    [Description("export_object only: unique SimaticML export options, from withDefaults and withReadOnly. Omit for none.")]
    public IReadOnlyList<string>? ExportOptions { get; set; }

    [Description("export_object only: character offset of the window to return (default 0). Use the previous result's nextOffset to continue.")]
    public int? Offset { get; set; }

    [Description("export_object only: window length in characters, 1-30000 (default 16000).")]
    public int? MaxChars { get; set; }
}

/// <summary>One step of an <c>objectPath</c>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ObjectPathSegment
{
    [Description("Step kind: 'composition' (an element of a composition), 'attribute' (an object-valued attribute), or 'service' (a service of the object).")]
    public string? Kind { get; set; }

    [Description("Composition, attribute, or service name as reported by describe_object. A service may be named by its simple or full type name.")]
    public string? Name { get; set; }

    [Description("composition steps only: the element's Name. With index, it is evidence the element at that index must still carry.")]
    public string? ElementName { get; set; }

    [Description("composition steps only: zero-based element position.")]
    public int? Index { get; set; }
}

internal static class ObjectPathSegmentMapper
{
    public static List<ObjectPathSegmentInfo>? Map(IReadOnlyList<ObjectPathSegment>? path)
        => path?.Select(segment => new ObjectPathSegmentInfo
        {
            Kind = segment.Kind ?? string.Empty,
            Name = segment.Name ?? string.Empty,
            ElementName = segment.ElementName,
            Index = segment.Index,
        }).ToList();
}
