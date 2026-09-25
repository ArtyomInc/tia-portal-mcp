using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.ObjectModel;
using Xunit;

namespace TiaMcpServer.Tests.ObjectRead;

public class ObjectPathResolverTests
{
    private static ObjectPathSegmentInfo Composition(string name, string? elementName = null, int? index = null)
        => new() { Kind = ObjectPathSegmentKinds.Composition, Name = name, ElementName = elementName, Index = index };

    private static ObjectPathSegmentInfo Attribute(string name) => new() { Kind = ObjectPathSegmentKinds.Attribute, Name = name };

    private static ObjectPathSegmentInfo Service(string name) => new() { Kind = ObjectPathSegmentKinds.Service, Name = name };

    private static string Category(Action action) => Assert.Throws<WorkerOperationException>(action).FailureCategory;

    [Fact]
    public void EmptyOrNullPath_ResolvesTheRoot()
    {
        var project = FakeObjectNode.Project(out _);
        Assert.Same(project, ObjectPathResolver.Resolve(project, null, null));
        Assert.Same(project, ObjectPathResolver.Resolve(project, ObjectRoots.Project, new List<ObjectPathSegmentInfo>()));
    }

    [Fact]
    public void CompositionService_AndAttributeSteps_ReachPlcSoftware()
    {
        var project = FakeObjectNode.Project(out var software);
        var resolved = ObjectPathResolver.Resolve(project, null, new[]
        {
            Composition("Devices", "PLC_1"),
            Composition("DeviceItems", index: 0),
            Service("SoftwareContainer"),
            Attribute("Software"),
        });

        Assert.Same(software, resolved);
    }

    [Fact]
    public void ServiceStep_AcceptsTheFullTypeName()
    {
        var project = FakeObjectNode.Project(out var software);
        var resolved = ObjectPathResolver.Resolve(project, null, new[]
        {
            Composition("Devices", index: 0),
            Composition("DeviceItems", "PLC_1", 0),
            Service("Siemens.Engineering.HW.Features.SoftwareContainer"),
            Attribute("Software"),
        });

        Assert.Same(software, resolved);
    }

    [Fact]
    public void IndexWithMatchingName_Resolves_AndMismatchedName_FailsEvidence()
    {
        var project = FakeObjectNode.Project(out _);
        var hmi = ObjectPathResolver.Resolve(project, null, new[] { Composition("Devices", "HMI_1", 1) });
        Assert.Equal("HMI_1", hmi.TryReadName());

        Assert.Equal(
            WorkerFailureCategories.TargetEvidenceMismatch,
            Category(() => ObjectPathResolver.Resolve(project, null, new[] { Composition("Devices", "PLC_1", 1) })));
    }

    [Fact]
    public void DuplicateElementNames_WithoutIndex_AreAmbiguous()
    {
        var project = new FakeObjectNode("P").Add("Devices", new FakeObjectNode("D", "Twin"), new FakeObjectNode("D", "Twin"));
        Assert.Equal(
            WorkerFailureCategories.TargetAmbiguous,
            Category(() => ObjectPathResolver.Resolve(project, null, new[] { Composition("Devices", "Twin") })));

        var second = ObjectPathResolver.Resolve(project, null, new[] { Composition("Devices", "Twin", 1) });
        Assert.Equal("Twin", second.TryReadName());
    }

    [Theory]
    [InlineData("Nope", null, 0)]
    [InlineData("Devices", "Missing", null)]
    [InlineData("Devices", null, 7)]
    public void MissingCompositionOrElement_IsNotFound(string composition, string? elementName, int? index)
    {
        var project = FakeObjectNode.Project(out _);
        Assert.Equal(
            WorkerFailureCategories.TargetNotFound,
            Category(() => ObjectPathResolver.Resolve(project, null, new[] { Composition(composition, elementName, index) })));
    }

    [Fact]
    public void AttributeSteps_FailClosedOnUndeclaredNullAndScalarValues()
    {
        var project = FakeObjectNode.Project(out _);
        var cpu = new[] { Composition("Devices", "PLC_1"), Composition("DeviceItems", index: 0) };

        Assert.Equal(
            WorkerFailureCategories.TargetNotFound,
            Category(() => ObjectPathResolver.Resolve(project, null, cpu.Append(Attribute("Undeclared")).ToList())));
        Assert.Equal(
            WorkerFailureCategories.TargetNotFound,
            Category(() => ObjectPathResolver.Resolve(project, null, cpu.Append(Attribute("Comment")).ToList())));
        Assert.Equal(
            WorkerFailureCategories.TargetKindUnsupported,
            Category(() => ObjectPathResolver.Resolve(project, null, cpu.Append(Attribute("OrderNumber")).ToList())));
    }

    [Fact]
    public void OnlineServices_AreDenied_AndUnknownServices_AreNotFound()
    {
        var project = FakeObjectNode.Project(out _);
        var cpu = new[] { Composition("Devices", "PLC_1"), Composition("DeviceItems", index: 0) };

        Assert.Equal(
            WorkerFailureCategories.AccessDenied,
            Category(() => ObjectPathResolver.Resolve(project, null, cpu.Append(Service("OnlineProvider")).ToList())));
        Assert.Equal(
            WorkerFailureCategories.TargetNotFound,
            Category(() => ObjectPathResolver.Resolve(project, null, cpu.Append(Service("Nothing")).ToList())));
    }

    [Fact]
    public void AmbiguousServiceSimpleName_RequiresTheFullName()
    {
        var node = new FakeObjectNode("X")
            .WithService("A.Provider", new FakeObjectNode("A.Provider"))
            .WithService("B.Provider", new FakeObjectNode("B.Provider"));

        Assert.Equal(
            WorkerFailureCategories.TargetAmbiguous,
            Category(() => ObjectPathResolver.Resolve(node, null, new[] { Service("Provider") })));
        Assert.Equal("B.Provider", ObjectPathResolver.Resolve(node, null, new[] { Service("B.Provider") }).TypeName);
    }

    [Fact]
    public void ServiceReturningNoInstance_IsNotFound()
    {
        var node = new FakeObjectNode("X").WithService("A.Provider", null);
        Assert.Equal(
            WorkerFailureCategories.TargetNotFound,
            Category(() => ObjectPathResolver.Resolve(node, null, new[] { Service("Provider") })));
    }

    [Fact]
    public void PortalProjects_AreHiddenOnEveryPortalObject()
    {
        var portal = new FakeObjectNode("Siemens.Engineering.TiaPortal")
            .Add("Projects", new FakeObjectNode("Siemens.Engineering.Project", "Other"))
            .Add("GlobalLibraries", new FakeObjectNode("L", "Lib").Add("Projects", new FakeObjectNode("Y", "Inner")));

        Assert.Equal(
            WorkerFailureCategories.AccessDenied,
            Category(() => ObjectPathResolver.Resolve(portal, ObjectRoots.Portal, new[] { Composition("Projects", index: 0) })));
        Assert.Equal(
            WorkerFailureCategories.AccessDenied,
            Category(() => ObjectPathResolver.Resolve(portal, ObjectRoots.Project, new[] { Composition("Projects", index: 0) })));

        // The same composition name on a non-Portal object is ordinary.
        Assert.Equal("Inner", ObjectPathResolver.Resolve(
            portal,
            ObjectRoots.Portal,
            new[] { Composition("GlobalLibraries", "Lib"), Composition("Projects", "Inner") }).TryReadName());
    }

    [Fact]
    public void AnyStepReachingAProject_IsDenied()
    {
        var other = new FakeObjectNode("Siemens.Engineering.Project", "Other");
        var session = new FakeObjectNode("Siemens.Engineering.Multiuser.LocalSession", "S").WithAttribute("Project", other);
        var portal = new FakeObjectNode("Siemens.Engineering.TiaPortal").Add("LocalSessions", session);

        Assert.Equal(
            WorkerFailureCategories.AccessDenied,
            Category(() => ObjectPathResolver.Resolve(portal, ObjectRoots.Portal, new[] { Composition("LocalSessions", "S"), Attribute("Project") })));
        Assert.True(ObjectPathRules.IsProjectType("Siemens.Engineering.Multiuser.MultiuserProject"));
    }

    [Fact]
    public void ParentSteps_AreDenied()
    {
        var project = FakeObjectNode.Project(out _);
        var device = (FakeObjectNode)ObjectPathResolver.Resolve(project, null, new[] { Composition("Devices", "PLC_1") });
        device.WithAttribute("Parent", project);

        Assert.Equal(
            WorkerFailureCategories.AccessDenied,
            Category(() => ObjectPathResolver.Resolve(project, null, new[] { Composition("Devices", "PLC_1"), Attribute("Parent") })));
    }

    [Fact]
    public void StructurallyInvalidPaths_AreValidationErrors()
    {
        var project = FakeObjectNode.Project(out _);
        var tooLong = Enumerable.Range(0, ObjectReadLimits.MaxPathSegments + 1).Select(_ => Composition("Devices", index: 0)).ToList();

        Assert.Equal(WorkerFailureCategories.ValidationError, Category(() => ObjectPathResolver.Resolve(project, "elsewhere", null)));
        Assert.Equal(WorkerFailureCategories.ValidationError, Category(() => ObjectPathResolver.Resolve(project, null, tooLong)));
        Assert.Equal(WorkerFailureCategories.ValidationError, Category(() => ObjectPathResolver.Resolve(project, null, new[] { Composition("Devices") })));
        Assert.Equal(WorkerFailureCategories.ValidationError, Category(() => ObjectPathResolver.Resolve(project, null, new[] { Composition("Devices", index: -1) })));
        Assert.Equal(WorkerFailureCategories.ValidationError, Category(() => ObjectPathResolver.Resolve(project, null, new[]
        {
            new ObjectPathSegmentInfo { Kind = ObjectPathSegmentKinds.Attribute, Name = "Comment", Index = 0 },
        })));
        Assert.Equal(WorkerFailureCategories.ValidationError, Category(() => ObjectPathResolver.Resolve(project, null, new[]
        {
            new ObjectPathSegmentInfo { Kind = "method", Name = "Delete" },
        })));
        Assert.Equal(WorkerFailureCategories.ValidationError, Category(() => ObjectPathResolver.Resolve(project, null, new[]
        {
            new ObjectPathSegmentInfo { Kind = ObjectPathSegmentKinds.Composition, Name = " ", Index = 0 },
        })));
    }

    [Fact]
    public void FailureMessages_NameTheFailingStep()
    {
        var project = FakeObjectNode.Project(out _);
        var exception = Assert.Throws<WorkerOperationException>(() => ObjectPathResolver.Resolve(
            project,
            null,
            new[] { Composition("Devices", "PLC_1"), Composition("Missing", index: 0) }));

        Assert.StartsWith("ObjectPath segment 1:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceRules_DenyOnlineDownloadAndUpload()
    {
        Assert.False(ObjectPathRules.IsServiceAllowed("Siemens.Engineering.Online.OnlineProvider"));
        Assert.False(ObjectPathRules.IsServiceAllowed("Siemens.Engineering.Download.DownloadProvider"));
        Assert.False(ObjectPathRules.IsServiceAllowed("Siemens.Engineering.Upload.UploadProvider"));
        Assert.True(ObjectPathRules.IsServiceAllowed("Siemens.Engineering.HW.Features.SoftwareContainer"));
        Assert.Equal("SoftwareContainer", ObjectPathRules.SimpleName("Siemens.Engineering.HW.Features.SoftwareContainer"));
        Assert.Equal("Inner", ObjectPathRules.SimpleName("Outer+Inner"));
    }
}
