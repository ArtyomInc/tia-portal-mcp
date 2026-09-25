using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.HmiRead;
using TiaMcpServer.Json;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.HmiRead;
using TiaMcpServer.OpennessWorker.ObjectModel;
using TiaMcpServer.Tests.ObjectRead;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.HmiRead;

internal static class HmiFixture
{
    public const string ContainerType = "Siemens.Engineering.HW.Features.SoftwareContainer";

    private static FakeObjectNode Station(string deviceName, FakeObjectNode software)
    {
        var item = new FakeObjectNode("Siemens.Engineering.HW.DeviceItem", software.Name)
            .WithService(ContainerType, new FakeObjectNode(ContainerType).WithAttribute("Software", software));
        return new FakeObjectNode("Siemens.Engineering.HW.Device", deviceName).Add("DeviceItems", item);
    }

    public static FakeObjectNode UnifiedSoftware()
    {
        var script = new FakeObjectNode("Siemens.Engineering.HmiUnified.UI.Dynamization.Script.IHmiScript")
            .WithAttribute("ScriptCode", "HMIRuntime.Trace('clicked');")
            .WithAttribute("GlobalDefinitionAreaScriptCode", "");
        var button = new FakeObjectNode("Siemens.Engineering.HmiUnified.UI.Widgets.HmiButton", "Button_1")
            .WithAttribute("Visible", true)
            .WithAttribute("Left", 10)
            .Add("PropertyEventHandlers")
            .Add("EventHandlers", new FakeObjectNode("Siemens.Engineering.HmiUnified.UI.Events.HmiScreenItemEventHandler").WithAttribute("EventType", DayOfWeek.Tuesday).WithAttribute("Script", script))
            .Add(
                "Dynamizations",
                new FakeObjectNode("Siemens.Engineering.HmiUnified.UI.Dynamization.Tag.TagDynamization").WithAttribute("PropertyName", "Text"),
                new FakeObjectNode("Siemens.Engineering.HmiUnified.UI.Dynamization.Script.ScriptDynamization")
                    .WithAttribute("PropertyName", "BackColor")
                    .WithAttribute("ScriptCode", "return 0xFF0000;"));
        var main = new FakeObjectNode("Siemens.Engineering.HmiUnified.UI.Screens.HmiScreen", "sMain")
            .WithAttribute("ScreenNumber", 1)
            .Add("ScreenItems", button)
            .Add("EventHandlers", new FakeObjectNode("Siemens.Engineering.HmiUnified.UI.Events.HmiScreenEventHandler").WithAttribute("EventType", DayOfWeek.Monday).WithAttribute("Script", script))
            .Add("Dynamizations");
        var detail = new FakeObjectNode("Siemens.Engineering.HmiUnified.UI.Screens.HmiScreen", "sDetail").Add("ScreenItems");
        var group = new FakeObjectNode("Siemens.Engineering.HmiUnified.UI.ScreenGroup.HmiScreenGroup", "Details")
            .Add("Screens", detail)
            .Add("Groups");
        return new FakeObjectNode(HmiReadBuilder.UnifiedType, "HMI_RT_1")
            .Add("Screens", main)
            .Add("ScreenGroups", group)
            .Add("Tags", new FakeObjectNode("Siemens.Engineering.HmiUnified.HmiTags.HmiTag", "Motor_On").WithAttribute("DataType", "Bool").WithAttribute("PlcTag", "DbHmi.On"))
            .Add("DiscreteAlarms", new FakeObjectNode("Siemens.Engineering.HmiUnified.HmiAlarm.HmiDiscreteAlarm", "Fault").WithAttribute("Id", 1u))
            .Add("AnalogAlarms");
    }

    public static FakeObjectNode ClassicSoftware()
    {
        var tagTable = new FakeObjectNode("Siemens.Engineering.Hmi.Tag.TagTable", "Default tag table")
            .Add("Tags", new FakeObjectNode("Siemens.Engineering.Hmi.Tag.Tag", "Level"));
        var tagFolder = new FakeObjectNode("Siemens.Engineering.Hmi.Tag.TagSystemFolder", "HMI tags")
            .Add("TagTables", tagTable)
            .Add("Folders");
        var screenFolder = new FakeObjectNode("Siemens.Engineering.Hmi.Screen.ScreenSystemFolder", "Screens")
            .Add("Screens", new FakeObjectNode("Siemens.Engineering.Hmi.Screen.Screen", "Root screen"))
            .Add("Folders");
        return new FakeObjectNode(HmiReadBuilder.ClassicType, "HMI_Classic")
            .WithAttribute("TagFolder", tagFolder)
            .WithAttribute("ScreenFolder", screenFolder);
    }

    public static FakeObjectNode Project(params FakeObjectNode[] softwares)
        => new FakeObjectNode("Siemens.Engineering.Project", "test")
            .Add("Devices", softwares.Select((software, i) => Station("HMI_" + i, software)).ToArray());
}

public class HmiReadBuilderTests
{
    [Fact]
    public void FindsUnifiedAndClassicHmis_WithRuntimes()
    {
        var result = HmiReadBuilder.ListHmis(HmiFixture.Project(HmiFixture.UnifiedSoftware(), HmiFixture.ClassicSoftware()));
        Assert.Equal(new[] { ("HMI_RT_1", "unified"), ("HMI_Classic", "classic") }, result.Hmis.Select(h => (h.Name, h.Runtime)));
    }

    [Fact]
    public void SeveralHmis_RequireAName()
    {
        var project = HmiFixture.Project(HmiFixture.UnifiedSoftware(), HmiFixture.ClassicSoftware());
        Assert.Equal(
            WorkerFailureCategories.TargetAmbiguous,
            Assert.Throws<WorkerOperationException>(() => HmiReadBuilder.List(project, "list_screens", null, null, null, null)).FailureCategory);
    }

    [Fact]
    public void UnifiedScreens_IncludeScreenGroups()
    {
        var project = HmiFixture.Project(HmiFixture.UnifiedSoftware());
        var result = HmiReadBuilder.List(project, "list_screens", null, null, null, null);

        Assert.Equal(new[] { ("sMain", ""), ("sDetail", "Details") }, result.Items.Select(i => (i.Name, string.Join("/", i.GroupPath))));
        Assert.Equal(1L, result.Items[0].Values["ScreenNumber"]);
        foreach (var item in result.Items)
        {
            Assert.Equal(item.Name, ObjectPathResolver.Resolve(project, null, item.ObjectPath).TryReadName());
        }
    }

    [Fact]
    public void ClassicTags_AreGroupedByTagTable()
    {
        var project = HmiFixture.Project(HmiFixture.ClassicSoftware());
        var result = HmiReadBuilder.List(project, "list_hmi_tags", null, null, null, null);

        var tag = Assert.Single(result.Items);
        Assert.Equal(("Level", "Default tag table"), (tag.Name, string.Join("/", tag.GroupPath)));
        Assert.Equal(HmiRuntimes.Classic, result.Runtime);
    }

    [Fact]
    public void RuntimeGaps_AreCapabilityUnavailable()
    {
        var project = HmiFixture.Project(HmiFixture.ClassicSoftware());
        Assert.Equal(
            WorkerFailureCategories.CapabilityUnavailable,
            Assert.Throws<WorkerOperationException>(() => HmiReadBuilder.List(project, "list_hmi_alarms", null, null, null, null)).FailureCategory);
        Assert.Equal(
            WorkerFailureCategories.CapabilityUnavailable,
            Assert.Throws<WorkerOperationException>(() => HmiReadBuilder.ListScreenItems(project, null, "Root screen", null, null, null)).FailureCategory);
    }

    [Fact]
    public void ScreenItems_CarryScalarProperties()
    {
        var project = HmiFixture.Project(HmiFixture.UnifiedSoftware());
        var result = HmiReadBuilder.ListScreenItems(project, null, "sMain", null, null, null);

        Assert.Equal("sMain", result.Target!.Name);
        var button = Assert.Single(result.Items);
        Assert.Equal(("Button_1", true, 10L), (button.Name, button.Values["Visible"], button.Values["Left"]));
    }

    [Fact]
    public void ScreenScripts_CollectEventsAndScriptDynamizations()
    {
        var project = HmiFixture.Project(HmiFixture.UnifiedSoftware());
        var result = HmiReadBuilder.ReadScreenScripts(project, null, "sMain", null, null, null);

        Assert.Equal(
            new[] { ("sMain", "event", "Monday"), ("Button_1", "event", "Tuesday"), ("Button_1", "dynamization", "BackColor") },
            result.Scripts.Select(s => (s.Owner, s.Kind, s.Trigger)));
        Assert.Equal("HMIRuntime.Trace('clicked');", result.Scripts[0].ScriptCode);
        Assert.Equal("return 0xFF0000;", result.Scripts[2].ScriptCode);
        Assert.Equal(
            "Siemens.Engineering.HmiUnified.UI.Dynamization.Script.ScriptDynamization",
            ObjectPathResolver.Resolve(project, null, result.Scripts[2].ObjectPath).TypeName);
    }

    [Fact]
    public void HostAndWorkerListings_Agree()
        => Assert.Equal(HmiReadCatalog.ListingOperations.OrderBy(n => n), HmiReadBuilder.ListingOperations.OrderBy(n => n));
}

public class HmiReadHostTests
{
    [Fact]
    public void Catalog_DeclaresTenObserveOperations_AndValidates()
    {
        Assert.Equal(10, HmiReadCatalog.OperationNames.Count);
        Assert.All(HmiReadCatalog.OperationNames, name => Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(name)));
        Assert.Contains(
            "missing required field(s): screen",
            HmiReadCatalog.Instance.Validate(new[] { new HmiReadOperationRequest { OperationId = "a", Operation = "read_screen_scripts" } }).Error);
        Assert.Contains(
            "'hmiName' is not valid for list_hmis",
            HmiReadCatalog.Instance.Validate(new[] { new HmiReadOperationRequest { OperationId = "a", Operation = "list_hmis", HmiName = "X" } }).Error);
    }

    [Theory]
    [InlineData("list_hmis", "{\"hmis\":[{\"name\":\"H\",\"runtime\":\"panel\",\"deviceName\":\"D\",\"deviceGroupPath\":[],\"objectPath\":[{\"kind\":\"composition\",\"name\":\"Devices\",\"index\":0}]}]}")]
    [InlineData("list_screens", "{\"hmiName\":\"H\",\"runtime\":\"unified\",\"target\":{\"name\":\"S\",\"kind\":\"K\",\"typeName\":\"T\",\"groupPath\":[],\"objectPath\":[{\"kind\":\"composition\",\"name\":\"Screens\",\"index\":0}]},\"items\":[],\"totalCount\":0}")]
    [InlineData("list_screen_items", "{\"hmiName\":\"H\",\"runtime\":\"unified\",\"items\":[],\"totalCount\":0}")]
    [InlineData("read_screen_scripts", "{\"hmiName\":\"H\",\"screen\":{\"name\":\"S\",\"kind\":\"K\",\"typeName\":\"T\",\"groupPath\":[],\"objectPath\":[{\"kind\":\"composition\",\"name\":\"Screens\",\"index\":0}]},\"scripts\":[{\"owner\":\"S\",\"kind\":\"macro\",\"objectPath\":[{\"kind\":\"composition\",\"name\":\"EventHandlers\",\"index\":0}]}],\"totalCount\":1}")]
    public void MalformedPayloads_AreProtocolErrors(string operation, string payload)
        => Assert.Equal(
            WorkerFailureCategories.ProtocolError,
            HmiReadWorker.Project(new HmiReadOperationRequest { OperationId = "a", Operation = operation }, WorkerCallResult.Ok(payload)).Failure!.Category);
}

[Collection("Mcp protocol serial")]
public class HmiReadEndToEndTests
{
    [Fact]
    public async Task Operations_RoundTripThroughTheWorker()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<HmiReadTools>();
        var result = await harness.Client.CallToolAsync(HmiReadTools.ToolName, new Dictionary<string, object?>
        {
            ["operations"] = new object[]
            {
                new { operationId = "hmis", operation = "list_hmis", projectPath = "hmi-read" },
                new { operationId = "screens", operation = "list_screens", projectPath = "hmi-read" },
                new { operationId = "items", operation = "list_screen_items", projectPath = "hmi-read", screen = "sMain" },
                new { operationId = "scripts", operation = "read_screen_scripts", projectPath = "hmi-read", screen = "sMain" },
                new { operationId = "tags", operation = "list_hmi_tags", projectPath = "hmi-read", nameContains = "Motor" },
            },
        });

        Assert.False(result.IsError);
        var root = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.All(items.EnumerateArray(), item => Assert.Equal("succeeded", item.GetProperty("status").GetString()));
        Assert.Equal("unified", items[0].GetProperty("result").GetProperty("hmis")[0].GetProperty("runtime").GetString());
        Assert.Equal("sMain", items[2].GetProperty("result").GetProperty("target").GetProperty("name").GetString());
        Assert.Equal("Tapped", items[3].GetProperty("result").GetProperty("scripts")[0].GetProperty("trigger").GetString());
    }
}
