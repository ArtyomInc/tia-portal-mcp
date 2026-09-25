using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.ObjectRead;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.ObjectRead;

public class ObjectReadPayloadContractTests
{
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static StructuredOperationItem Project(string operation, string payload)
        => ObjectReadPayloadContract.Project(
            new ObjectReadOperationRequest { OperationId = "op", Operation = operation },
            WorkerCallResult.Ok(payload));

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, Camel);

    private static void AssertRejected(string operation, string payload)
    {
        var item = Project(operation, payload);
        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain(payload, item.Failure.Message, StringComparison.Ordinal);
        Assert.Null(item.Result);
    }

    private static ObjectChildrenPageInfo Page() => new()
    {
        Children =
        {
            new ObjectChildInfo
            {
                Composition = "Devices",
                Index = 0,
                Name = "PLC_1",
                TypeName = "Siemens.Engineering.HW.Device",
                ObjectPath = { new ObjectPathSegmentInfo { Kind = "composition", Name = "Devices", ElementName = "PLC_1", Index = 0 } },
            },
        },
        TotalCount = 2,
        NextCursor = "next",
    };

    private static ObjectExportInfo Export() => new()
    {
        TypeName = "T",
        TotalChars = 10,
        Sha256 = new string('a', 64),
        Offset = 0,
        Content = "0123456789",
    };

    [Fact]
    public void ValidPayloads_DecodeIntoObjects_NotNestedStrings()
    {
        var item = Project("list_object_children", Json(Page()));

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        var result = item.Result!.Value;
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Equal("PLC_1", result.GetProperty("children")[0].GetProperty("name").GetString());

        Assert.Equal("next", result.GetProperty("nextCursor").GetString());
    }

    [Fact]
    public void MembersTheWorkerOmitted_BecomeExplicitNulls()
    {
        // The real worker omits null members on the wire; the canonical document must not.
        var item = Project("describe_object", "{\"typeName\":\"T\"}");

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Equal(JsonValueKind.Null, item.Result!.Value.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Array, item.Result!.Value.GetProperty("compositions").ValueKind);
    }

    [Fact]
    public void EveryOperation_HasADeclaredContract()
    {
        Assert.Equal(OperationBatchStatus.Succeeded, Project("describe_object", Json(new ObjectDescriptionInfo { TypeName = "T" })).Status);
        Assert.Equal(OperationBatchStatus.Succeeded, Project("read_object_attributes", Json(new ObjectAttributesInfo { TypeName = "T" })).Status);
        Assert.Equal(OperationBatchStatus.Succeeded, Project("export_object", Json(Export())).Status);
        Assert.Equal(OperationBatchStatus.Succeeded, Project("list_capabilities", Json(new OpennessCapabilitiesInfo())).Status);
        AssertRejected("set_object_attributes", "{}");
    }

    [Theory]
    [InlineData("describe_object", "{\"unexpectedShape\":true}")]
    [InlineData("describe_object", "{\"TypeName\":\"T\"}")]
    [InlineData("describe_object", "{\"typeName\":\"T\",\"compositions\":null}")]
    [InlineData("describe_object", "{\"typeName\":\"T\",\"attributes\":[{\"name\":\"A\",\"access\":\"sometimes\",\"supportedTypes\":[]}]}")]
    [InlineData("list_object_children", "not json")]
    [InlineData("list_capabilities", "{\"products\":[{\"name\":null,\"options\":[]}]}")]
    [InlineData("read_object_attributes", "{\"typeName\":\"T\",\"attributes\":[{\"name\":\"A\",\"access\":\"readOnly\",\"availability\":\"available\",\"supportedTypes\":[]}]}")]
    [InlineData("describe_object", "{\"root\":\"device\",\"typeName\":\"T\"}")]
    public void NonConformingPayloads_AreProtocolErrors(string operation, string payload)
        => AssertRejected(operation, payload);

    [Fact]
    public void InconsistentPages_AreRejected()
    {
        var noCursorBeforeEnd = Page();
        noCursorBeforeEnd.NextCursor = null;
        AssertRejected("list_object_children", Json(noCursorBeforeEnd));

        var cursorAtEnd = Page();
        cursorAtEnd.TotalCount = 1;
        AssertRejected("list_object_children", Json(cursorAtEnd));

        var detachedChild = Page();
        detachedChild.Children[0].ObjectPath.Add(new ObjectPathSegmentInfo { Kind = "attribute", Name = "Parent" });
        AssertRejected("list_object_children", Json(detachedChild));
    }

    [Fact]
    public void InconsistentExportWindows_AreRejected()
    {
        var pastEnd = Export();
        pastEnd.TotalChars = 5;
        AssertRejected("export_object", Json(pastEnd));

        var missingNext = Export();
        missingNext.TotalChars = 20;
        AssertRejected("export_object", Json(missingNext));

        var badDigest = Export();
        badDigest.Sha256 = "abc";
        AssertRejected("export_object", Json(badDigest));

        var wrongNext = Export();
        wrongNext.TotalChars = 20;
        wrongNext.NextOffset = 12;
        AssertRejected("export_object", Json(wrongNext));
    }

    [Fact]
    public void WorkerFailures_PassThroughWithTheirCategory()
    {
        var item = ObjectReadPayloadContract.Project(
            new ObjectReadOperationRequest { OperationId = "op", Operation = "describe_object" },
            WorkerCallResult.Fail(WorkerFailureCategories.TargetEvidenceMismatch, "ObjectPath segment 1: renamed."));

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.TargetEvidenceMismatch, item.Failure!.Category);
        Assert.Equal("ObjectPath segment 1: renamed.", item.Failure.Message);
    }
}
