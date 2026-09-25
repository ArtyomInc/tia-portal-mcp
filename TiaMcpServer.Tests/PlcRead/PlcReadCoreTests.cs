using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.ObjectModel;
using TiaMcpServer.OpennessWorker.PlcRead;
using TiaMcpServer.Tests.ObjectRead;
using Xunit;

namespace TiaMcpServer.Tests.PlcRead;

internal static class PlcFixture
{
    public const string ContainerType = "Siemens.Engineering.HW.Features.SoftwareContainer";

    public static FakeObjectNode Plc(string name, out FakeObjectNode software)
    {
        software = new FakeObjectNode(PlcLocator.PlcSoftwareType, name);
        return new FakeObjectNode("Siemens.Engineering.HW.DeviceItem", name)
            .WithService(ContainerType, new FakeObjectNode(ContainerType).WithAttribute("Software", software));
    }

    public static FakeObjectNode Block(string name, string kind, int number)
        => new FakeObjectNode("Siemens.Engineering.SW.Blocks." + kind, name)
            .WithAttribute("Number", number)
            .WithAttribute("IsConsistent", true)
            .WithAttribute("ModifiedDate", new DateTime(2026, 9, 25, 7, 0, 0, DateTimeKind.Utc))
            .WithAttribute("HeaderVersion", new Version(1, 2))
            .WithAttribute("Title", new FakeObjectNode("Siemens.Engineering.MultilingualText"));

    /// <summary>One PLC in a grouped station, with nested block groups, tables, TOs, and checksums.</summary>
    public static FakeObjectNode Project(out FakeObjectNode software)
    {
        var cpu = Plc("PLC_1", out software);
        var rack = new FakeObjectNode("Siemens.Engineering.HW.DeviceItem", "Rail_0");
        var station = new FakeObjectNode("Siemens.Engineering.HW.Device", "Station_1").Add("DeviceItems", rack, cpu);
        var line = new FakeObjectNode("Siemens.Engineering.HW.DeviceUserGroup", "Line")
            .Add("Devices", station)
            .Add("Groups");

        var subGroup = new FakeObjectNode("Siemens.Engineering.SW.Blocks.PlcBlockUserGroup", "Valves")
            .Add("Blocks", Block("fbValve", "FB", 1), Block("Main", "FC", 7))
            .Add("Groups");
        var systemGroup = new FakeObjectNode("Siemens.Engineering.SW.Blocks.PlcBlockSystemGroup", "System blocks")
            .Add("Blocks", Block("PID_Compact", "FB", 1130));
        var blocks = new FakeObjectNode("Siemens.Engineering.SW.Blocks.PlcBlockSystemGroup", "Program blocks")
            .Add("Blocks", Block("Main", "OB", 1))
            .Add("Groups", subGroup)
            .Add("SystemBlockGroups", systemGroup);

        var entry = new FakeObjectNode("Siemens.Engineering.SW.WatchAndForceTables.PlcWatchTableEntry", "Start")
            .WithAttribute("Address", "%I0.0")
            .WithAttribute("DisplayFormat", DayOfWeek.Monday)
            .WithAttribute("ModifyValue", "TRUE");
        var watch = new FakeObjectNode("Siemens.Engineering.SW.WatchAndForceTables.PlcWatchTable", "Commissioning")
            .Add("Entries", entry, new FakeObjectNode("Siemens.Engineering.SW.WatchAndForceTables.PlcWatchTableEntry", "Stop").WithAttribute("Address", "%I0.1"));
        var tables = new FakeObjectNode("Siemens.Engineering.SW.WatchAndForceTables.PlcWatchAndForceTableSystemGroup", "Watch")
            .Add("WatchTables", watch)
            .Add("ForceTables", new FakeObjectNode("Siemens.Engineering.SW.WatchAndForceTables.PlcForceTable", "Force table").Add("Entries"))
            .Add("Groups");

        var axis = new FakeObjectNode("Siemens.Engineering.SW.TechnologicalObjects.TechnologicalInstanceDB", "Axis_1")
            .WithAttribute("OfSystemLibElement", "TO_PositioningAxis")
            .Add(
                "Parameters",
                new FakeObjectNode("Siemens.Engineering.SW.TechnologicalObjects.TechnologicalParameter", "Actor.Type").WithAttribute("Value", 1),
                new FakeObjectNode("Siemens.Engineering.SW.TechnologicalObjects.TechnologicalParameter", "DynamicLimits.MaxVelocity").WithAttribute("Value", 250.5));
        var technologyObjects = new FakeObjectNode("Siemens.Engineering.SW.TechnologicalObjects.TechnologicalInstanceDBGroup", "Technology objects")
            .Add("TechnologicalObjects", axis)
            .Add("Groups");

        software
            .WithAttribute("BlockGroup", blocks)
            .WithAttribute("WatchAndForceTableGroup", tables)
            .WithAttribute("TechnologicalObjectGroup", technologyObjects)
            .WithService(
                "Siemens.Engineering.SW.PlcChecksumProvider",
                new FakeObjectNode("Siemens.Engineering.SW.PlcChecksumProvider").WithAttribute("Software", "E2 7F").WithAttribute("TextLists", "FA 70"));

        return new FakeObjectNode("Siemens.Engineering.Project", "Fixture")
            .Add("Devices", new FakeObjectNode("Siemens.Engineering.HW.Device", "HMI_1").Add("DeviceItems"))
            .Add("DeviceGroups", line);
    }
}

public class PlcLocatorTests
{
    [Fact]
    public void FindsPlcsInDeviceGroups_WithAResolvableObjectPath()
    {
        var project = PlcFixture.Project(out var software);
        var diagnostics = new List<string>();

        var plc = Assert.Single(PlcLocator.FindAll(project, diagnostics));

        Assert.Equal("PLC_1", plc.Summary.Name);
        Assert.Equal("Station_1", plc.Summary.DeviceName);
        Assert.Equal(new[] { "Line" }, plc.Summary.DeviceGroupPath);
        Assert.Same(software, ObjectPathResolver.Resolve(project, null, plc.Summary.ObjectPath));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Selection_IsExact_AndFailsClosed()
    {
        var project = PlcFixture.Project(out _);
        Assert.Equal("PLC_1", PlcLocator.Select(project, null, new()).Summary.Name);
        Assert.Equal("PLC_1", PlcLocator.Select(project, "PLC_1", new()).Summary.Name);

        var wrongCase = Assert.Throws<WorkerOperationException>(() => PlcLocator.Select(project, "plc_1", new()));
        Assert.Equal(WorkerFailureCategories.TargetNotFound, wrongCase.FailureCategory);
        Assert.Contains("'PLC_1'", wrongCase.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralPlcs_RequireAName_AndDuplicateNamesAreAmbiguous()
    {
        var project = new FakeObjectNode("Siemens.Engineering.Project")
            .Add(
                "Devices",
                new FakeObjectNode("Siemens.Engineering.HW.Device", "A").Add("DeviceItems", PlcFixture.Plc("PLC_A", out _)),
                new FakeObjectNode("Siemens.Engineering.HW.Device", "B").Add("DeviceItems", PlcFixture.Plc("PLC_B", out _)),
                new FakeObjectNode("Siemens.Engineering.HW.Device", "C").Add("DeviceItems", PlcFixture.Plc("PLC_B", out _)));

        Assert.Equal(
            WorkerFailureCategories.TargetAmbiguous,
            Assert.Throws<WorkerOperationException>(() => PlcLocator.Select(project, null, new())).FailureCategory);
        Assert.Equal("PLC_A", PlcLocator.Select(project, "PLC_A", new()).Summary.Name);
        Assert.Equal(
            WorkerFailureCategories.TargetAmbiguous,
            Assert.Throws<WorkerOperationException>(() => PlcLocator.Select(project, "PLC_B", new())).FailureCategory);
    }

    [Fact]
    public void AProjectWithoutPlcs_IsNotFound()
        => Assert.Equal(
            WorkerFailureCategories.TargetNotFound,
            Assert.Throws<WorkerOperationException>(() => PlcLocator.Select(new FakeObjectNode("Siemens.Engineering.Project"), null, new())).FailureCategory);
}

public class ObjectScalarNormalizerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("text", "text")]
    [InlineData(true, true)]
    [InlineData(7, 7L)]
    [InlineData((byte)3, 3L)]
    [InlineData(2.5f, 2.5d)]
    public void Scalars_NormalizeToJsonValues(object? input, object? expected)
    {
        Assert.True(ObjectScalarNormalizer.TryNormalize(input, out var json));
        Assert.Equal(expected, json);
    }

    [Fact]
    public void DatesEnumsVersionsAndArrays_NormalizeToStrings()
    {
        Assert.True(ObjectScalarNormalizer.TryNormalize(new DateTime(2026, 9, 25, 7, 0, 0, DateTimeKind.Utc), out var date));
        Assert.Equal("2026-09-25T07:00:00.0000000Z", date);
        Assert.True(ObjectScalarNormalizer.TryNormalize(DayOfWeek.Friday, out var symbol));
        Assert.Equal("Friday", symbol);
        Assert.True(ObjectScalarNormalizer.TryNormalize(new Version(4, 1), out var version));
        Assert.Equal("4.1", version);
        Assert.True(ObjectScalarNormalizer.TryNormalize(TimeSpan.FromMilliseconds(1500), out var span));
        Assert.Equal("00:00:01.5000000", span);
        Assert.True(ObjectScalarNormalizer.TryNormalize(new[] { "a", "b" }, out var array));
        Assert.Equal(new object?[] { "a", "b" }, Assert.IsType<List<object?>>(array));
        Assert.True(ObjectScalarNormalizer.TryNormalize(ulong.MaxValue, out var big));
        Assert.Equal("18446744073709551615", big);
    }

    [Fact]
    public void ObjectsNaNAndNestedArrays_AreNotRepresentable()
    {
        Assert.False(ObjectScalarNormalizer.TryNormalize(new FakeObjectNode("X"), out _));
        Assert.False(ObjectScalarNormalizer.TryNormalize(double.NaN, out _));
        Assert.False(ObjectScalarNormalizer.TryNormalize(new[] { new[] { 1 } }, out _));
        Assert.False(ObjectScalarNormalizer.TryNormalize(Enumerable.Range(0, 101).ToArray(), out _));
    }

    [Fact]
    public void ReadValues_SkipsUndeclared_AndReportsUnreadableNames()
    {
        var node = new FakeObjectNode("X", "x").WithAttribute("A", 1).WithAttribute("Obj", new FakeObjectNode("Y")).WithAttribute("B", "b");
        node.FailingValues.Add("B");
        var unavailable = new List<string>();

        var values = ObjectScalarNormalizer.ReadValues(node, new[] { "A", "B", "Obj", "Missing" }, unavailable);

        Assert.Equal(new Dictionary<string, object?> { ["A"] = 1L }, values);
        Assert.Equal(new[] { "B", "Obj" }, unavailable);
    }

    [Fact]
    public void ReadValues_WithoutNames_ReadsEveryDeclaredScalarAttribute()
    {
        var node = new FakeObjectNode("X").WithAttribute("B", 2).WithAttribute("A", "a").WithAttribute("Nav", new FakeObjectNode("Y"));
        var unavailable = new List<string>();

        var values = ObjectScalarNormalizer.ReadValues(node, null, unavailable);

        Assert.Equal(new[] { "A", "B" }, values.Keys);
        Assert.Empty(unavailable);
    }
}

public class PlcReadBuilderTests
{
    [Fact]
    public void ListBlocks_WalksUserAndSystemGroupsDepthFirst_WithGroupPathsAndValues()
    {
        var project = PlcFixture.Project(out _);

        var result = PlcReadBuilder.List(project, "list_blocks", null, null, null, null);

        Assert.Equal("PLC_1", result.PlcName);
        Assert.Equal(
            new[] { ("Main", "OB", ""), ("fbValve", "FB", "Valves"), ("Main", "FC", "Valves"), ("PID_Compact", "FB", "System blocks") },
            result.Items.Select(i => (i.Name, i.Kind, string.Join("/", i.GroupPath))));
        var main = result.Items[0];
        Assert.Equal(1L, main.Values["Number"]);
        Assert.Equal("2026-09-25T07:00:00.0000000Z", main.Values["ModifiedDate"]);
        Assert.Equal("1.2", main.Values["HeaderVersion"]);
        Assert.DoesNotContain("Title", main.Values.Keys);
        Assert.Null(result.NextCursor);

        foreach (var item in result.Items)
        {
            Assert.Equal(item.TypeName, ObjectPathResolver.Resolve(project, null, item.ObjectPath).TypeName);
        }
    }

    [Fact]
    public void Listings_FilterByName_AndPage()
    {
        var project = PlcFixture.Project(out _);

        var filtered = PlcReadBuilder.List(project, "list_blocks", "PLC_1", "MAIN", null, null);
        Assert.Equal(new[] { "OB", "FC" }, filtered.Items.Select(i => i.Kind));

        var first = PlcReadBuilder.List(project, "list_blocks", null, null, 3, null);
        Assert.Equal(3, first.Items.Count);
        var second = PlcReadBuilder.List(project, "list_blocks", null, null, 3, first.NextCursor);
        Assert.Equal("PID_Compact", Assert.Single(second.Items).Name);
        Assert.Equal(3, second.Offset);

        Assert.Equal(
            WorkerFailureCategories.CursorFilterMismatch,
            Assert.Throws<WorkerOperationException>(() => PlcReadBuilder.List(project, "list_blocks", null, "Main", 3, first.NextCursor)).FailureCategory);
    }

    [Fact]
    public void ListingAnUnofferedHierarchy_IsCapabilityUnavailable()
    {
        var project = PlcFixture.Project(out _);
        var exception = Assert.Throws<WorkerOperationException>(() => PlcReadBuilder.List(project, "list_types", null, null, null, null));
        Assert.Equal(WorkerFailureCategories.CapabilityUnavailable, exception.FailureCategory);
    }

    [Fact]
    public void ListWatchTables_IncludesWatchAndForceTables()
    {
        var result = PlcReadBuilder.List(PlcFixture.Project(out _), "list_watch_tables", null, null, null, null);
        Assert.Equal(new[] { ("Commissioning", "PlcWatchTable"), ("Force table", "PlcForceTable") }, result.Items.Select(i => (i.Name, i.Kind)));
    }

    [Fact]
    public void ReadWatchTable_ReturnsDeclaredEntryValues()
    {
        var result = PlcReadBuilder.ReadWatchTable(PlcFixture.Project(out _), null, "Commissioning", null, null, null);

        Assert.Equal("Commissioning", result.Target.Name);
        Assert.Equal(2, result.TotalCount);
        var start = result.Entries[0];
        Assert.Equal("Start", start.Name);
        Assert.Equal("%I0.0", start.Values["Address"]);
        Assert.Equal("Monday", start.Values["DisplayFormat"]);
        Assert.Equal(new[] { "Address" }, result.Entries[1].Values.Keys);
    }

    [Fact]
    public void ReadWatchTable_UnknownNameOrGroup_IsNotFound()
    {
        var project = PlcFixture.Project(out _);
        Assert.Equal(
            WorkerFailureCategories.TargetNotFound,
            Assert.Throws<WorkerOperationException>(() => PlcReadBuilder.ReadWatchTable(project, null, "Nope", null, null, null)).FailureCategory);
        Assert.Equal(
            WorkerFailureCategories.TargetNotFound,
            Assert.Throws<WorkerOperationException>(() => PlcReadBuilder.ReadWatchTable(project, null, "Commissioning", new[] { "Other" }, null, null)).FailureCategory);
    }

    [Fact]
    public void SelectOne_RequiresGroupPathWhenNamesRepeat()
    {
        var project = PlcFixture.Project(out _);
        var plc = PlcLocator.Select(project, null, new());
        var listing = PlcReadBuilder.Listings["list_blocks"];

        Assert.Equal(
            WorkerFailureCategories.TargetAmbiguous,
            Assert.Throws<WorkerOperationException>(() => PlcReadBuilder.SelectOne(plc, listing, "Main", null, "block", new())).FailureCategory);
        Assert.Equal("FC", PlcReadBuilder.SelectOne(plc, listing, "Main", new[] { "Valves" }, "block", new()).Info.Kind);
        Assert.Equal("OB", PlcReadBuilder.SelectOne(plc, listing, "Main", Array.Empty<string>(), "block", new()).Info.Kind);
    }

    [Fact]
    public void ReadTechnologyObject_ReturnsParameters_AndReportsUnknownNames()
    {
        var project = PlcFixture.Project(out _);

        var all = PlcReadBuilder.ReadTechnologyObject(project, null, "Axis_1", null, null, null, null);
        Assert.Equal(new[] { "Actor.Type", "DynamicLimits.MaxVelocity" }, all.Entries.Select(e => e.Name));
        Assert.Equal(250.5d, all.Entries[1].Values["Value"]);
        Assert.Equal("TO_PositioningAxis", all.Target.Values["OfSystemLibElement"]);

        var some = PlcReadBuilder.ReadTechnologyObject(project, null, "Axis_1", null, new[] { "Actor.Type", "Nope" }, null, null);
        Assert.Equal("Actor.Type", Assert.Single(some.Entries).Name);
        Assert.Contains(some.Diagnostics, d => d.Contains("'Nope'", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadChecksums_ReadsTheProvider()
    {
        var result = PlcReadBuilder.ReadChecksums(PlcFixture.Project(out _), null);
        Assert.Equal(("E2 7F", "FA 70"), (result.Software, result.TextLists));
    }

    [Fact]
    public void ListPlcs_ReportsEveryPlc()
        => Assert.Equal("PLC_1", Assert.Single(PlcReadBuilder.ListPlcs(PlcFixture.Project(out _)).Plcs).Name);

    [Fact]
    public void EveryListingOperation_HasASpec()
    {
        foreach (var operation in TiaMcpServer.PlcRead.PlcReadCatalog.ListingOperations)
        {
            Assert.True(PlcReadBuilder.Listings.ContainsKey(operation), operation);
        }
    }
}
