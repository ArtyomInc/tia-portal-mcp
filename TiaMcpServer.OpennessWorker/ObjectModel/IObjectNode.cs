using System.Collections.Generic;

namespace TiaMcpServer.OpennessWorker.ObjectModel;

/// <summary>
/// Siemens-free view of one Openness engineering object, limited to the read-only generic
/// surface: declared compositions, attributes, and services, and navigation along them.
///
/// <para>
/// The production adapter wraps <c>IEngineeringObject</c>; tests use an in-memory graph. Nothing
/// here can mutate: there is no Invoke, Create, SetAttribute, or Delete counterpart.
/// </para>
/// </summary>
public interface IObjectNode
{
    /// <summary>Full CLR type name of the underlying object.</summary>
    string TypeName { get; }

    /// <summary>True when the object's type declares a public <c>Export(FileInfo, ExportOptions)</c>.</summary>
    bool IsExportable { get; }

    /// <summary>The object's <c>Name</c> attribute, or null when undeclared or unreadable.</summary>
    string? TryReadName();

    /// <summary>Declared compositions (name and element type), in declaration order.</summary>
    IReadOnlyList<ObjectMemberDescriptor> GetCompositions();

    /// <summary>
    /// Elements of the named composition in enumeration order, stopping after
    /// <paramref name="limit"/> + 1 elements so a caller can detect an oversized composition
    /// without enumerating it completely.
    /// </summary>
    IReadOnlyList<IObjectNode> GetCompositionElements(string compositionName, int limit);

    /// <summary>Declared attributes (name, access, supported types), in declaration order.</summary>
    IReadOnlyList<ObjectAttributeDescriptor> GetAttributes();

    /// <summary>
    /// Follows an object-valued attribute. Returns <see cref="ObjectFollowResult.Null"/> for a null
    /// value and <see cref="ObjectFollowResult.NotAnObject"/> for a scalar.
    /// </summary>
    ObjectFollowResult FollowAttribute(string attributeName);

    /// <summary>Declared services (full type names), in declaration order.</summary>
    IReadOnlyList<ObjectMemberDescriptor> GetServices();

    /// <summary>The service of the given full type name, or null when the object returns none.</summary>
    IObjectNode? GetService(string serviceTypeFullName);
}

/// <summary>A declared composition or service.</summary>
public sealed class ObjectMemberDescriptor
{
    public ObjectMemberDescriptor(string name, string? typeName)
    {
        Name = name;
        TypeName = typeName;
    }

    /// <summary>Composition name, or the service's full type name.</summary>
    public string Name { get; }

    public string? TypeName { get; }
}

/// <summary>A declared attribute.</summary>
public sealed class ObjectAttributeDescriptor
{
    public ObjectAttributeDescriptor(string name, string access, IReadOnlyList<string> supportedTypes, bool navigable)
    {
        Name = name;
        Access = access;
        SupportedTypes = supportedTypes;
        Navigable = navigable;
    }

    public string Name { get; }

    /// <summary>One of <c>none</c>, <c>readOnly</c>, <c>writeOnly</c>, <c>readWrite</c>, <c>unknown</c>.</summary>
    public string Access { get; }

    public IReadOnlyList<string> SupportedTypes { get; }

    /// <summary>True when a supported type is an engineering object an attribute step can follow.</summary>
    public bool Navigable { get; }
}

/// <summary>Outcome of <see cref="IObjectNode.FollowAttribute"/>.</summary>
public sealed class ObjectFollowResult
{
    private ObjectFollowResult(IObjectNode? node, string? valueTypeName)
    {
        Node = node;
        ValueTypeName = valueTypeName;
    }

    public IObjectNode? Node { get; }

    /// <summary>CLR type of a non-object value, for diagnostics.</summary>
    public string? ValueTypeName { get; }

    public bool IsNull => Node is null && ValueTypeName is null;

    public static ObjectFollowResult Null { get; } = new ObjectFollowResult(null, null);

    public static ObjectFollowResult Object(IObjectNode node) => new ObjectFollowResult(node, null);

    public static ObjectFollowResult NotAnObject(string valueTypeName) => new ObjectFollowResult(null, valueTypeName);
}
