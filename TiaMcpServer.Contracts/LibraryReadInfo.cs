using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>Closed vocabulary of library kinds.</summary>
public static class LibraryKinds
{
    public const string Project = "project";
    public const string Global = "global";
}

/// <summary>Result of <c>list_libraries</c>.</summary>
public sealed class LibraryListInfo
{
    public List<LibrarySummaryInfo> Libraries { get; set; } = new List<LibrarySummaryInfo>();
    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class LibrarySummaryInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>One of <see cref="LibraryKinds"/>.</summary>
    public string Kind { get; set; } = LibraryKinds.Project;

    /// <summary>The <c>object_read</c> root that <see cref="ObjectPath"/> starts from.</summary>
    public string Root { get; set; } = ObjectRoots.Project;

    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();
    public Dictionary<string, object?> Values { get; set; } = new Dictionary<string, object?>();
}

/// <summary>Result of <c>list_library_types</c> and <c>list_master_copies</c>.</summary>
public sealed class LibraryObjectListInfo
{
    public string LibraryName { get; set; } = string.Empty;
    public string LibraryKind { get; set; } = LibraryKinds.Project;

    /// <summary>The <c>object_read</c> root every item's object path starts from.</summary>
    public string Root { get; set; } = ObjectRoots.Project;

    public List<DomainObjectInfo> Items { get; set; } = new List<DomainObjectInfo>();
    public int TotalCount { get; set; }
    public int Offset { get; set; }
    public string? NextCursor { get; set; }
    public List<string> Diagnostics { get; set; } = new List<string>();
}

/// <summary>Result of <c>read_library_type</c>.</summary>
public sealed class LibraryTypeInfo
{
    public string LibraryName { get; set; } = string.Empty;
    public string LibraryKind { get; set; } = LibraryKinds.Project;
    public string Root { get; set; } = ObjectRoots.Project;
    public DomainObjectInfo Type { get; set; } = new DomainObjectInfo();
    public List<LibraryTypeVersionInfo> Versions { get; set; } = new List<LibraryTypeVersionInfo>();
    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class LibraryTypeVersionInfo
{
    public List<ObjectPathSegmentInfo> ObjectPath { get; set; } = new List<ObjectPathSegmentInfo>();

    /// <summary><c>VersionNumber</c>, <c>State</c>, <c>IsDefault</c>, <c>Author</c>, <c>ModifiedDate</c>, <c>Guid</c>, <c>OriginalLibrary</c>.</summary>
    public Dictionary<string, object?> Values { get; set; } = new Dictionary<string, object?>();

    public List<string> Unavailable { get; set; } = new List<string>();

    /// <summary>Type versions this version depends on.</summary>
    public List<LibraryTypeVersionReferenceInfo> Dependencies { get; set; } = new List<LibraryTypeVersionReferenceInfo>();

    /// <summary>Type versions that depend on this version.</summary>
    public List<LibraryTypeVersionReferenceInfo> Dependents { get; set; } = new List<LibraryTypeVersionReferenceInfo>();
}

public sealed class LibraryTypeVersionReferenceInfo
{
    public string? TypeName { get; set; }
    public string? VersionNumber { get; set; }
}

/// <summary>Result of <c>check_library_updates</c>: the update-check message tree, flattened.</summary>
public sealed class LibraryUpdateCheckInfo
{
    public string LibraryName { get; set; } = string.Empty;
    public string LibraryKind { get; set; } = LibraryKinds.Project;
    public bool IncludeUpToDate { get; set; }
    public List<LibraryUpdateMessageInfo> Messages { get; set; } = new List<LibraryUpdateMessageInfo>();
    public int TotalCount { get; set; }
    public int Offset { get; set; }
    public string? NextCursor { get; set; }
}

public sealed class LibraryUpdateMessageInfo
{
    public int Depth { get; set; }
    public string? Description { get; set; }
}

/// <summary>Result of <c>find_type_instances</c>.</summary>
public sealed class LibraryTypeInstancesInfo
{
    public string LibraryName { get; set; } = string.Empty;
    public string LibraryKind { get; set; } = LibraryKinds.Project;
    public string TypeName { get; set; } = string.Empty;
    public string PlcName { get; set; } = string.Empty;
    public List<LibraryTypeInstanceInfo> Instances { get; set; } = new List<LibraryTypeInstanceInfo>();
    public List<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class LibraryTypeInstanceInfo
{
    public string? Name { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Version of the library type the instance is connected to.</summary>
    public string? VersionNumber { get; set; }

    /// <summary>Names of the instance's containing groups, outermost first, below the PLC software.</summary>
    public List<string> GroupPath { get; set; } = new List<string>();
}
