using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.LibraryRead;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.LibraryRead;
using TiaMcpServer.OpennessWorker.ObjectModel;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tests.ObjectRead;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.LibraryRead;

public class LibraryReadBuilderTests
{
    private static FakeObjectNode Version(string number, string state, params FakeObjectNode[] dependencies)
        => new FakeObjectNode("Siemens.Engineering.Library.Types.LibraryTypeVersion")
            .WithAttribute("VersionNumber", new System.Version(number))
            .WithAttribute("State", DayOfWeek.Monday)
            .WithAttribute("Author", "Artiom")
            .WithAttribute("Dependencies", dependencies.ToList())
            .WithAttribute("Dependents", new List<FakeObjectNode>());

    private static FakeObjectNode Type(string name, params FakeObjectNode[] versions)
    {
        var type = new FakeObjectNode("Siemens.Engineering.Library.Types.LibraryType", name)
            .WithAttribute("Author", "Artiom")
            .WithAttribute("Guid", new Guid("00000000-0000-0000-0000-000000000001"))
            .Add("Versions", versions);
        foreach (var version in versions)
        {
            version.WithAttribute("TypeObject", type);
        }

        return type;
    }

    private static (FakeObjectNode Project, FakeObjectNode Portal) Fixture()
    {
        var udt = Type("iHmiValve", Version("0.0.2", "Committed"));
        var faceplate = Type("fpValve", Version("0.0.3", "Committed"), Version("0.0.4", "InWork", (FakeObjectNode)ObjectPathResolver.Resolve(udt, null, new[]
        {
            new ObjectPathSegmentInfo { Kind = ObjectPathSegmentKinds.Composition, Name = "Versions", Index = 0 },
        })));
        var folder = new FakeObjectNode("Siemens.Engineering.Library.Types.LibraryTypeUserFolder", "HMI")
            .Add("Types", Type("fpValve", Version("1.0.0", "Committed")))
            .Add("Folders");
        var typeFolder = new FakeObjectNode("Siemens.Engineering.Library.Types.LibraryTypeSystemFolder", "Types")
            .Add("Types", udt, faceplate)
            .Add("Folders", folder);
        var masterCopies = new FakeObjectNode("Siemens.Engineering.Library.MasterCopies.MasterCopySystemFolder", "Master copies")
            .Add("MasterCopies", new FakeObjectNode("Siemens.Engineering.Library.MasterCopies.MasterCopy", "Motor").WithAttribute("Author", "A"))
            .Add("Folders");
        var projectLibrary = new FakeObjectNode("Siemens.Engineering.Library.ProjectLibrary")
            .WithAttribute("TypeFolder", typeFolder)
            .WithAttribute("MasterCopyFolder", masterCopies);
        var project = new FakeObjectNode("Siemens.Engineering.Project", "test").WithAttribute("ProjectLibrary", projectLibrary);

        var globalTypes = new FakeObjectNode("Siemens.Engineering.Library.Types.LibraryTypeSystemFolder", "Types")
            .Add("Types", Type("Standard", Version("2.0.0", "Committed")))
            .Add("Folders");
        var global = new FakeObjectNode("Siemens.Engineering.Library.GlobalLibrary", "Corporate")
            .WithAttribute("TypeFolder", globalTypes)
            .WithAttribute("IsReadOnly", true)
            .WithAttribute("Path", new FileInfo(@"C:\Libraries\Corporate.al21"));
        var portal = new FakeObjectNode("Siemens.Engineering.TiaPortal").Add("GlobalLibraries", global);
        return (project, portal);
    }

    [Fact]
    public void ListLibraries_ReportsTheProjectLibraryAndOpenGlobalLibraries()
    {
        var (project, portal) = Fixture();
        var result = LibraryReadBuilder.ListLibraries(project, portal);

        Assert.Equal(new[] { ("test", "project", "project"), ("Corporate", "global", "portal") }, result.Libraries.Select(l => (l.Name, l.Kind, l.Root)));
        Assert.Equal(true, result.Libraries[1].Values["IsReadOnly"]);
        Assert.Equal(@"C:\Libraries\Corporate.al21", result.Libraries[1].Values["Path"]);
        Assert.Equal("Corporate", ObjectPathResolver.Resolve(portal, ObjectRoots.Portal, result.Libraries[1].ObjectPath).TryReadName());
    }

    [Fact]
    public void ListTypes_WalksFolders_WithResolvablePaths()
    {
        var (project, portal) = Fixture();
        var result = LibraryReadBuilder.ListObjects(project, portal, "list_library_types", null, null, null);

        Assert.Equal(new[] { ("iHmiValve", ""), ("fpValve", ""), ("fpValve", "HMI") }, result.Items.Select(i => (i.Name, string.Join("/", i.GroupPath))));
        Assert.Equal("00000000-0000-0000-0000-000000000001", result.Items[0].Values["Guid"]);
        Assert.Equal(ObjectRoots.Project, result.Root);
        foreach (var item in result.Items)
        {
            Assert.Equal(item.Name, ObjectPathResolver.Resolve(project, null, item.ObjectPath).TryReadName());
        }
    }

    [Fact]
    public void GlobalLibraryListings_UseThePortalRoot()
    {
        var (project, portal) = Fixture();
        var result = LibraryReadBuilder.ListObjects(project, portal, "list_library_types", "Corporate", null, null);

        Assert.Equal(("Corporate", "global", "portal"), (result.LibraryName, result.LibraryKind, result.Root));
        var type = Assert.Single(result.Items);
        Assert.Equal("Standard", ObjectPathResolver.Resolve(portal, ObjectRoots.Portal, type.ObjectPath).TryReadName());
    }

    [Fact]
    public void UnknownGlobalLibraries_AreNotFound_AndNeverOpened()
    {
        var (project, portal) = Fixture();
        var exception = Assert.Throws<WorkerOperationException>(() => LibraryReadBuilder.ListObjects(project, portal, "list_library_types", "Missing", null, null));
        Assert.Equal(WorkerFailureCategories.TargetNotFound, exception.FailureCategory);
        Assert.Contains("'Corporate'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("never opened", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadType_ReturnsVersionsWithDependencies()
    {
        var (project, portal) = Fixture();
        var result = LibraryReadBuilder.ReadType(project, portal, null, "fpValve", Array.Empty<string>());

        Assert.Equal(new[] { "0.0.3", "0.0.4" }, result.Versions.Select(v => v.Values["VersionNumber"]));
        var dependency = Assert.Single(result.Versions[1].Dependencies);
        Assert.Equal(("iHmiValve", "0.0.2"), (dependency.TypeName, dependency.VersionNumber));
        Assert.Empty(result.Versions[0].Dependents);
        Assert.Equal(
            "Siemens.Engineering.Library.Types.LibraryTypeVersion",
            ObjectPathResolver.Resolve(project, null, result.Versions[0].ObjectPath).TypeName);
    }

    [Fact]
    public void ReadType_RepeatedNames_NeedAFolderPath()
    {
        var (project, portal) = Fixture();
        Assert.Equal(
            WorkerFailureCategories.TargetAmbiguous,
            Assert.Throws<WorkerOperationException>(() => LibraryReadBuilder.ReadType(project, portal, null, "fpValve", null)).FailureCategory);
        Assert.Equal("1.0.0", LibraryReadBuilder.ReadType(project, portal, null, "fpValve", new[] { "HMI" }).Versions[0].Values["VersionNumber"]);
    }

    [Fact]
    public void ListMasterCopies_ReadsEveryScalarAttribute()
    {
        var (project, portal) = Fixture();
        var result = LibraryReadBuilder.ListObjects(project, portal, "list_master_copies", null, null, null);
        var copy = Assert.Single(result.Items);
        Assert.Equal(("Motor", "A"), (copy.Name, copy.Values["Author"]));
    }

    [Fact]
    public void WithoutAPortal_OnlyTheProjectLibraryIsListed()
    {
        var (project, _) = Fixture();
        var result = LibraryReadBuilder.ListLibraries(project, null);
        Assert.Single(result.Libraries);
        Assert.Contains(result.Diagnostics, d => d.Contains("Portal", StringComparison.Ordinal));
    }
}

public class LibraryReadHostTests
{
    private static LibraryReadOperationRequest Op(string operation, Action<LibraryReadOperationRequest>? configure = null)
    {
        var request = new LibraryReadOperationRequest { OperationId = "a", Operation = operation };
        configure?.Invoke(request);
        return request;
    }

    [Fact]
    public void Catalog_DeclaresSixObserveOperations()
    {
        Assert.Equal(6, LibraryReadCatalog.OperationNames.Count);
        Assert.All(LibraryReadCatalog.OperationNames, name => Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(name)));
    }

    [Fact]
    public void Catalog_ValidatesFields()
    {
        Assert.True(LibraryReadCatalog.Instance.Validate(new[] { Op("find_type_instances", o => { o.Name = "fbValve"; o.Version = "1.0.0"; o.PlcName = "PLC_1"; }) }).IsValid);
        Assert.Contains("missing required field(s): name", LibraryReadCatalog.Instance.Validate(new[] { Op("read_library_type") }).Error);
        Assert.Contains("'libraryName' is not valid for list_libraries", LibraryReadCatalog.Instance.Validate(new[] { Op("list_libraries", o => o.LibraryName = "X") }).Error);
        Assert.Contains("'includeUpToDate' is not valid for list_library_types", LibraryReadCatalog.Instance.Validate(new[] { Op("list_library_types", o => o.IncludeUpToDate = true) }).Error);
        Assert.Contains("'libraryName' must be nonblank", LibraryReadCatalog.Instance.Validate(new[] { Op("list_master_copies", o => o.LibraryName = " ") }).Error);
    }

    [Theory]
    [InlineData("list_libraries", "{\"libraries\":[{\"name\":\"L\",\"kind\":\"folder\",\"root\":\"project\",\"objectPath\":[{\"kind\":\"attribute\",\"name\":\"ProjectLibrary\"}]}]}")]
    [InlineData("list_library_types", "{\"libraryName\":\"L\",\"libraryKind\":\"project\",\"root\":\"device\",\"items\":[],\"totalCount\":0}")]
    [InlineData("check_library_updates", "{\"libraryName\":\"L\",\"libraryKind\":\"project\",\"messages\":[{\"depth\":-1}],\"totalCount\":1}")]
    [InlineData("find_type_instances", "{\"libraryName\":\"L\",\"libraryKind\":\"project\",\"typeName\":\"T\",\"plcName\":\"P\",\"instances\":[null]}")]
    public void MalformedPayloads_AreProtocolErrors(string operation, string payload)
        => Assert.Equal(
            WorkerFailureCategories.ProtocolError,
            LibraryReadPayloadContract.Project(Op(operation), WorkerCallResult.Ok(payload)).Failure!.Category);
}

[Collection("Mcp protocol serial")]
public class LibraryReadEndToEndTests
{
    [Fact]
    public async Task EveryOperation_RoundTripsThroughTheWorker()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<LibraryReadTools>();
        var result = await harness.Client.CallToolAsync(LibraryReadTools.ToolName, new Dictionary<string, object?>
        {
            ["operations"] = new object[]
            {
                new { operationId = "libs", operation = "list_libraries", projectPath = "library-read" },
                new { operationId = "types", operation = "list_library_types", projectPath = "library-read", libraryName = "Corporate" },
                new { operationId = "type", operation = "read_library_type", projectPath = "library-read", name = "fpValve" },
                new { operationId = "mc", operation = "list_master_copies", projectPath = "library-read" },
                new { operationId = "check", operation = "check_library_updates", projectPath = "library-read" },
                new { operationId = "find", operation = "find_type_instances", projectPath = "library-read", name = "fbValve", plcName = "PLC_1" },
            },
        });

        Assert.False(result.IsError);
        var root = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.All(items.EnumerateArray(), item => Assert.Equal("succeeded", item.GetProperty("status").GetString()));
        Assert.Equal("portal", items[1].GetProperty("result").GetProperty("root").GetString());
        Assert.Equal("iHmiValve", items[2].GetProperty("result").GetProperty("versions")[0].GetProperty("dependencies")[0].GetProperty("typeName").GetString());
        Assert.Equal("FB", items[5].GetProperty("result").GetProperty("instances")[0].GetProperty("kind").GetString());
    }
}
