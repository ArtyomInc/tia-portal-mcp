using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.ObjectModel;

namespace TiaMcpServer.OpennessWorker.LibraryRead;

/// <summary>A selected library with the root and path that address it.</summary>
public sealed class LocatedLibrary
{
    public LocatedLibrary(IObjectNode node, string name, string kind, string root, List<ObjectPathSegmentInfo> path)
    {
        Node = node;
        Name = name;
        Kind = kind;
        Root = root;
        Path = path;
    }

    public IObjectNode Node { get; }
    public string Name { get; }
    public string Kind { get; }
    public string Root { get; }
    public List<ObjectPathSegmentInfo> Path { get; }
}

/// <summary>
/// Siemens-free builders of the <c>library_read</c> results. Libraries are the project library
/// (root <c>project</c>) and the global libraries already open in the Portal (root
/// <c>portal</c>); nothing is opened, updated, or modified.
/// </summary>
public static class LibraryReadBuilder
{
    private static readonly string[] GlobalLibraryAttributes =
    {
        "Author", "Version", "Family", "Copyright", "IsReadOnly", "IsWriteProtected", "IsModified",
        "CreationTime", "LastModified", "LastModifiedBy", "Path", "Size",
    };

    private static readonly string[] TypeAttributes =
    {
        "Author", "Guid", "Status", "DoNotUse", "SetForUpdate", "Namespace", "MinimumTargetDeviceVersion",
    };

    private static readonly string[] VersionAttributes =
    {
        "VersionNumber", "State", "IsDefault", "Author", "ModifiedDate", "Guid", "OriginalLibrary",
    };

    public static readonly GroupTreeSpec TypeTree = new(new[] { "Types" }, new[] { "Folders" }, TypeAttributes);

    public static readonly GroupTreeSpec MasterCopyTree = new(new[] { "MasterCopies" }, new[] { "Folders" }, attributes: null);

    public static LibraryListInfo ListLibraries(IObjectNode project, IObjectNode? portal)
    {
        var diagnostics = new List<string>();
        var result = new LibraryListInfo { Diagnostics = diagnostics };
        foreach (var library in All(project, portal, diagnostics))
        {
            var unavailable = new List<string>();
            result.Libraries.Add(new LibrarySummaryInfo
            {
                Name = library.Name,
                Kind = library.Kind,
                Root = library.Root,
                ObjectPath = library.Path,
                Values = library.Kind == LibraryKinds.Global
                    ? ObjectScalarNormalizer.ReadValues(library.Node, GlobalLibraryAttributes, unavailable)
                    : new Dictionary<string, object?>(StringComparer.Ordinal),
            });
        }

        return result;
    }

    /// <summary>Null selects the project library; a name selects exactly one open global library.</summary>
    public static LocatedLibrary Select(IObjectNode project, IObjectNode? portal, string? libraryName, List<string> diagnostics)
    {
        var all = All(project, portal, diagnostics);
        if (libraryName is null)
        {
            return all.FirstOrDefault(library => library.Kind == LibraryKinds.Project)
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, "The project library could not be read.");
        }

        var matches = all
            .Where(library => library.Kind == LibraryKinds.Global && string.Equals(library.Name, libraryName, StringComparison.Ordinal))
            .ToList();
        var open = string.Join(", ", all.Where(l => l.Kind == LibraryKinds.Global).Select(l => $"'{l.Name}'"));
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound,
                $"No open global library is named '{libraryName}'. Open global libraries: {(open.Length == 0 ? "none" : open)}. "
                    + "Omit libraryName for the project library; libraries are never opened by this tool."),
            _ => throw new WorkerOperationException(
                WorkerFailureCategories.TargetAmbiguous,
                $"Several open global libraries are named '{libraryName}'."),
        };
    }

    public static LibraryObjectListInfo ListObjects(
        IObjectNode project,
        IObjectNode? portal,
        string operation,
        string? libraryName,
        int? pageSize,
        string? cursor)
    {
        var diagnostics = new List<string>();
        var library = Select(project, portal, libraryName, diagnostics);
        var (folder, tree) = operation == "list_master_copies"
            ? ("MasterCopyFolder", MasterCopyTree)
            : ("TypeFolder", TypeTree);
        var items = Items(library, folder, tree, diagnostics, null);
        var page = ListPager.Page(
            items.Select(item => item.Info).ToList(),
            new[] { "library-list-v1", operation, library.Kind, library.Name },
            item => item.GroupPath.Concat(new[] { "/", item.Name, item.TypeName }),
            pageSize,
            cursor);
        return new LibraryObjectListInfo
        {
            LibraryName = library.Name,
            LibraryKind = library.Kind,
            Root = library.Root,
            Items = page.Items.ToList(),
            TotalCount = page.TotalCount,
            Offset = page.Offset,
            NextCursor = page.NextCursor,
            Diagnostics = diagnostics,
        };
    }

    public static (LocatedLibrary Library, GroupTreeItem Type) SelectType(
        IObjectNode project,
        IObjectNode? portal,
        string? libraryName,
        string typeName,
        IReadOnlyList<string>? folderPath,
        List<string> diagnostics)
    {
        var library = Select(project, portal, libraryName, diagnostics);
        var matches = Items(library, "TypeFolder", TypeTree, diagnostics, typeName)
            .Where(item => folderPath is null || item.Info.GroupPath.SequenceEqual(folderPath, StringComparer.Ordinal))
            .ToList();
        return matches.Count switch
        {
            1 => (library, matches[0]),
            0 => throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound,
                $"Library '{library.Name}' has no type named '{typeName}'"
                    + (folderPath is null ? "." : $" in folder '{string.Join("/", folderPath)}'.")),
            _ => throw new WorkerOperationException(
                WorkerFailureCategories.TargetAmbiguous,
                $"Library '{library.Name}' has several types named '{typeName}'; add folderPath."),
        };
    }

    public static LibraryTypeInfo ReadType(
        IObjectNode project,
        IObjectNode? portal,
        string? libraryName,
        string typeName,
        IReadOnlyList<string>? folderPath)
    {
        var diagnostics = new List<string>();
        var (library, type) = SelectType(project, portal, libraryName, typeName, folderPath, diagnostics);
        var result = new LibraryTypeInfo
        {
            LibraryName = library.Name,
            LibraryKind = library.Kind,
            Root = library.Root,
            Type = type.Info,
            Diagnostics = diagnostics,
        };

        var versions = DeviceWalker.Elements(type.Node, "Versions", diagnostics);
        for (var index = 0; index < versions.Count; index++)
        {
            var version = versions[index];
            var unavailable = new List<string>();
            result.Versions.Add(new LibraryTypeVersionInfo
            {
                ObjectPath = DeviceWalker.Append(type.Info.ObjectPath, "Versions", index, null),
                Values = ObjectScalarNormalizer.ReadValues(version, VersionAttributes, unavailable),
                Unavailable = unavailable,
                Dependencies = References(version, "Dependencies", diagnostics),
                Dependents = References(version, "Dependents", diagnostics),
            });
        }

        return result;
    }

    /// <summary>Type name and version number of each version in an association.</summary>
    public static List<LibraryTypeVersionReferenceInfo> References(IObjectNode version, string association, List<string> diagnostics)
    {
        var list = version.ReadObjectList(association);
        if (list is null)
        {
            var read = version.ReadValue(association);
            if (read.IsDeclared && !read.Succeeded)
            {
                diagnostics.Add($"'{association}' of a type version could not be read.");
            }

            return new List<LibraryTypeVersionReferenceInfo>();
        }

        return list.Select(reference => new LibraryTypeVersionReferenceInfo
        {
            TypeName = reference.FollowAttribute("TypeObject").Node?.TryReadName(),
            VersionNumber = Text(reference, "VersionNumber"),
        }).ToList();
    }

    public static string? Text(IObjectNode node, string name)
    {
        var read = node.ReadValue(name);
        return read.Succeeded && ObjectScalarNormalizer.TryNormalize(read.Value, out var json) ? json as string : null;
    }

    private static List<GroupTreeItem> Items(
        LocatedLibrary library,
        string folder,
        GroupTreeSpec tree,
        List<string> diagnostics,
        string? exactName)
    {
        var root = library.Node.FollowAttribute(folder).Node
            ?? throw new WorkerOperationException(
                WorkerFailureCategories.CapabilityUnavailable,
                $"Library '{library.Name}' exposes no {folder}.");
        var rootPath = library.Path.Select(ObjectChildrenPager.Copy).ToList();
        rootPath.Add(new ObjectPathSegmentInfo { Kind = ObjectPathSegmentKinds.Attribute, Name = folder });
        Func<IObjectNode, bool>? filter = exactName is null
            ? null
            : node => string.Equals(node.TryReadName(), exactName, StringComparison.Ordinal);
        return GroupTreeLister.List(root, rootPath, tree, diagnostics, filter);
    }

    private static List<LocatedLibrary> All(IObjectNode project, IObjectNode? portal, List<string> diagnostics)
    {
        var libraries = new List<LocatedLibrary>();
        var projectLibrary = project.FollowAttribute("ProjectLibrary").Node;
        if (projectLibrary is not null)
        {
            libraries.Add(new LocatedLibrary(
                projectLibrary,
                project.TryReadName() ?? "Project library",
                LibraryKinds.Project,
                ObjectRoots.Project,
                new List<ObjectPathSegmentInfo> { new() { Kind = ObjectPathSegmentKinds.Attribute, Name = "ProjectLibrary" } }));
        }
        else
        {
            diagnostics.Add("The project library could not be read.");
        }

        if (portal is null)
        {
            diagnostics.Add("The Portal is not available; global libraries are not listed.");
            return libraries;
        }

        var globals = DeviceWalker.Elements(portal, "GlobalLibraries", diagnostics);
        for (var index = 0; index < globals.Count; index++)
        {
            var name = globals[index].TryReadName() ?? string.Empty;
            libraries.Add(new LocatedLibrary(
                globals[index],
                name,
                LibraryKinds.Global,
                ObjectRoots.Portal,
                DeviceWalker.Append(Array.Empty<ObjectPathSegmentInfo>(), "GlobalLibraries", index, name)));
        }

        return libraries;
    }
}
