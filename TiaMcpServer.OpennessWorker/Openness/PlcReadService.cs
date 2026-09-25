using Siemens.Engineering;
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
        var elements = CompareResultFlattener.Flatten(leftSoftware.CompareTo(rightSoftware), includeIdentical);

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

    private static string RequireName(WorkerRequest request)
        => string.IsNullOrWhiteSpace(request.PlcObjectName)
            ? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "PlcObjectName is required.")
            : request.PlcObjectName!;
}
