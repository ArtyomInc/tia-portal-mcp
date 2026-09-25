using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.HardwareRead;
using TiaMcpServer.OpennessWorker.ObjectModel;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tests.ObjectRead;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class DeviceWalkerAndHardwareBuilderTests
{
    private static FakeObjectNode Item(string name, params long[] identifiers)
    {
        var item = new FakeObjectNode("Siemens.Engineering.HW.DeviceItem", name);
        item.Add("HwIdentifiers", identifiers.Select(id => new FakeObjectNode("Siemens.Engineering.HW.HwIdentifier").WithAttribute("Identifier", id)).ToArray());
        return item;
    }

    private static FakeObjectNode Project()
    {
        var port = Item("Port_1", 66);
        var pn = Item("PROFINET interface_1", 64).Add("DeviceItems", port);
        var cpu = Item("PLC_1", 48, 49).Add("DeviceItems", pn);
        var station = new FakeObjectNode("Siemens.Engineering.HW.Device", "Station_1")
            .WithAttribute("TypeIdentifier", "System:Device.S71500")
            .Add("DeviceItems", Item("Rail_0"), cpu)
            .Add("HwIdentifiers", new FakeObjectNode("Siemens.Engineering.HW.HwIdentifier").WithAttribute("Identifier", 32L));
        var inner = new FakeObjectNode("Siemens.Engineering.HW.DeviceUserGroup", "Cell")
            .Add("Devices", new FakeObjectNode("Siemens.Engineering.HW.Device", "IO_1").Add("DeviceItems"))
            .Add("Groups");
        var line = new FakeObjectNode("Siemens.Engineering.HW.DeviceUserGroup", "Line")
            .Add("Devices", station)
            .Add("Groups", inner);
        return new FakeObjectNode("Siemens.Engineering.Project")
            .Add("Devices", new FakeObjectNode("Siemens.Engineering.HW.Device", "HMI_1").Add("DeviceItems"))
            .Add("DeviceGroups", line);
    }

    [Fact]
    public void Devices_AreUngroupedFirst_ThenDepthFirstByGroup_WithResolvablePaths()
    {
        var project = Project();
        var devices = DeviceWalker.Devices(project, new());

        Assert.Equal(new[] { ("HMI_1", ""), ("Station_1", "Line"), ("IO_1", "Line/Cell") }, devices.Select(d => (d.Name, string.Join("/", d.GroupPath))));
        foreach (var device in devices)
        {
            Assert.Equal(device.Name, ObjectPathResolver.Resolve(project, null, device.ObjectPath).TryReadName());
        }
    }

    [Fact]
    public void DeviceSelection_IgnoresCase_AndFailsClosed()
    {
        var project = Project();
        Assert.Equal("Station_1", DeviceWalker.Select(project, "station_1", new()).Name);
        Assert.Equal(
            WorkerFailureCategories.TargetNotFound,
            Assert.Throws<WorkerOperationException>(() => DeviceWalker.Select(project, "Nope", new())).FailureCategory);

        var twins = new FakeObjectNode("Siemens.Engineering.Project").Add(
            "Devices",
            new FakeObjectNode("Siemens.Engineering.HW.Device", "Twin"),
            new FakeObjectNode("Siemens.Engineering.HW.Device", "TWIN"));
        Assert.Equal(
            WorkerFailureCategories.TargetAmbiguous,
            Assert.Throws<WorkerOperationException>(() => DeviceWalker.Select(twins, "twin", new())).FailureCategory);
    }

    [Fact]
    public void DeviceGroupTree_ListsGroupsWithTheirOwnDevices()
    {
        var tree = HardwareReadBuilder.ListDeviceGroups(Project());

        Assert.Equal("HMI_1", Assert.Single(tree.UngroupedDevices).Name);
        Assert.Equal(new[] { "Line", "Line/Cell" }, tree.Groups.Select(g => string.Join("/", g.GroupPath)));
        Assert.Equal("Station_1", Assert.Single(tree.Groups[0].Devices).Name);
        Assert.Equal("System:Device.S71500", tree.Groups[0].Devices[0].TypeIdentifier);
        Assert.Equal("IO_1", Assert.Single(tree.Groups[1].Devices).Name);
    }

    [Fact]
    public void HwIdentifiers_CoverTheDeviceAndEveryNestedItem()
    {
        var project = Project();
        var result = HardwareReadBuilder.ListHwIdentifiers(project, "Station_1", null, null);

        Assert.Equal(
            new[] { (32L, ""), (48L, "PLC_1"), (49L, "PLC_1"), (64L, "PLC_1/PROFINET interface_1"), (66L, "PLC_1/PROFINET interface_1/Port_1") },
            result.Identifiers.Select(i => (i.Identifier, string.Join("/", i.OwnerPath))));
        Assert.Equal(
            "Siemens.Engineering.HW.HwIdentifier",
            ObjectPathResolver.Resolve(project, null, result.Identifiers[3].ObjectPath).TypeName);

        var page = HardwareReadBuilder.ListHwIdentifiers(project, "Station_1", 2, null);
        var next = HardwareReadBuilder.ListHwIdentifiers(project, "Station_1", 2, page.NextCursor);
        Assert.Equal(new[] { 49L, 64L }, next.Identifiers.Select(i => i.Identifier));
    }
}

public class HardwareReadCatalogTests
{
    private static NetworkOperationRequest Op(string operation, Action<NetworkOperationRequest>? configure = null)
    {
        var request = new NetworkOperationRequest { OperationId = "a", Operation = operation };
        configure?.Invoke(request);
        return request;
    }

    [Fact]
    public void HardwareReads_AreObserveOnly()
    {
        foreach (var name in NetworkOperationCatalog.HardwareReadOperationNames)
        {
            Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(name));
            Assert.Contains(name, NetworkOperationCatalog.ReadOperationNames);
        }
    }

    [Fact]
    public void Validation_CoversRequiredFieldsAndBounds()
    {
        Assert.True(NetworkOperationCatalog.ValidateRead(new[] { Op("list_device_groups") }).IsValid);
        Assert.True(NetworkOperationCatalog.ValidateRead(new[] { Op("compare_hardware", o => { o.DeviceName = "A"; o.CompareDeviceName = "B"; o.IncludeIdentical = true; o.PageSize = 10; }) }).IsValid);
        Assert.Contains("missing required field(s): deviceName", NetworkOperationCatalog.ValidateRead(new[] { Op("list_hw_identifiers") }).Error);
        Assert.Contains("compareDeviceName", NetworkOperationCatalog.ValidateRead(new[] { Op("compare_hardware", o => o.DeviceName = "A") }).Error);
        Assert.Contains("'pageSize' must be between 1 and 200", NetworkOperationCatalog.ValidateRead(new[] { Op("list_hw_identifiers", o => { o.DeviceName = "A"; o.PageSize = 0; }) }).Error);
        Assert.Contains("'deviceName' must not be blank", NetworkOperationCatalog.ValidateRead(new[] { Op("read_port_topology", o => o.DeviceName = " ") }).Error);
        Assert.Contains("'includeIdentical' is not valid for read_port_topology", NetworkOperationCatalog.ValidateRead(new[] { Op("read_port_topology", o => o.IncludeIdentical = true) }).Error);
        Assert.Contains("'compareDeviceName' is not valid for list_device_groups", NetworkOperationCatalog.ValidateRead(new[] { Op("list_device_groups", o => o.CompareDeviceName = "B") }).Error);
    }

    [Fact]
    public void HardwareReads_CannotRunInAWriteRequest()
        => Assert.Contains("is a read operation", NetworkOperationCatalog.ValidateWrite(new[] { Op("list_device_groups") }).Error);
}

public class HardwareReadPayloadContractTests
{
    private static StructuredOperationItem Project(string operation, string payload)
        => NetworkPayloadContract.Project(
            new NetworkOperationRequest { OperationId = "op", Operation = operation },
            WorkerCallResult.Ok(payload),
            _ => { });

    [Theory]
    [InlineData("list_device_groups", "{\"ungroupedDevices\":[],\"groups\":[{\"name\":\"G\",\"groupPath\":[],\"objectPath\":[{\"kind\":\"composition\",\"name\":\"DeviceGroups\",\"index\":0}],\"devices\":[]}]}")]
    [InlineData("list_hw_identifiers", "{\"deviceName\":\"D\",\"identifiers\":[],\"totalCount\":2}")]
    [InlineData("read_port_topology", "{\"ports\":[{\"deviceName\":\"D\",\"itemPath\":[],\"objectPath\":[],\"values\":{},\"partners\":[]}]}")]
    [InlineData("compare_hardware", "{\"leftDeviceName\":\"A\",\"rightDeviceName\":\"B\",\"elements\":[{\"path\":[],\"depth\":2,\"state\":\"ObjectsDifferent\"}],\"totalCount\":1}")]
    [InlineData("list_unplugged_items", "{\"devices\":[{\"deviceName\":null,\"items\":[]}]}")]
    public void MalformedHardwarePayloads_AreProtocolErrors(string operation, string payload)
        => Assert.Equal(WorkerFailureCategories.ProtocolError, Project(operation, payload).Failure!.Category);

    [Fact]
    public void ValidHardwarePayloads_Decode()
    {
        Assert.Equal(OperationBatchStatus.Succeeded, Project("list_device_groups", "{\"ungroupedDevices\":[],\"groups\":[]}").Status);
        Assert.Equal(OperationBatchStatus.Succeeded, Project("list_unplugged_items", "{\"devices\":[]}").Status);
        Assert.Equal(OperationBatchStatus.Succeeded, Project("read_port_topology", "{\"ports\":[]}").Status);
    }
}

[Collection("Mcp protocol serial")]
public class HardwareReadEndToEndTests
{
    [Fact]
    public async Task HardwareReads_RoundTripThroughNetworkRead()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkReadTools>();
        var result = await harness.Client.CallToolAsync("network_read", new Dictionary<string, object?>
        {
            ["operations"] = new object[]
            {
                new { operationId = "groups", operation = "list_device_groups", projectPath = "hardware-read" },
                new { operationId = "unplugged", operation = "list_unplugged_items", projectPath = "hardware-read" },
                new { operationId = "hwid", operation = "list_hw_identifiers", projectPath = "hardware-read", deviceName = "Station_1" },
                new { operationId = "ports", operation = "read_port_topology", projectPath = "hardware-read" },
                new { operationId = "cmp", operation = "compare_hardware", projectPath = "hardware-read", deviceName = "Station_1", compareDeviceName = "Station_2" },
            },
        });

        Assert.False(result.IsError);
        var root = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.All(items.EnumerateArray(), item => Assert.Equal("succeeded", item.GetProperty("status").GetString()));
        Assert.Equal("Line", items[0].GetProperty("result").GetProperty("groups")[0].GetProperty("name").GetString());
        Assert.Equal(48, items[2].GetProperty("result").GetProperty("identifiers")[0].GetProperty("identifier").GetInt32());
        Assert.Equal("IO_1", items[3].GetProperty("result").GetProperty("ports")[0].GetProperty("partners")[0].GetProperty("deviceName").GetString());
        Assert.Equal("Station_2", items[4].GetProperty("result").GetProperty("rightDeviceName").GetString());
    }
}
