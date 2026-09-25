using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.PlcRead;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.PlcRead;

public class PlcReadCatalogTests
{
    private static PlcReadOperationRequest Op(string operation, Action<PlcReadOperationRequest>? configure = null, string id = "a")
    {
        var request = new PlcReadOperationRequest { OperationId = id, Operation = operation };
        configure?.Invoke(request);
        return request;
    }

    private static string Error(PlcReadOperationRequest operation)
    {
        var result = PlcReadCatalog.Instance.Validate(new[] { operation });
        Assert.False(result.IsValid);
        return result.Error;
    }

    [Fact]
    public void Catalog_DeclaresTheFourteenR1Operations_AllReadOnlyObserve()
    {
        Assert.Equal(14, PlcReadCatalog.OperationNames.Count);
        foreach (var name in PlcReadCatalog.OperationNames)
        {
            Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(name));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, name), name);
        }
    }

    [Fact]
    public void ValidOperations_Pass()
    {
        var result = PlcReadCatalog.Instance.Validate(new[]
        {
            Op("list_plcs", id: "1"),
            Op("list_blocks", o => { o.PlcName = "PLC_1"; o.NameContains = "Valve"; o.PageSize = 10; o.Cursor = "c"; }, "2"),
            Op("read_watch_table", o => { o.Name = "T"; o.GroupPath = new[] { "G" }; }, "3"),
            Op("read_technology_object", o => { o.Name = "Axis"; o.ParameterNames = new[] { "Actor.Type" }; }, "4"),
            Op("read_block_fingerprints", o => o.Name = "Main", "5"),
            Op("read_checksums", id: "6"),
            Op("compare_software", o => { o.ComparePlcName = "PLC_2"; o.IncludeIdentical = true; }, "7"),
            Op("read_watch_table", o => { o.Name = "T"; o.GroupPath = Array.Empty<string>(); }, "8"),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void RequiredAndInapplicableFields_AreEnforced()
    {
        Assert.Contains("missing required field(s): name", Error(Op("read_watch_table")));
        Assert.Contains("missing required field(s): name", Error(Op("read_block_fingerprints", o => o.Name = " ")));
        Assert.Contains("missing required field(s): comparePlcName", Error(Op("compare_software")));
        Assert.Contains("'plcName' is not valid for list_plcs", Error(Op("list_plcs", o => o.PlcName = "PLC_1")));
        Assert.Contains("'parameterNames' is not valid for read_watch_table", Error(Op("read_watch_table", o => { o.Name = "T"; o.ParameterNames = new[] { "x" }; })));
        Assert.Contains("'pageSize' is not valid for read_block_fingerprints", Error(Op("read_block_fingerprints", o => { o.Name = "M"; o.PageSize = 5; })));
        Assert.Contains("'nameContains' is not valid for read_checksums", Error(Op("read_checksums", o => o.NameContains = "x")));
        Assert.Contains("Valid plc_read operations", Error(Op("list_supervisions")));
    }

    [Fact]
    public void Bounds_AreEnforced()
    {
        Assert.Contains("'plcName' must be nonblank", Error(Op("read_checksums", o => o.PlcName = " ")));
        Assert.Contains("'pageSize' must be between 1 and 200", Error(Op("list_types", o => o.PageSize = 201)));
        Assert.Contains("'parameterNames' must contain", Error(Op("read_technology_object", o => { o.Name = "A"; o.ParameterNames = new[] { "x", "x" }; })));
        Assert.Contains("'groupPath' must contain", Error(Op("read_watch_table", o => { o.Name = "T"; o.GroupPath = new string[33]; })));
    }
}

public class PlcReadPayloadContractTests
{
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static StructuredOperationItem Project(string operation, object payload)
        => PlcReadPayloadContract.Project(
            new PlcReadOperationRequest { OperationId = "op", Operation = operation },
            WorkerCallResult.Ok(payload as string ?? JsonSerializer.Serialize(payload, Camel)));

    private static DomainObjectInfo Item() => new()
    {
        Name = "Main",
        Kind = "OB",
        TypeName = "Siemens.Engineering.SW.Blocks.OB",
        ObjectPath = { new ObjectPathSegmentInfo { Kind = "composition", Name = "Blocks", ElementName = "Main", Index = 0 } },
        Values = { ["Number"] = 1, ["Tags"] = new[] { "a", "b" } },
    };

    [Fact]
    public void Listings_DecodeWithScalarValues()
    {
        var item = Project("list_blocks", new PlcObjectListInfo { PlcName = "PLC_1", Items = { Item() }, TotalCount = 1 });
        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Equal(1, item.Result!.Value.GetProperty("items")[0].GetProperty("values").GetProperty("Number").GetInt32());
    }

    [Theory]
    [InlineData("{\"plcName\":\"PLC_1\",\"items\":[{\"name\":\"M\",\"kind\":\"OB\",\"typeName\":\"T\",\"objectPath\":[{\"kind\":\"composition\",\"name\":\"Blocks\",\"index\":0}],\"values\":{\"x\":{\"nested\":1}}}],\"totalCount\":1}")]
    [InlineData("{\"plcName\":\"PLC_1\",\"items\":[{\"name\":\"M\",\"kind\":\"OB\",\"typeName\":\"T\",\"objectPath\":[]}],\"totalCount\":1}")]
    [InlineData("{\"plcName\":\"PLC_1\",\"items\":[],\"totalCount\":3}")]
    [InlineData("{\"plcName\":\"PLC_1\",\"unexpected\":true}")]
    public void MalformedListings_AreProtocolErrors(string payload)
    {
        var item = Project("list_blocks", payload);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain("nested", item.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareElements_MustHaveConsistentDepth()
    {
        var bad = new PlcCompareInfo
        {
            LeftPlcName = "A",
            RightPlcName = "B",
            Elements = { new PlcCompareElementInfo { Path = { "x" }, Depth = 3, State = "ObjectsDifferent" } },
            TotalCount = 1,
        };
        Assert.Equal(WorkerFailureCategories.ProtocolError, Project("compare_software", bad).Failure!.Category);

        bad.Elements[0].Depth = 1;
        Assert.Equal(OperationBatchStatus.Succeeded, Project("compare_software", bad).Status);
    }

    [Fact]
    public void EveryOperation_HasADeclaredContract()
    {
        foreach (var operation in PlcReadCatalog.OperationNames)
        {
            var item = Project(operation, "{\"unexpectedShape\":true}");
            Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
            Assert.DoesNotContain("No declared result contract", item.Failure.Message, StringComparison.Ordinal);
        }
    }
}

[Collection("Mcp protocol serial")]
public class PlcReadEndToEndTests
{
    private static ValueTask<CallToolResult> CallAsync(McpProtocolTestHarness harness, object operations)
        => harness.Client.CallToolAsync(PlcReadTools.ToolName, new Dictionary<string, object?> { ["operations"] = operations });

    private static JsonElement AssertCanonical(CallToolResult result)
    {
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(structured), Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        return structured;
    }

    [Fact]
    public async Task EveryOperationFamily_RoundTripsThroughTheWorker()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<PlcReadTools>();
        var result = await CallAsync(harness, new object[]
        {
            new { operationId = "plcs", operation = "list_plcs", projectPath = "plc-read" },
            new { operationId = "blocks", operation = "list_blocks", projectPath = "plc-read", plcName = "PLC_1", pageSize = 2 },
            new { operationId = "table", operation = "read_watch_table", projectPath = "plc-read", name = "Commissioning" },
            new { operationId = "fp", operation = "read_block_fingerprints", projectPath = "plc-read", name = "Main" },
            new { operationId = "sum", operation = "read_checksums", projectPath = "plc-read" },
            new { operationId = "cmp", operation = "compare_software", projectPath = "plc-read", comparePlcName = "PLC_2" },
        });

        Assert.False(result.IsError);
        var root = AssertCanonical(result);
        Assert.Equal("plc_read", root.GetProperty("tool").GetString());
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.All(items.EnumerateArray(), item => Assert.Equal("succeeded", item.GetProperty("status").GetString()));
        Assert.Equal("PLC_1", items[0].GetProperty("result").GetProperty("plcs")[0].GetProperty("name").GetString());
        Assert.Equal("plc-cursor", items[1].GetProperty("result").GetProperty("nextCursor").GetString());
        Assert.Equal("Commissioning", items[2].GetProperty("result").GetProperty("target").GetProperty("name").GetString());
        Assert.Equal("Code", items[3].GetProperty("result").GetProperty("fingerprints")[0].GetProperty("id").GetString());
        Assert.Equal("PLC_2", items[5].GetProperty("result").GetProperty("rightPlcName").GetString());
    }

    [Fact]
    public async Task InvalidRequests_FailBeforeTheWorker()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<PlcReadTools>();
        var result = await CallAsync(harness, new object[] { new { operationId = "a", operation = "compare_software", projectPath = "plc-read" } });

        Assert.True(result.IsError);
        Assert.Equal(WorkerFailureCategories.ValidationError, AssertCanonical(result).GetProperty("error").GetProperty("category").GetString());
    }
}
