using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.ObjectRead;
using Xunit;

namespace TiaMcpServer.Tests.ObjectRead;

public class ObjectReadCatalogTests
{
    private static ObjectReadOperationRequest Op(string operation, Action<ObjectReadOperationRequest>? configure = null, string id = "a")
    {
        var request = new ObjectReadOperationRequest { OperationId = id, Operation = operation };
        configure?.Invoke(request);
        return request;
    }

    private static string Error(params ObjectReadOperationRequest[] operations)
    {
        var result = ObjectReadCatalog.Instance.Validate(operations);
        Assert.False(result.IsValid);
        return result.Error;
    }

    [Fact]
    public void Catalog_DeclaresExactlyTheFiveR0Operations()
        => Assert.Equal(
            new[] { "describe_object", "list_object_children", "read_object_attributes", "export_object", "list_capabilities" },
            ObjectReadCatalog.OperationNames);

    [Fact]
    public void EveryOperation_IsReadOnlySafe_AndClassified()
    {
        foreach (var name in ObjectReadCatalog.OperationNames)
        {
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, name), name);
            Assert.False(OperationPolicyCatalog.RequiresExpectedSessionIdentity(name), name);
        }

        Assert.Equal(OperationCapability.TemporaryExport, OperationPolicyCatalog.GetCapability("export_object"));
        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability("describe_object"));
    }

    [Fact]
    public void MinimalOperations_AreValid()
    {
        var result = ObjectReadCatalog.Instance.Validate(new[]
        {
            Op("describe_object", id: "1"),
            Op("list_object_children", o => o.ObjectPath = new[] { new ObjectPathSegment { Kind = "composition", Name = "Devices", Index = 0 } }, "2"),
            Op("read_object_attributes", o => o.AttributeNames = new[] { "Name" }, "3"),
            Op("export_object", o => { o.ExportOptions = new[] { "withDefaults" }; o.Offset = 0; o.MaxChars = 30_000; }, "4"),
            Op("list_capabilities", id: "5"),
            Op("describe_object", o => o.Root = "portal", "6"),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void BatchLevelRules_MatchTheOtherStructuredTools()
    {
        Assert.Equal("Batch must contain at least one operation.", ObjectReadCatalog.Instance.Validate(Array.Empty<ObjectReadOperationRequest>()).Error);
        Assert.Contains("maximum of 50", ObjectReadCatalog.Instance.Validate(
            Enumerable.Range(0, 51).Select(i => Op("list_capabilities", id: i.ToString())).ToArray()).Error);
        Assert.Contains("Duplicate operationId 'a'", Error(Op("list_capabilities"), Op("list_capabilities")));
        Assert.Contains("requires a unique operationId", Error(Op("list_capabilities", id: " ")));
        Assert.Contains("at most 256 characters", Error(Op("list_capabilities", id: new string('x', 257))));
        Assert.Contains("Valid object_read operations", Error(Op("set_object_attributes")));
    }

    [Theory]
    [InlineData("list_capabilities", "root")]
    [InlineData("list_capabilities", "objectPath")]
    [InlineData("describe_object", "attributeNames")]
    [InlineData("describe_object", "cursor")]
    [InlineData("read_object_attributes", "pageSize")]
    [InlineData("list_object_children", "exportOptions")]
    [InlineData("export_object", "compositionNames")]
    public void InapplicableFields_AreRejected(string operation, string field)
    {
        var request = Op(operation, o =>
        {
            switch (field)
            {
                case "root": o.Root = "project"; break;
                case "objectPath": o.ObjectPath = Array.Empty<ObjectPathSegment>(); break;
                case "attributeNames": o.AttributeNames = new[] { "Name" }; break;
                case "cursor": o.Cursor = "c"; break;
                case "pageSize": o.PageSize = 5; break;
                case "exportOptions": o.ExportOptions = new[] { "withDefaults" }; break;
                case "compositionNames": o.CompositionNames = new[] { "Devices" }; break;
            }
        });

        Assert.Contains($"'{field}' is not valid for {operation}", Error(request));
    }

    [Theory]
    [InlineData("{\"kind\":\"composition\",\"name\":\"Devices\"}", "requires elementName, index, or both")]
    [InlineData("{\"kind\":\"composition\",\"name\":\"Devices\",\"index\":-1}", "must be 0 or greater")]
    [InlineData("{\"kind\":\"composition\",\"name\":\"Devices\",\"elementName\":\"\"}", "must be nonempty")]
    [InlineData("{\"kind\":\"attribute\",\"name\":\"Comment\",\"index\":0}", "apply only to composition steps")]
    [InlineData("{\"kind\":\"service\",\"name\":\"X\",\"elementName\":\"y\"}", "apply only to composition steps")]
    [InlineData("{\"kind\":\"method\",\"name\":\"Delete\"}", "kind' must be one of")]
    [InlineData("{\"kind\":\"attribute\",\"name\":\" \"}", "must be nonblank")]
    public void MalformedSegments_AreRejected(string segmentJson, string expected)
    {
        var segment = JsonSerializer.Deserialize<ObjectPathSegment>(segmentJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        Assert.Contains(expected, Error(Op("describe_object", o => o.ObjectPath = new[] { segment })));
    }

    [Fact]
    public void TooLongPaths_AndUnknownRoots_AreRejected()
    {
        var segments = Enumerable.Range(0, 33).Select(_ => new ObjectPathSegment { Kind = "attribute", Name = "Parent" }).ToArray();
        Assert.Contains("at most 32 segments", Error(Op("describe_object", o => o.ObjectPath = segments)));
        Assert.Contains("'root' must be one of", Error(Op("describe_object", o => o.Root = "device")));
    }

    [Fact]
    public void OperationBounds_AreEnforced()
    {
        Assert.Contains("'pageSize' must be between 1 and 200", Error(Op("list_object_children", o => o.PageSize = 0)));
        Assert.Contains("'cursor' must not be blank", Error(Op("list_object_children", o => o.Cursor = " ")));
        Assert.Contains("'compositionNames' must contain", Error(Op("list_object_children", o => o.CompositionNames = new[] { "A", "A" })));
        Assert.Contains("'attributeNames' must contain", Error(Op("read_object_attributes", o => o.AttributeNames = Array.Empty<string>())));
        Assert.Contains("'attributeNames' must contain", Error(Op("read_object_attributes", o => o.AttributeNames = Enumerable.Range(0, 201).Select(i => $"A{i}").ToArray())));
        Assert.Contains("'exportOptions' values must be one of", Error(Op("export_object", o => o.ExportOptions = new[] { "all" })));
        Assert.Contains("'exportOptions' values must be unique", Error(Op("export_object", o => o.ExportOptions = new[] { "withDefaults", "withDefaults" })));
        Assert.Contains("'offset' must be 0 or greater", Error(Op("export_object", o => o.Offset = -1)));
        Assert.Contains("'maxChars' must be between 1 and 30000", Error(Op("export_object", o => o.MaxChars = 30_001)));
    }

    [Fact]
    public void RequestJson_RejectsUnknownMembers()
    {
        Assert.Throws<JsonException>(() => CanonicalJson.Deserialize<ObjectReadOperationRequest>(
            "{\"operationId\":\"a\",\"operation\":\"describe_object\",\"path\":[]}"));
        Assert.Throws<JsonException>(() => CanonicalJson.Deserialize<ObjectReadOperationRequest>(
            "{\"operationId\":\"a\",\"operation\":\"describe_object\",\"objectPath\":[{\"kind\":\"composition\",\"name\":\"Devices\",\"key\":\"x\"}]}"));
    }

    [Fact]
    public void AccessMode_AllowsEveryOperationInReadOnlyMode()
        => Assert.Empty(ObjectReadCatalog.Instance.ValidateAccessMode(
            ObjectReadCatalog.OperationNames.Select(name => Op(name, id: name)).ToArray(),
            McpAccessMode.ReadOnly));
}
