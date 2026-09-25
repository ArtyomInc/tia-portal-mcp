using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

[Collection("Mcp protocol serial")]
public class WriteToolMcpAnnotationProtocolTests
{
    private static readonly string[] ReadOnlyToolNames = ApprovedToolSurface.ReadOnlyToolNames;

    private static readonly string[] ReadWriteToolNames = ApprovedToolSurface.ReadWriteToolNames;

    private static readonly (string Name, bool ReadOnly, bool Destructive, bool OpenWorld)[] ExpectedWriteToolAnnotations =
    {
        ("preview_write_batch", true, false, false),
        ("apply_write_batch", false, true, false),
        ("open_project", false, true, false),
        ("create_project", false, true, false),
        ("save_project", false, true, false),
        ("save_project_as", false, true, false),
        ("archive_project", false, true, false),
        ("close_project", false, true, false),
    };

    [Fact]
    public async Task ToolsList_ReadWriteProductionSurface_ExposesExactNamesCountsAnnotations_AndRepresentativeSchemas()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite);
        var tools = (await harness.Client.ListToolsAsync()).OrderBy(tool => tool.Name).ToArray();
        var byName = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        Assert.Equal(ReadWriteToolNames, tools.Select(tool => tool.Name));
        Assert.Equal(ReadWriteToolNames.Length, tools.Length);

        foreach (var expected in ExpectedWriteToolAnnotations)
        {
            var annotations = byName[expected.Name].ProtocolTool.Annotations;
            Assert.NotNull(annotations);
            Assert.Equal(expected.ReadOnly, annotations!.ReadOnlyHint);
            Assert.Equal(expected.Destructive, annotations.DestructiveHint);
            Assert.Equal(expected.OpenWorld, annotations.OpenWorldHint);
        }

        Assert.Contains("\"operations\"", byName["preview_write_batch"].ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("\"projectPath\"", byName["open_project"].ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("\"confirm\"", byName["open_project"].ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("\"safetyToken\"", byName["open_project"].ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolsList_ReadOnlyProductionSurface_ExposesExactReadOnlyTools_AndNoWriteTools()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly);
        var tools = (await harness.Client.ListToolsAsync()).OrderBy(tool => tool.Name).ToArray();
        var toolNames = tools.Select(tool => tool.Name).ToArray();

        Assert.Equal(ReadOnlyToolNames, toolNames);
        Assert.Equal(ReadOnlyToolNames.Length, tools.Length);

        foreach (var writeToolName in ReadWriteToolNames.Except(ReadOnlyToolNames, StringComparer.Ordinal))
        {
            Assert.DoesNotContain(writeToolName, toolNames);
        }
    }
}
