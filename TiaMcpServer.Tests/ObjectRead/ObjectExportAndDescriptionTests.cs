using System.Security.Cryptography;
using System.Text;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.ObjectModel;
using Xunit;

namespace TiaMcpServer.Tests.ObjectRead;

public class ObjectExportWindowTests
{
    private static ObjectExportInfo Window(string document, int? offset, int? maxChars)
    {
        var result = new ObjectExportInfo();
        ObjectExportWindow.Apply(result, document, offset, maxChars);
        return result;
    }

    [Fact]
    public void Windows_ReassembleTheWholeDocument_AndShareOneDigest()
    {
        var document = string.Concat(Enumerable.Range(0, 500).Select(i => $"<Tag n=\"{i}\"/>"));
        var expectedDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document))).ToLowerInvariant();
        var builder = new StringBuilder();
        int? offset = 0;
        while (offset is not null)
        {
            var window = Window(document, offset, 1000);
            Assert.Equal(expectedDigest, window.Sha256);
            Assert.Equal(document.Length, window.TotalChars);
            Assert.Equal(offset, window.Offset);
            builder.Append(window.Content);
            offset = window.NextOffset;
        }

        Assert.Equal(document, builder.ToString());
    }

    [Fact]
    public void DefaultWindow_Is16000Characters()
    {
        var window = Window(new string('x', 40_000), null, null);
        Assert.Equal(ObjectReadLimits.DefaultExportMaxChars, window.Content.Length);
        Assert.Equal(16_000, window.NextOffset);
    }

    [Fact]
    public void SurrogatePairs_AreNeverSplit()
    {
        var document = "ab\U0001F600cd";
        var first = Window(document, 0, 3);
        Assert.Equal("ab", first.Content);
        Assert.Equal(2, first.NextOffset);
        Assert.Equal("\U0001F600c", Window(document, 2, 3).Content);
    }

    [Fact]
    public void EmptyDocument_IsOneEmptyLastWindow()
    {
        var window = Window(string.Empty, null, null);
        Assert.Equal(string.Empty, window.Content);
        Assert.Null(window.NextOffset);
        Assert.Equal(0, window.TotalChars);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 30_001)]
    public void OutOfBoundsWindows_AreValidationErrors(int offset, int maxChars)
    {
        var exception = Assert.Throws<WorkerOperationException>(() => Window("0123456789", offset, maxChars));
        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }
}

public class ObjectDescriptionBuilderTests
{
    [Fact]
    public void Description_IsSorted_AndFlagsNavigationAndServices()
    {
        var project = FakeObjectNode.Project(out _);
        var cpu = ObjectPathResolver.Resolve(project, null, new[]
        {
            new ObjectPathSegmentInfo { Kind = ObjectPathSegmentKinds.Composition, Name = "Devices", ElementName = "PLC_1" },
            new ObjectPathSegmentInfo { Kind = ObjectPathSegmentKinds.Composition, Name = "DeviceItems", Index = 0 },
        });
        ((FakeObjectNode)cpu).WithAttribute("Parent", new FakeObjectNode("Device"));

        var description = ObjectDescriptionBuilder.Build(cpu, null, null);

        Assert.Equal("PLC_1", description.Name);
        Assert.Equal(new[] { "Comment", "OrderNumber", "Parent" }, description.Attributes.Select(a => a.Name));
        Assert.True(description.Attributes.Single(a => a.Name == "Parent").Navigable);
        Assert.False(description.Attributes.Single(a => a.Name == "OrderNumber").Navigable);
        Assert.Equal(new[] { "OnlineProvider", "SoftwareContainer" }, description.Services.Select(s => s.Name));
        Assert.False(description.Services[0].Allowed);
        Assert.True(description.Services[1].Allowed);
        Assert.Equal("Siemens.Engineering.HW.Features.SoftwareContainer", description.Services[1].TypeName);
        Assert.Empty(description.Diagnostics);
    }

    [Fact]
    public void PortalRootDescription_HidesProjects()
    {
        var portal = new FakeObjectNode("Siemens.Engineering.TiaPortal")
            .Add("Projects", new FakeObjectNode("P"))
            .Add("GlobalLibraries");

        var description = ObjectDescriptionBuilder.Build(portal, ObjectRoots.Portal, null);

        Assert.Equal("GlobalLibraries", Assert.Single(description.Compositions).Name);
        Assert.Equal(ObjectRoots.Portal, description.Root);
    }

    [Fact]
    public void AFailingMemberFamily_BecomesADiagnostic_AndKeepsTheOthers()
    {
        var node = new FakeObjectNode("X", "x") { FailAttributes = true, IsExportable = true }.Add("Items");

        var description = ObjectDescriptionBuilder.Build(node, null, null);

        Assert.Empty(description.Attributes);
        Assert.Equal("Items", Assert.Single(description.Compositions).Name);
        Assert.True(description.Exportable);
        Assert.Contains(description.Diagnostics, d => d.Contains("attributes", StringComparison.Ordinal));
    }
}
