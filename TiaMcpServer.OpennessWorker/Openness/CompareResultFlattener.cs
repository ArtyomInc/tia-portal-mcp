using Siemens.Engineering.Compare;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Flattens an Openness <see cref="CompareResult"/> tree depth-first into
/// <see cref="CompareElementInfo"/> rows. Shared by software and hardware comparison so both report
/// differences identically.
/// </summary>
internal static class CompareResultFlattener
{
    public const int MaxElements = 10_000;

    private static readonly HashSet<CompareResultState> IdenticalStates = new()
    {
        CompareResultState.ObjectsIdentical,
        CompareResultState.FolderContentsIdentical,
    };

    public static List<CompareElementInfo> Flatten(CompareResult result, bool includeIdentical)
    {
        var output = new List<CompareElementInfo>();
        Flatten(result.RootElement, new List<string>(), 0, includeIdentical, output);
        return output;
    }

    private static void Flatten(
        CompareResultElement? element,
        List<string> path,
        int depth,
        bool includeIdentical,
        List<CompareElementInfo> output)
    {
        if (element is null)
        {
            return;
        }

        var state = element.ComparisonResult;
        if (includeIdentical || !IdenticalStates.Contains(state))
        {
            if (output.Count == MaxElements)
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.SnapshotTooLarge,
                    $"The comparison holds more than {MaxElements} elements; compare with includeIdentical false.");
            }

            output.Add(new CompareElementInfo
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
}
