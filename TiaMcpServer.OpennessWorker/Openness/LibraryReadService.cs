using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.LibraryRead;
using TiaMcpServer.OpennessWorker.ObjectModel;
using TiaMcpServer.OpennessWorker.PlcRead;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Siemens-facing <c>library_read</c> handlers for the method-based reads: the library update
/// check (<c>UpdateCheck</c>, which only reports) and the instance search of a type version
/// (<c>FindInstances</c>, scoped to one PLC). Listings stay in the Siemens-free
/// <see cref="LibraryReadBuilder"/>.
/// </summary>
internal static class LibraryReadService
{
    private const int MaxMessages = 10_000;

    public static LibraryUpdateCheckInfo CheckUpdates(Project project, TiaPortal? portal, WorkerRequest request)
    {
        var diagnostics = new List<string>();
        var library = LibraryReadBuilder.Select(Node(project), Node(portal), request.LibraryName, diagnostics);
        var includeUpToDate = request.LibraryIncludeUpToDate ?? false;
        var mode = includeUpToDate ? UpdateCheckMode.ReportOutOfDateAndUpToDate : UpdateCheckMode.ReportOutOfDateOnly;
        var result = ((EngineeringObjectNode)library.Node).EngineeringObject switch
        {
            ProjectLibrary projectLibrary => projectLibrary.UpdateCheck(project, mode),
            GlobalLibrary globalLibrary => globalLibrary.UpdateCheck(project, mode),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "The selected library cannot be checked."),
        };

        var messages = new List<LibraryUpdateMessageInfo>();
        foreach (UpdateCheckResultMessage message in result.Messages)
        {
            Flatten(message, 0, messages);
        }

        var page = ListPager.Page(
            messages,
            new[] { "library-update-check-v1", library.Kind, library.Name, includeUpToDate.ToString() },
            message => new[] { message.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture), message.Description },
            request.ObjectPageSize,
            request.ObjectCursor);
        return new LibraryUpdateCheckInfo
        {
            LibraryName = library.Name,
            LibraryKind = library.Kind,
            IncludeUpToDate = includeUpToDate,
            Messages = page.Items.ToList(),
            TotalCount = page.TotalCount,
            Offset = page.Offset,
            NextCursor = page.NextCursor,
        };
    }

    public static LibraryTypeInstancesInfo FindInstances(Project project, TiaPortal? portal, WorkerRequest request)
    {
        var typeName = string.IsNullOrWhiteSpace(request.PlcObjectName)
            ? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "PlcObjectName is required.")
            : request.PlcObjectName!;
        var diagnostics = new List<string>();
        var root = Node(project);
        var (library, type) = LibraryReadBuilder.SelectType(root, Node(portal), request.LibraryName, typeName, request.PlcGroupPath, diagnostics);
        var plc = PlcLocator.Select(root, request.PlcName, diagnostics);
        var software = (PlcSoftware)((EngineeringObjectNode)plc.Software).EngineeringObject;
        var libraryType = (LibraryType)((EngineeringObjectNode)type.Node).EngineeringObject;

        var result = new LibraryTypeInstancesInfo
        {
            LibraryName = library.Name,
            LibraryKind = library.Kind,
            TypeName = type.Info.Name,
            PlcName = plc.Summary.Name,
            Diagnostics = diagnostics,
        };

        var matchedVersion = false;
        foreach (LibraryTypeVersion version in libraryType.Versions)
        {
            var versionNumber = version.VersionNumber?.ToString();
            if (request.LibraryVersion is not null && !string.Equals(versionNumber, request.LibraryVersion, StringComparison.Ordinal))
            {
                continue;
            }

            matchedVersion = true;
            foreach (var instance in version.FindInstances(software))
            {
                result.Instances.Add(Instance(instance, versionNumber, software));
            }
        }

        if (request.LibraryVersion is not null && !matchedVersion)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound,
                $"Type '{type.Info.Name}' has no version '{request.LibraryVersion}'.");
        }

        return result;
    }

    private static TiaMcpServer.Contracts.LibraryTypeInstanceInfo Instance(
        Siemens.Engineering.Library.Types.LibraryTypeInstanceInfo info,
        string? versionNumber,
        PlcSoftware software)
    {
        var instance = info.LibraryTypeInstance;
        var node = new EngineeringObjectNode(instance);
        var groups = new List<string>();
        var current = instance.Parent as IEngineeringObject;
        for (var depth = 0; current is not null && !ReferenceEquals(current, software) && !current.Equals(software) && depth < 32; depth++)
        {
            var name = new EngineeringObjectNode(current).TryReadName();
            if (current is not PlcSoftware && name is not null)
            {
                groups.Insert(0, name);
            }

            current = current.Parent as IEngineeringObject;
        }

        return new TiaMcpServer.Contracts.LibraryTypeInstanceInfo
        {
            Name = node.TryReadName(),
            Kind = ObjectPathRules.SimpleName(node.TypeName),
            TypeName = node.TypeName,
            VersionNumber = versionNumber,
            GroupPath = groups,
        };
    }

    private static void Flatten(UpdateCheckResultMessage message, int depth, List<LibraryUpdateMessageInfo> output)
    {
        if (output.Count == MaxMessages)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.SnapshotTooLarge,
                $"The update check produced more than {MaxMessages} messages; report out-of-date types only.");
        }

        output.Add(new LibraryUpdateMessageInfo { Depth = depth, Description = message.Description });
        foreach (UpdateCheckResultMessage child in message.Messages)
        {
            Flatten(child, depth + 1, output);
        }
    }

    private static IObjectNode Node(Project project) => new EngineeringObjectNode(project);

    private static IObjectNode? Node(TiaPortal? portal) => portal is null ? null : new EngineeringObjectNode(portal);
}
