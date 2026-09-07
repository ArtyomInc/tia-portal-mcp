using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectTreeV3ContractTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt = CreatedAt.AddMinutes(10);

    [Fact]
    public void SuccessEnvelope_UsesTheExactV3Shape()
    {
        var response = new BrowseProjectTreeResponse(
            ProjectTreeContract.Version,
            ProjectTreeStatuses.Succeeded,
            new BrowseProjectTreeResult(
                new ProjectTreeSnapshotMetadata("snapshot-1", CreatedAt, ExpiresAt, 1),
                new ProjectTreeQuery(@"C:\Projects\Plant.ap21", null, null),
                new ProjectTreePagination(0, 100, 1, null),
                new[]
                {
                    new ProjectTreeFlatNode(
                        "n0",
                        null,
                        0,
                        "PLC_1",
                        ProjectTreeNodeTypes.Device,
                        new Dictionary<string, string>())
                }),
            Failure: null,
            Warnings: Array.Empty<string>());

        using var document = JsonDocument.Parse(CanonicalJson.Serialize(response));
        var root = document.RootElement;

        Assert.Equal("3.0", root.GetProperty("contractVersion").GetString());
        Assert.Equal("succeeded", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("failure").ValueKind);
        Assert.Equal(5, root.EnumerateObject().Count());
        Assert.Empty(root.GetProperty("warnings").EnumerateArray());

        var result = root.GetProperty("result");
        Assert.Equal(4, result.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("query").GetProperty("startSelector").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("query").GetProperty("depth").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("pagination").GetProperty("nextCursor").ValueKind);

        var node = Assert.Single(result.GetProperty("nodes").EnumerateArray());
        Assert.Equal(6, node.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Null, node.GetProperty("parentNodeId").ValueKind);
        Assert.Empty(node.GetProperty("details").EnumerateObject());
    }

    [Fact]
    public void FailureEnvelope_UsesExplicitNullResult()
    {
        var response = new BrowseProjectTreeResponse(
            ProjectTreeContract.Version,
            ProjectTreeStatuses.Failed,
            Result: null,
            new BrowseProjectTreeFailure(WorkerFailureCategories.InvalidSelector, "The selector is invalid."),
            Warnings: Array.Empty<string>());

        using var document = JsonDocument.Parse(CanonicalJson.Serialize(response));

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("result").ValueKind);
        Assert.Equal(WorkerFailureCategories.InvalidSelector,
            document.RootElement.GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public void SelectorSegment_RejectsUnknownMembers()
    {
        const string json = """{"nodeType":"Device","name":"PLC_1","startPath":"legacy"}""";

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProjectTreeSelectorSegment>(json, options));
    }

    [Fact]
    public void WorkerRequest_UsesTypedStartSelectorAlongsideLegacyStartPath()
    {
        var request = new WorkerRequest
        {
            StartPath = "PLC_1/Blocks",
            StartSelector = new List<ProjectTreeSelectorSegment>
            {
                new() { NodeType = ProjectTreeNodeTypes.Device, Name = "PLC_1" }
            }
        };

        using var document = JsonDocument.Parse(CanonicalJson.Serialize(request));

        Assert.Equal("PLC_1/Blocks", document.RootElement.GetProperty("startPath").GetString());
        var selector = Assert.Single(document.RootElement.GetProperty("startSelector").EnumerateArray());
        Assert.Equal(ProjectTreeNodeTypes.Device, selector.GetProperty("nodeType").GetString());
        Assert.Equal("PLC_1", selector.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(WorkerFailureCategories.InvalidSelector)]
    [InlineData(WorkerFailureCategories.SnapshotTooLarge)]
    [InlineData(WorkerFailureCategories.SnapshotUnavailable)]
    [InlineData(WorkerFailureCategories.ResultItemTooLarge)]
    [InlineData(WorkerFailureCategories.ResultMetadataTooLarge)]
    public void ProjectTreeFailureCategory_IsKnownAndAcceptedByWorkerCallResult(string category)
    {
        Assert.True(WorkerFailureCategories.IsKnown(category));

        var result = WorkerCallResult.Fail(category, "expected test failure");

        Assert.Equal(category, result.FailureCategory);
    }

    [Fact]
    public void NodeTypeVocabulary_IsTheExactClosedSet()
    {
        var expected = new[]
        {
            ProjectTreeNodeTypes.Device,
            ProjectTreeNodeTypes.PlcSoftware,
            ProjectTreeNodeTypes.SoftwareUnit,
            ProjectTreeNodeTypes.BlockFolder,
            ProjectTreeNodeTypes.SystemBlockFolder,
            ProjectTreeNodeTypes.Ob,
            ProjectTreeNodeTypes.Fb,
            ProjectTreeNodeTypes.Fc,
            ProjectTreeNodeTypes.GlobalDb,
            ProjectTreeNodeTypes.InstanceDb,
            ProjectTreeNodeTypes.ArrayDb,
            ProjectTreeNodeTypes.Block,
            ProjectTreeNodeTypes.TagTableFolder,
            ProjectTreeNodeTypes.TagTable,
            ProjectTreeNodeTypes.TypeFolder,
            ProjectTreeNodeTypes.Type
        };

        Assert.Equal(
            expected.OrderBy(value => value, StringComparer.Ordinal),
            ProjectTreeNodeTypes.All.OrderBy(value => value, StringComparer.Ordinal));
    }
}
