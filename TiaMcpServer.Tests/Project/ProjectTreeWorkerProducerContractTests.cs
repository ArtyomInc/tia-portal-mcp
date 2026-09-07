using System.Text.Json;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Tests.TestUtilities;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ProjectTreeWorkerProducerContractTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(2, false)]
    [InlineData(1, false)]
    [InlineData(null, true)]
    [InlineData(1, true)]
    public void DefaultAndShallowSnapshots_RealWorkerSerializationPassesStrictDecoder(int? depth, bool selected)
    {
        var selector = selected ? Selector(("Device", "plc_1")) : null;
        var snapshot = new ProjectTreeSnapshotWalker().WalkSnapshot(ProjectWithLeaves(includeLeaves: false), selector, depth);

        var observation = Decode(snapshot, selector, depth);

        Assert.Equal("PLC_1", Assert.Single(observation.Roots).Name);
        if (depth == 1)
            Assert.Equal("1", observation.Roots[0].Details!["ChildrenOmitted"]);
        else
            Assert.Null(observation.Roots[0].Details);
        Assert.Equal(depth, observation.Depth);
        if (selected)
            Assert.Equal("PLC_1", Assert.Single(observation.CanonicalStartSelector!).Name);
        else
            Assert.Null(observation.CanonicalStartSelector);
    }

    [Theory]
    [InlineData("BlockFolder", "Program blocks", "FB", "Motor", false)]
    [InlineData("TagTableFolder", "PLC tags", "TagTable", "Signals", false)]
    [InlineData("TypeFolder", "PLC data types", "Type", "State", false)]
    [InlineData("BlockFolder", "Program blocks", "FB", "Motor", true)]
    [InlineData("TagTableFolder", "PLC tags", "TagTable", "Signals", true)]
    [InlineData("TypeFolder", "PLC data types", "Type", "State", true)]
    public void FullAndLeafSelectedSnapshots_AllLeafFamiliesPassStrictDecoder(
        string folderType, string folderName, string leafType, string leafName, bool selected)
    {
        var selector = selected
            ? Selector(("Device", "PLC_1"), ("PlcSoftware", "PLC"), (folderType, folderName), (leafType, leafName))
            : null;
        var snapshot = new ProjectTreeSnapshotWalker().WalkSnapshot(ProjectWithLeaves(), selector, depth: null);

        // Check the producer first so a missing children array is diagnosed independently
        // of the separate null-member serialization defect.
        var leaf = Descendants(snapshot.Roots).Single(node => node.NodeType == leafType);
        Assert.NotNull(leaf.Children);
        Assert.Empty(leaf.Children);
        var observation = Decode(snapshot, selector, depth: null);
        var decodedLeaf = Descendants(observation.Roots).Single(node => node.NodeType == leafType);
        Assert.Equal(leafName, decodedLeaf.Name);
        Assert.Empty(decodedLeaf.Children!);
        if (selected)
            Assert.Equal(leafName, Assert.Single(observation.Roots).Name);
    }

    [Fact]
    public void LegacyAndUnrelatedPayloads_KeepOmittingNulls()
    {
        var legacy = WorkerSerializationHarness.Serialize(new List<ProjectTreeNode>
        {
            new() { Name = "PLC_1", NodeType = "Device" }
        });
        Assert.Equal("[{\"name\":\"PLC_1\",\"nodeType\":\"Device\"}]", legacy.Payload);
        Assert.Equal("{\"name\":\"unchanged\"}", WorkerSerializationHarness.Serialize(
            new { Name = "unchanged", Optional = (string?)null }).Payload);
    }

    [Fact]
    public void NetworkObjectList_StillPreservesRequiredNullCursor()
    {
        var response = WorkerSerializationHarness.Serialize(new NetworkObjectListInfo());
        using var json = JsonDocument.Parse(response.Payload!);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextCursor").ValueKind);
    }

    private static ProjectTreeObservation Decode(ProjectTreeSelectionResult snapshot,
        IReadOnlyList<ProjectTreeSelectorSegment>? selector, int? depth)
    {
        var response = WorkerSerializationHarness.Serialize(new ProjectTreeBrowseResultInfo
        {
            StartSelector = snapshot.CanonicalStartSelector?.ToList(),
            Depth = depth,
            Roots = snapshot.Roots.ToList()
        });
        Assert.True(response.Success);
        var worker = WorkerCallResult.Ok(response.Payload!) with
        {
            ResolvedProjectPath = @"C:\Projects\Plant.ap21"
        };
        return ProjectTreeWorkerPayloadContract.Decode(worker, selector, depth);
    }

    private static Siemens.Engineering.Project ProjectWithLeaves(bool includeLeaves = true)
    {
        var project = new Siemens.Engineering.Project();
        var device = new Device { Name = "PLC_1" };
        var plc = new PlcSoftware { Name = "PLC" };
        plc.BlockGroup.Name = "Program blocks";
        plc.TagTableGroup.Name = "PLC tags";
        plc.TypeGroup.Name = "PLC data types";
        if (includeLeaves)
        {
            plc.BlockGroup.Blocks.Items.Add(new FB { Name = "Motor", Number = 1 });
            plc.TagTableGroup.TagTables.Items.Add(new PlcTagTable { Name = "Signals" });
            plc.TypeGroup.Types.Items.Add(new PlcType { Name = "State" });
        }
        device.DeviceItems.Items.Add(new DeviceItem
        {
            Container = new SoftwareContainer { Software = plc }
        });
        project.Devices.Items.Add(device);
        return project;
    }

    private static List<ProjectTreeSelectorSegment> Selector(params (string Type, string Name)[] segments)
        => segments.Select(segment => new ProjectTreeSelectorSegment { NodeType = segment.Type, Name = segment.Name }).ToList();

    private static IEnumerable<ProjectTreeNode> Descendants(IEnumerable<ProjectTreeNode> roots)
    {
        foreach (var node in roots)
        {
            yield return node;
            foreach (var child in Descendants(node.Children ?? Enumerable.Empty<ProjectTreeNode>()))
                yield return child;
        }
    }
}
