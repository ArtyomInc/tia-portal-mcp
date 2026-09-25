using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.ObjectRead;
using Xunit;

namespace TiaMcpServer.Tests.ObjectRead;

[Collection("Mcp protocol serial")]
public class ObjectReadEndToEndTests
{
    private static ValueTask<CallToolResult> CallAsync(McpProtocolTestHarness harness, object operations)
        => harness.Client.CallToolAsync(
            ObjectReadTools.ToolName,
            new Dictionary<string, object?> { ["operations"] = operations });

    private static JsonElement AssertCanonical(CallToolResult result)
    {
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal(CanonicalJson.Serialize(structured), text);
        return structured;
    }

    private static readonly object[] PlcPath =
    {
        new { kind = "composition", name = "Devices", elementName = "PLC_1", index = 0 },
    };

    [Fact]
    public async Task EveryOperation_RoundTripsThroughTheWorker_AsOneCanonicalDocument()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ObjectReadTools>();
        var result = await CallAsync(harness, new object[]
        {
            new { operationId = "describe", operation = "describe_object", projectPath = "object-read" },
            new { operationId = "children", operation = "list_object_children", projectPath = "object-read", pageSize = 2 },
            new { operationId = "attributes", operation = "read_object_attributes", projectPath = "object-read", objectPath = PlcPath, attributeNames = new[] { "Author" } },
            new { operationId = "export", operation = "export_object", projectPath = "object-read", objectPath = PlcPath, exportOptions = new[] { "withDefaults" } },
            new { operationId = "caps", operation = "list_capabilities", projectPath = "object-read" },
        });

        Assert.False(result.IsError);
        var root = AssertCanonical(result);
        Assert.Equal("object_read", root.GetProperty("tool").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal(5, items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            Assert.Equal("succeeded", item.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Object, item.GetProperty("result").ValueKind);
        }

        var children = items[1].GetProperty("result");
        Assert.Equal(3, children.GetProperty("totalCount").GetInt32());
        Assert.Equal("fixture-cursor", children.GetProperty("nextCursor").GetString());
        var childPath = children.GetProperty("children")[0].GetProperty("objectPath");
        Assert.Equal("PLC_1", childPath[0].GetProperty("elementName").GetString());

        var attribute = items[2].GetProperty("result").GetProperty("attributes")[0];
        Assert.Equal("Engineer", attribute.GetProperty("value").GetProperty("value").GetString());
        Assert.Equal("PLC_1", items[2].GetProperty("result").GetProperty("objectPath")[0].GetProperty("elementName").GetString());

        var export = items[3].GetProperty("result");
        Assert.StartsWith("<Document>", export.GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, export.GetProperty("nextOffset").ValueKind);
    }

    [Fact]
    public async Task ContinuationCursor_IsForwardedToTheWorker()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ObjectReadTools>();
        var result = await CallAsync(harness, new object[]
        {
            new { operationId = "page2", operation = "list_object_children", projectPath = "object-read", pageSize = 2, cursor = "fixture-cursor" },
        });

        var page = AssertCanonical(result).GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(2, page.GetProperty("offset").GetInt32());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task WorkerFailuresAndContractViolations_AreItemFailures_NotToolErrors()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ObjectReadTools>();
        var result = await CallAsync(harness, new object[]
        {
            new { operationId = "missing", operation = "describe_object", projectPath = "object-read-not-found" },
            new { operationId = "malformed", operation = "describe_object", projectPath = "object-read-malformed" },
            new { operationId = "ok", operation = "list_capabilities", projectPath = "object-read" },
        });

        Assert.False(result.IsError);
        var root = AssertCanonical(result);
        Assert.False(root.GetProperty("success").GetBoolean());
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal(WorkerFailureCategories.TargetNotFound, items[0].GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(WorkerFailureCategories.ProtocolError, items[1].GetProperty("failure").GetProperty("category").GetString());
        Assert.DoesNotContain("unexpectedShape", items[1].GetRawText(), StringComparison.Ordinal);
        Assert.Equal("succeeded", items[2].GetProperty("status").GetString());
        var counts = root.GetProperty("batch").GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("succeeded").GetInt32());
        Assert.Equal(2, counts.GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task InvalidRequests_FailBeforeAnyWorkerCall()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ObjectReadTools>();
        var result = await CallAsync(harness, new object[]
        {
            new { operationId = "a", operation = "describe_object", projectPath = "object-read", objectPath = new object[] { new { kind = "composition", name = "Devices" } } },
        });

        Assert.True(result.IsError);
        var root = AssertCanonical(result);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("batch").ValueKind);
        Assert.Equal(WorkerFailureCategories.ValidationError, root.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task ReadOnlyProductionSurface_ExposesObjectRead_AndRunsIt()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly);
        var tools = await harness.Client.ListToolsAsync();
        var tool = Assert.Single(tools, t => t.Name == ObjectReadTools.ToolName);
        Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint);
        Assert.NotNull(tool.ProtocolTool.OutputSchema);

        var result = await CallAsync(harness, new object[]
        {
            new { operationId = "caps", operation = "list_capabilities", projectPath = "object-read" },
        });
        Assert.Equal("succeeded", AssertCanonical(result).GetProperty("batch").GetProperty("operations")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task DomainReadPath_RefusesAnyNonReadWorkerMethod()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ObjectReadTools>();
        foreach (var method in new[] { "delete_block", "open_project", "compile_check", "start_plc", "no_such_method" })
        {
            var result = await harness.WorkerClient.ReadDomainAsync(method, "object-read", _ => { });
            Assert.False(result.Success);
            Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
        }
    }
}
