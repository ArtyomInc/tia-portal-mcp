using Siemens.Engineering;
using Siemens.Engineering.Compare;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.ObjectModel;
using TiaMcpServer.OpennessWorker.PlcRead;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Siemens-facing <c>plc_read</c> handlers. Listings, entries, and checksums are delegated to the
/// Siemens-free <see cref="PlcReadBuilder"/>; only the method-based reads — block fingerprints and
/// offline software comparison — need typed Openness calls here.
/// </summary>
internal static class PlcReadService
{
    private const int MaxCompareElements = 10_000;

    private static readonly HashSet<CompareResultState> IdenticalStates = new()
    {
        CompareResultState.ObjectsIdentical,
        CompareResultState.FolderContentsIdentical,
    };

    public static IObjectNode Root(Project project) => new EngineeringObjectNode(project);

    public static PlcFingerprintsInfo ReadBlockFingerprints(Project project, WorkerRequest request)
    {
        var diagnostics = new List<string>();
        var plc = PlcLocator.Select(Root(project), request.PlcName, diagnostics);
        var block = PlcReadBuilder.SelectOne(
            plc,
            PlcReadBuilder.Listings["list_blocks"],
            RequireName(request),
            request.PlcGroupPath,
            "block",
            diagnostics);
        if (((EngineeringObjectNode)block.Node).EngineeringObject is not PlcBlock plcBlock)
        {
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "The selected object is not a PLC block.");
        }

        var provider = plcBlock.GetService<FingerprintProvider>()
            ?? throw new WorkerOperationException(
                WorkerFailureCategories.CapabilityUnavailable,
                $"Block '{block.Info.Name}' offers no fingerprint provider.");
        return new PlcFingerprintsInfo
        {
            PlcName = plc.Summary.Name,
            Target = block.Info,
            Fingerprints = provider.GetFingerprints()
                .Select(fingerprint => new PlcFingerprintInfo { Id = fingerprint.Id.ToString(), Value = fingerprint.Value })
                .OrderBy(fingerprint => fingerprint.Id, StringComparer.Ordinal)
                .ToList(),
        };
    }

    public static PlcCompareInfo CompareSoftware(Project project, WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlcComparePlcName))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "PlcComparePlcName is required.");
        }

        var diagnostics = new List<string>();
        var root = Root(project);
        var left = PlcLocator.Select(root, request.PlcName, diagnostics);
        var right = PlcLocator.Select(root, request.PlcComparePlcName, diagnostics);
        var leftSoftware = (PlcSoftware)((EngineeringObjectNode)left.Software).EngineeringObject;
        var rightSoftware = (PlcSoftware)((EngineeringObjectNode)right.Software).EngineeringObject;

        var includeIdentical = request.PlcIncludeIdentical ?? false;
        var elements = new List<PlcCompareElementInfo>();
        var result = leftSoftware.CompareTo(rightSoftware);
        Flatten(result.RootElement, new List<string>(), 0, includeIdentical, elements);

        var page = ListPager.Page(
            elements,
            new[] { "plc-compare-v1", left.Summary.Name, right.Summary.Name, includeIdentical.ToString() },
            element => element.Path.Concat(new[] { "|", element.State, element.Detail }),
            request.ObjectPageSize,
            request.ObjectCursor);
        return new PlcCompareInfo
        {
            LeftPlcName = left.Summary.Name,
            RightPlcName = right.Summary.Name,
            IncludeIdentical = includeIdentical,
            Elements = page.Items.ToList(),
            TotalCount = page.TotalCount,
            Offset = page.Offset,
            NextCursor = page.NextCursor,
        };
    }

    private static void Flatten(
        CompareResultElement? element,
        List<string> path,
        int depth,
        bool includeIdentical,
        List<PlcCompareElementInfo> output)
    {
        if (element is null)
        {
            return;
        }

        var state = element.ComparisonResult;
        if (includeIdentical || !IdenticalStates.Contains(state))
        {
            if (output.Count == MaxCompareElements)
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.SnapshotTooLarge,
                    $"The comparison holds more than {MaxCompareElements} elements; compare with includeIdentical false.");
            }

            output.Add(new PlcCompareElementInfo
            {
                Path = path.ToList(),
                Depth = depth,
                LeftName = element.LeftName,
                RightName = element.RightName,
                State = state.ToString(),
                Detail = string.IsNullOrEmpty(element.DetailedInformation) ? null : element.DetailedInformation,
            });
        }

        // Identical folders contain only identical elements, so they are not descended into
        // unless the caller asked for identical elements.
        if (!includeIdentical && state == CompareResultState.FolderContentsIdentical)
        {
            return;
        }

        foreach (CompareResultElement child in element.Elements)
        {
            Flatten(child, path.Append(child.LeftName ?? child.RightName ?? string.Empty).ToList(), depth + 1, includeIdentical, output);
        }
    }

    private static string RequireName(WorkerRequest request)
        => string.IsNullOrWhiteSpace(request.PlcObjectName)
            ? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "PlcObjectName is required.")
            : request.PlcObjectName!;
}
