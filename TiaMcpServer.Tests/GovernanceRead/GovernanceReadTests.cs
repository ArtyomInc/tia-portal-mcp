using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.GovernanceRead;
using TiaMcpServer.Json;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.GovernanceRead;
using TiaMcpServer.OpennessWorker.ObjectModel;
using TiaMcpServer.Tests.ObjectRead;
using TiaMcpServer.Tests.PlcRead;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.GovernanceRead;

public class GovernanceReadBuilderTests
{
    private static FakeObjectNode Umac()
    {
        var right = new FakeObjectNode("Siemens.Engineering.Umac.EngineeringFunctionRight", "Open project").WithAttribute("Identifier", "OPEN");
        var role = new FakeObjectNode("Siemens.Engineering.Umac.CustomRole", "Engineer")
            .WithAttribute("Identifier", "ENG")
            .WithAttribute("AssignedEngineeringRights", new List<FakeObjectNode> { right });
        var user = new FakeObjectNode("Siemens.Engineering.Umac.ProjectUser", "alice")
            .WithAttribute("IsActive", true)
            .WithAttribute("SessionTimeOut", 30)
            .WithAttribute("Roles", new List<FakeObjectNode> { role });
        return new FakeObjectNode("Siemens.Engineering.Umac.UmacConfigurator")
            .Add("ProjectUsers", user)
            .Add("CustomRoles", role)
            .Add("SystemRoles")
            .Add("EngineeringFunctionRights", right);
    }

    [Fact]
    public void ReadUmac_ListsSectionsWithRoleAndRightReferences()
    {
        var project = new FakeObjectNode("Siemens.Engineering.Project", "test")
            .WithService("Siemens.Engineering.Umac.UmacConfigurator", Umac());

        var result = GovernanceReadBuilder.ReadProjectService(project, "UmacConfigurator", GovernanceReadBuilder.UmacSections);

        var users = result.Sections.Single(s => s.Name == "projectUsers");
        var alice = Assert.Single(users.Items);
        Assert.Equal(new[] { "Engineer" }, alice.References["Roles"]);
        Assert.Equal((true, 30L), (alice.Values["IsActive"], alice.Values["SessionTimeOut"]));
        Assert.Equal(new[] { "Open project" }, result.Sections.Single(s => s.Name == "customRoles").Items[0].References["AssignedEngineeringRights"]);
        Assert.Empty(result.Sections.Single(s => s.Name == "systemRoles").Items);
        Assert.Empty(result.Sections.Single(s => s.Name == "umcUsers").Items);
        Assert.Equal("alice", ObjectPathResolver.Resolve(project, null, alice.ObjectPath).TryReadName());
    }

    [Fact]
    public void AMissingService_IsCapabilityUnavailable()
    {
        var project = new FakeObjectNode("Siemens.Engineering.Project", "test");
        var exception = Assert.Throws<WorkerOperationException>(() => GovernanceReadBuilder.ReadProjectService(project, "TestSuiteService", GovernanceReadBuilder.TestSuiteSections));
        Assert.Equal(WorkerFailureCategories.CapabilityUnavailable, exception.FailureCategory);
    }

    [Fact]
    public void ReadSafety_ReadsTheCpuServiceStateSettingsAndSignatures()
    {
        var project = PlcFixture.Project(out _);
        var cpu = (FakeObjectNode)ObjectPathResolver.Resolve(project, null, PlcLocatorPath(project));
        var signatures = new FakeObjectNode("Siemens.Engineering.Safety.SafetySignatureProvider")
            .Add("Signatures", new FakeObjectNode("Siemens.Engineering.Safety.SafetySignature").WithAttribute("Value", "0x1234ABCD"));
        cpu.WithService(
            "Siemens.Engineering.Safety.SafetyAdministration",
            new FakeObjectNode("Siemens.Engineering.Safety.SafetyAdministration")
                .WithAttribute("IsLoggedOnToSafetyOfflineProgram", false)
                .WithAttribute("IsSafetyOfflineProgramPasswordSet", true)
                .WithAttribute("ProgramSignatures", signatures)
                .WithAttribute("Settings", new FakeObjectNode("Siemens.Engineering.Safety.SafetySettings").WithAttribute("ActivationOfFChangeHistory", true))
                .Add("RuntimeGroups", new FakeObjectNode("Siemens.Engineering.Safety.RuntimeGroup", "F-runtime group 1").WithAttribute("MaximumCycleTime", 400)));

        var result = GovernanceReadBuilder.ReadSafety(project, null);

        Assert.Equal("PLC_1", result.Scope);
        Assert.Equal(true, result.Values["IsSafetyOfflineProgramPasswordSet"]);
        Assert.Equal(true, result.Values["Settings.ActivationOfFChangeHistory"]);
        Assert.Equal("0x1234ABCD", result.Sections.Single(s => s.Name == "programSignatures").Items[0].Values["Value"]);
        Assert.Equal(400L, result.Sections.Single(s => s.Name == "runtimeGroups").Items[0].Values["MaximumCycleTime"]);
    }

    [Fact]
    public void ReadSafety_OnAStandardCpu_IsCapabilityUnavailable()
    {
        var project = PlcFixture.Project(out _);
        Assert.Equal(
            WorkerFailureCategories.CapabilityUnavailable,
            Assert.Throws<WorkerOperationException>(() => GovernanceReadBuilder.ReadSafety(project, null)).FailureCategory);
    }

    [Fact]
    public void ReadPortal_NeverListsProjects()
    {
        var session = new FakeObjectNode("Siemens.Engineering.Multiuser.LocalSession", "Line A").WithAttribute("Project", new FakeObjectNode("Siemens.Engineering.Multiuser.MultiuserProject", "Line A"));
        var portal = new FakeObjectNode("Siemens.Engineering.TiaPortal")
            .Add("ProjectServers", new FakeObjectNode("Siemens.Engineering.Multiuser.ProjectServer").WithAttribute("Host", "srv01").WithAttribute("Port", 8735))
            .Add("LocalSessions", session)
            .Add("Projects", new FakeObjectNode("Siemens.Engineering.Project", "Other"));

        var result = GovernanceReadBuilder.ReadPortal(portal, GovernanceReadBuilder.MultiuserSections);

        Assert.Equal(ObjectRoots.Portal, result.Root);
        Assert.Equal(("srv01", 8735L), ((string)result.Sections[0].Items[0].Values["Host"]!, (long)result.Sections[0].Items[0].Values["Port"]!));
        var local = Assert.Single(result.Sections[1].Items);
        Assert.DoesNotContain("Project", local.Values.Keys);
        Assert.DoesNotContain(result.Sections, s => s.Items.Any(i => i.Kind == "Project"));
    }

    private static List<ObjectPathSegmentInfo> PlcLocatorPath(FakeObjectNode project)
    {
        var plc = TiaMcpServer.OpennessWorker.PlcRead.PlcLocator.Select(project, null, new());
        return plc.Path.Take(plc.Path.Count - 2).ToList();
    }
}

public class GovernanceReadHostTests
{
    [Fact]
    public void Catalog_DeclaresSixObserveOperations()
    {
        Assert.Equal(6, GovernanceReadCatalog.OperationNames.Count);
        Assert.All(GovernanceReadCatalog.OperationNames, name => Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(name)));
        Assert.Contains(
            "'plcName' is not valid for read_umac",
            GovernanceReadCatalog.Instance.Validate(new[] { new GovernanceReadOperationRequest { OperationId = "a", Operation = "read_umac", PlcName = "PLC_1" } }).Error);
    }

    [Theory]
    [InlineData("list_portal_processes", "{\"processes\":[{\"id\":1,\"isAttached\":true},{\"id\":2,\"isAttached\":true}]}")]
    [InlineData("read_umac", "{\"scope\":\"project\",\"root\":\"device\",\"sections\":[]}")]
    [InlineData("read_umac", "{\"scope\":\"project\",\"root\":\"project\",\"sections\":[{\"name\":\"s\",\"items\":[{\"kind\":\"K\",\"objectPath\":[],\"values\":{},\"references\":{}}]}]}")]
    public void MalformedPayloads_AreProtocolErrors(string operation, string payload)
        => Assert.Equal(
            WorkerFailureCategories.ProtocolError,
            GovernanceReadCatalog.Project(new GovernanceReadOperationRequest { OperationId = "a", Operation = operation }, WorkerCallResult.Ok(payload)).Failure!.Category);
}

[Collection("Mcp protocol serial")]
public class GovernanceReadEndToEndTests
{
    [Fact]
    public async Task Operations_RoundTrip_AndCapabilityGapsStayItemFailures()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<GovernanceReadTools>();
        var result = await harness.Client.CallToolAsync(GovernanceReadTools.ToolName, new Dictionary<string, object?>
        {
            ["operations"] = new object[]
            {
                new { operationId = "procs", operation = "list_portal_processes", projectPath = "governance-read" },
                new { operationId = "umac", operation = "read_umac", projectPath = "governance-read" },
                new { operationId = "safety", operation = "read_safety", projectPath = "governance-read" },
                new { operationId = "mu", operation = "list_multiuser", projectPath = "governance-read" },
            },
        });

        Assert.False(result.IsError);
        var root = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.True(items[0].GetProperty("result").GetProperty("processes")[0].GetProperty("isAttached").GetBoolean());
        Assert.Equal("HMI Operator", items[1].GetProperty("result").GetProperty("sections")[0].GetProperty("items")[0].GetProperty("references").GetProperty("Roles")[0].GetString());
        Assert.Equal(WorkerFailureCategories.CapabilityUnavailable, items[2].GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal("portal", items[3].GetProperty("result").GetProperty("root").GetString());
    }
}
