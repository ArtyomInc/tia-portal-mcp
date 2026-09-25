using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.ObjectModel;
using Xunit;

namespace TiaMcpServer.Tests.ObjectRead;

public class ObjectChildrenPagerTests
{
    private static FakeObjectNode Folder(int count, string composition = "Items")
        => new FakeObjectNode("Folder").Add(
            composition,
            Enumerable.Range(0, count).Select(i => new FakeObjectNode("Item", $"Item_{i}")).ToArray());

    private static string Category(Action action) => Assert.Throws<WorkerOperationException>(action).FailureCategory;

    [Fact]
    public void Children_AreOrderedByCompositionThenEnumeration_AndCarryResolvablePaths()
    {
        var project = new FakeObjectNode("Siemens.Engineering.Project")
            .Add("Devices", new FakeObjectNode("Device", "PLC_1"), new FakeObjectNode("Device"))
            .Add("DeviceGroups", new FakeObjectNode("Group", "Line"));

        var page = ObjectChildrenPager.Build(project, null, null, null, null, null);

        Assert.Equal(new[] { "DeviceGroups", "Devices", "Devices" }, page.Children.Select(c => c.Composition));
        Assert.Equal(new[] { 0, 0, 1 }, page.Children.Select(c => c.Index));
        Assert.Equal(3, page.TotalCount);
        Assert.Null(page.NextCursor);

        var unnamed = page.Children[2];
        Assert.Null(unnamed.Name);
        var step = Assert.Single(unnamed.ObjectPath);
        Assert.Null(step.ElementName);
        Assert.Equal(1, step.Index);

        foreach (var child in page.Children)
        {
            Assert.Equal(child.TypeName, ObjectPathResolver.Resolve(project, null, child.ObjectPath).TypeName);
        }
    }

    [Fact]
    public void ChildPaths_ExtendTheListedPathWithoutAliasingIt()
    {
        var project = FakeObjectNode.Project(out _);
        var path = new List<ObjectPathSegmentInfo>
        {
            new() { Kind = ObjectPathSegmentKinds.Composition, Name = "Devices", ElementName = "PLC_1" },
        };
        var node = ObjectPathResolver.Resolve(project, null, path);

        var page = ObjectChildrenPager.Build(node, null, path, null, null, null);
        var child = Assert.Single(page.Children);

        Assert.Equal(2, child.ObjectPath.Count);
        Assert.NotSame(path[0], child.ObjectPath[0]);
        Assert.Equal("DeviceItems", child.ObjectPath[1].Name);
        Assert.Equal("PLC_1", child.ObjectPath[1].ElementName);
    }

    [Fact]
    public void Paging_WalksEveryChildExactlyOnce()
    {
        var folder = Folder(7);
        var seen = new List<string?>();
        string? cursor = null;
        do
        {
            var page = ObjectChildrenPager.Build(folder, null, null, null, 3, cursor);
            Assert.Equal(7, page.TotalCount);
            seen.AddRange(page.Children.Select(c => c.Name));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(Enumerable.Range(0, 7).Select(i => $"Item_{i}"), seen);
    }

    [Fact]
    public void Cursor_IsRejectedForAnotherQuery()
    {
        var folder = Folder(5).Add("Other", new FakeObjectNode("X", "x"));
        var first = ObjectChildrenPager.Build(folder, null, null, new[] { "Items" }, 2, null);

        Assert.Equal(
            WorkerFailureCategories.CursorFilterMismatch,
            Category(() => ObjectChildrenPager.Build(folder, null, null, null, 2, first.NextCursor)));
        Assert.Equal(
            WorkerFailureCategories.CursorFilterMismatch,
            Category(() => ObjectChildrenPager.Build(folder, ObjectRoots.Portal, null, new[] { "Items" }, 2, first.NextCursor)));
    }

    [Fact]
    public void Cursor_IsRejectedAfterTheChildrenChanged()
    {
        var folder = Folder(5);
        var first = ObjectChildrenPager.Build(folder, null, null, null, 2, null);
        folder.Add("Items", new FakeObjectNode("Item", "Late"));

        Assert.Equal(
            WorkerFailureCategories.CursorSnapshotMismatch,
            Category(() => ObjectChildrenPager.Build(folder, null, null, null, 2, first.NextCursor)));
    }

    [Fact]
    public void RenamingAChild_ChangesTheSnapshot()
    {
        var item = new FakeObjectNode("Item", "Before");
        var folder = new FakeObjectNode("Folder").Add("Items", item, new FakeObjectNode("Item", "B"), new FakeObjectNode("Item", "C"));
        var first = ObjectChildrenPager.Build(folder, null, null, null, 1, null);
        item.Name = "After";

        Assert.Equal(
            WorkerFailureCategories.CursorSnapshotMismatch,
            Category(() => ObjectChildrenPager.Build(folder, null, null, null, 1, first.NextCursor)));
    }

    [Theory]
    [InlineData("not a cursor!")]
    [InlineData("e30")]
    [InlineData("eyJ2IjoxfQ")]
    public void MalformedCursor_IsInvalid(string cursor)
        => Assert.Equal(
            WorkerFailureCategories.InvalidCursor,
            Category(() => ObjectChildrenPager.Build(Folder(3), null, null, null, 1, cursor)));

    [Fact]
    public void CursorPastTheEnd_IsOutOfRange()
    {
        var folder = Folder(3);
        var query = OffsetCursorCodec.Hash(new string?[] { "object-children-v1", ObjectRoots.Project, null });
        var snapshot = OffsetCursorCodec.Hash(Enumerable.Range(0, 3).SelectMany(i => new string?[] { "Items", i.ToString(), $"Item_{i}", "Item" }));
        var cursor = OffsetCursorCodec.Encode(3, query, snapshot);

        Assert.Equal(
            WorkerFailureCategories.CursorOutOfRange,
            Category(() => ObjectChildrenPager.Build(folder, null, null, null, 1, cursor)));
    }

    [Fact]
    public void CompositionFilter_SelectsOnlyNamedCompositions_AndRejectsUnknownNames()
    {
        var folder = Folder(2).Add("Other", new FakeObjectNode("X", "x"));

        var page = ObjectChildrenPager.Build(folder, null, null, new[] { "Other" }, null, null);
        Assert.Equal("x", Assert.Single(page.Children).Name);

        Assert.Equal(
            WorkerFailureCategories.TargetNotFound,
            Category(() => ObjectChildrenPager.Build(folder, null, null, new[] { "Missing" }, null, null)));
        Assert.Equal(
            WorkerFailureCategories.ValidationError,
            Category(() => ObjectChildrenPager.Build(folder, null, null, new[] { "Other", "Other" }, null, null)));
    }

    [Fact]
    public void PortalRoot_HidesProjectsFromTheListing()
    {
        var portal = new FakeObjectNode("Siemens.Engineering.TiaPortal")
            .Add("Projects", new FakeObjectNode("Siemens.Engineering.Project", "Other"))
            .Add("GlobalLibraries", new FakeObjectNode("L", "Lib"));

        var page = ObjectChildrenPager.Build(portal, ObjectRoots.Portal, null, null, null, null);
        Assert.Equal("GlobalLibraries", Assert.Single(page.Children).Composition);

        Assert.Equal(
            WorkerFailureCategories.AccessDenied,
            Category(() => ObjectChildrenPager.Build(portal, ObjectRoots.Portal, null, new[] { "Projects" }, null, null)));
    }

    [Fact]
    public void OversizedCompositions_FailWithoutEnumeratingEverything()
    {
        var folder = Folder(ObjectReadLimits.MaxChildrenSnapshot + 5);

        Assert.Equal(
            WorkerFailureCategories.SnapshotTooLarge,
            Category(() => ObjectChildrenPager.Build(folder, null, null, null, null, null)));
        Assert.True(folder.EnumeratedElements <= ObjectReadLimits.MaxChildrenSnapshot + 1);
    }

    [Fact]
    public void AnUnenumerableComposition_BecomesADiagnostic()
    {
        var folder = Folder(2).Add("Broken", new FakeObjectNode("X", "x"));
        folder.FailingCompositions.Add("Broken");

        var page = ObjectChildrenPager.Build(folder, null, null, null, null, null);

        Assert.Equal(2, page.Children.Count);
        Assert.Contains(page.Diagnostics, d => d.Contains("'Broken'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void PageSize_OutOfBounds_IsRejected(int pageSize)
        => Assert.Equal(
            WorkerFailureCategories.ValidationError,
            Category(() => ObjectChildrenPager.Build(Folder(1), null, null, null, pageSize, null)));

    [Fact]
    public void EmptyObject_ReturnsAnEmptyLastPage()
    {
        var page = ObjectChildrenPager.Build(new FakeObjectNode("Leaf"), null, null, null, null, null);
        Assert.Empty(page.Children);
        Assert.Equal(0, page.TotalCount);
        Assert.Null(page.NextCursor);
    }
}
