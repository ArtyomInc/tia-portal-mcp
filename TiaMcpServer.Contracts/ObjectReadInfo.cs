using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>Result of <c>describe_object</c>: what the resolved object declares.</summary>
public sealed class ObjectDescriptionInfo
{
    public string Root { get; set; } = ObjectRoots.Project;
    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();
    public string TypeName { get; set; } = string.Empty;

    /// <summary>The object's <c>Name</c> attribute when it declares a readable one.</summary>
    public string? Name { get; set; }

    public List<ObjectCompositionDescriptorInfo> Compositions { get; set; } = new List<ObjectCompositionDescriptorInfo>();
    public List<ObjectAttributeDescriptorInfo> Attributes { get; set; } = new List<ObjectAttributeDescriptorInfo>();
    public List<ObjectServiceDescriptorInfo> Services { get; set; } = new List<ObjectServiceDescriptorInfo>();

    /// <summary>True when the object's type declares a public <c>Export(FileInfo, ExportOptions)</c>.</summary>
    public bool Exportable { get; set; }

    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class ObjectCompositionDescriptorInfo
{
    public string Name { get; set; } = string.Empty;
    public string? TypeName { get; set; }
}

public sealed class ObjectAttributeDescriptorInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>One of <c>none</c>, <c>readOnly</c>, <c>writeOnly</c>, <c>readWrite</c>, <c>unknown</c>.</summary>
    public string Access { get; set; } = string.Empty;

    public List<string> SupportedTypes { get; set; } = new List<string>();

    /// <summary>True when a supported type is an Openness engineering object an <c>attribute</c> step can follow.</summary>
    public bool Navigable { get; set; }
}

public sealed class ObjectServiceDescriptorInfo
{
    /// <summary>Simple type name, usable as a <c>service</c> step name.</summary>
    public string Name { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    /// <summary>False for services the generic reader refuses (online, download, upload).</summary>
    public bool Allowed { get; set; }
}

/// <summary>Result of <c>list_object_children</c>: one page of composition elements.</summary>
public sealed class ObjectChildrenPageInfo
{
    public string Root { get; set; } = ObjectRoots.Project;
    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();
    public List<ObjectChildInfo> Children { get; set; } = new List<ObjectChildInfo>();

    /// <summary>Total children across the selected compositions (all pages).</summary>
    public int TotalCount { get; set; }

    /// <summary>Zero-based position of the first child of this page.</summary>
    public int Offset { get; set; }

    /// <summary>Opaque continuation, or null on the last page.</summary>
    public string? NextCursor { get; set; }

    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class ObjectChildInfo
{
    public string Composition { get; set; } = string.Empty;
    public int Index { get; set; }
    public string? Name { get; set; }
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Complete path to this child, ready to send back as <c>objectPath</c>.</summary>
    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();
}

/// <summary>Result of <c>read_object_attributes</c>.</summary>
public sealed class ObjectAttributesInfo
{
    public string Root { get; set; } = ObjectRoots.Project;
    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();
    public string TypeName { get; set; } = string.Empty;
    public List<NetworkAttributeInfo> Attributes { get; set; } = new List<NetworkAttributeInfo>();
    public List<string> Diagnostics { get; set; } = new List<string>();
}

/// <summary>Result of <c>export_object</c>: one character window of a SimaticML document.</summary>
public sealed class ObjectExportInfo
{
    public string Root { get; set; } = ObjectRoots.Project;
    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();
    public string TypeName { get; set; } = string.Empty;
    public List<string> ExportOptions { get; set; } = new List<string>();

    /// <summary>Length of the whole document in UTF-16 characters.</summary>
    public int TotalChars { get; set; }

    /// <summary>Lowercase hex SHA-256 of the whole document's UTF-8 bytes.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public int Offset { get; set; }
    public string Content { get; set; } = string.Empty;

    /// <summary>Offset of the next window, or null when this window ends the document.</summary>
    public int? NextOffset { get; set; }
}

/// <summary>Result of <c>list_capabilities</c>.</summary>
public sealed class OpennessCapabilitiesInfo
{
    public List<InstalledProductInfo> Products { get; set; } = new List<InstalledProductInfo>();
    public List<OpennessAssemblyAvailabilityInfo> Assemblies { get; set; } = new List<OpennessAssemblyAvailabilityInfo>();
    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class InstalledProductInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public List<string> Options { get; set; } = new List<string>();
}

public sealed class OpennessAssemblyAvailabilityInfo
{
    public string Name { get; set; } = string.Empty;
    public bool Available { get; set; }
    public string? Version { get; set; }
}
