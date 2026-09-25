using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.ObjectModel;
using TiaMcpServer.OpennessWorker.PlcRead;

namespace TiaMcpServer.OpennessWorker.GovernanceRead;

/// <summary>One section of a governance read: a composition and the associations to name.</summary>
public sealed class GovernanceSectionSpec
{
    public GovernanceSectionSpec(string name, IReadOnlyList<string> path, string composition, IReadOnlyList<string> references)
    {
        Name = name;
        Path = path;
        Composition = composition;
        References = references;
    }

    public string Name { get; }

    /// <summary>Attribute steps from the scope object to the composition owner.</summary>
    public IReadOnlyList<string> Path { get; }

    public string Composition { get; }
    public IReadOnlyList<string> References { get; }
}

/// <summary>
/// Siemens-free builders of the <c>governance_read</c> results. Each read resolves one scope object
/// (a project service, the PLC's Safety administration, or the Portal) and lists named sections of
/// it with every scalar attribute and the names of referenced objects. Passwords are never read:
/// only the observable state flags that Openness exposes.
/// </summary>
public static class GovernanceReadBuilder
{
    private static readonly string[] None = Array.Empty<string>();

    public static readonly IReadOnlyList<GovernanceSectionSpec> UmacSections = new[]
    {
        new GovernanceSectionSpec("projectUsers", None, "ProjectUsers", new[] { "Roles" }),
        new GovernanceSectionSpec("customRoles", None, "CustomRoles", new[] { "AssignedEngineeringRights" }),
        new GovernanceSectionSpec("systemRoles", None, "SystemRoles", new[] { "AssignedEngineeringRights" }),
        new GovernanceSectionSpec("umcUsers", None, "UmcUsers", new[] { "Roles" }),
        new GovernanceSectionSpec("umcUserGroups", None, "UmcUserGroups", new[] { "Roles" }),
        new GovernanceSectionSpec("engineeringFunctionRights", None, "EngineeringFunctionRights", None),
        new GovernanceSectionSpec("customDeviceFunctionRights", None, "CustomDeviceFunctionRights", None),
    };

    public static readonly IReadOnlyList<GovernanceSectionSpec> SafetySections = new[]
    {
        new GovernanceSectionSpec("programSignatures", new[] { "ProgramSignatures" }, "Signatures", None),
        new GovernanceSectionSpec("runtimeGroups", None, "RuntimeGroups", None),
    };

    public static readonly IReadOnlyList<GovernanceSectionSpec> TestSuiteSections = new[]
    {
        new GovernanceSectionSpec("testCases", new[] { "ApplicationTestGroup" }, "TestCases", None),
        new GovernanceSectionSpec("applicationTestSets", new[] { "ApplicationTestGroup" }, "ApplicationTestSets", None),
        new GovernanceSectionSpec("styleGuideRuleSets", new[] { "StyleGuideGroup" }, "RuleSets", None),
        new GovernanceSectionSpec("systemTestCases", new[] { "SystemTestGroup" }, "SystemTestCases", None),
    };

    public static readonly IReadOnlyList<GovernanceSectionSpec> VciSections = new[]
    {
        new GovernanceSectionSpec("workspaces", new[] { "WorkspaceGroup" }, "Workspaces", None),
        new GovernanceSectionSpec("workspaceGroups", new[] { "WorkspaceGroup" }, "Groups", None),
    };

    public static readonly IReadOnlyList<GovernanceSectionSpec> MultiuserSections = new[]
    {
        new GovernanceSectionSpec("projectServers", None, "ProjectServers", None),
        new GovernanceSectionSpec("localSessions", None, "LocalSessions", None),
    };

    public static GovernanceInfo ReadProjectService(IObjectNode project, string serviceName, IReadOnlyList<GovernanceSectionSpec> sections)
    {
        var path = new List<ObjectPathSegmentInfo> { new() { Kind = ObjectPathSegmentKinds.Service, Name = serviceName } };
        IObjectNode service;
        try
        {
            service = ObjectPathResolver.Resolve(project, ObjectRoots.Project, path);
        }
        catch (WorkerOperationException ex) when (ex.FailureCategory == WorkerFailureCategories.TargetNotFound)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.CapabilityUnavailable,
                $"The project offers no {serviceName}: the product is not installed, not licensed, or not used by this project.");
        }

        return Read("project", ObjectRoots.Project, service, path, sections, scopeValues: false);
    }

    public static GovernanceInfo ReadSafety(IObjectNode project, string? plcName)
    {
        var diagnostics = new List<string>();
        var plc = PlcLocator.Select(project, plcName, diagnostics);

        // The PLC path ends with the SoftwareContainer service and its Software attribute; the
        // Safety administration is a service of the same CPU device item.
        var itemPath = plc.Path.Take(plc.Path.Count - 2).Select(ObjectChildrenPager.Copy).ToList();
        var path = itemPath.Append(new ObjectPathSegmentInfo { Kind = ObjectPathSegmentKinds.Service, Name = "SafetyAdministration" }).ToList();
        IObjectNode administration;
        try
        {
            administration = ObjectPathResolver.Resolve(project, ObjectRoots.Project, path);
        }
        catch (Exception ex) when (ex is not WorkerOperationException
            || ((WorkerOperationException)ex).FailureCategory == WorkerFailureCategories.TargetNotFound)
        {
            // A standard CPU either declares no Safety administration or refuses to create one.
            throw new WorkerOperationException(
                WorkerFailureCategories.CapabilityUnavailable,
                $"PLC '{plc.Summary.Name}' has no Safety administration (not a fail-safe CPU, or STEP 7 Safety is not available).");
        }

        var result = Read(plc.Summary.Name, ObjectRoots.Project, administration, path, SafetySections, scopeValues: true);
        var settings = administration.FollowAttribute("Settings").Node;
        if (settings is not null)
        {
            var unavailable = new List<string>();
            foreach (var pair in ObjectScalarNormalizer.ReadValues(settings, null, unavailable))
            {
                result.Values["Settings." + pair.Key] = pair.Value;
            }
        }

        result.Diagnostics.InsertRange(0, diagnostics);
        return result;
    }

    public static GovernanceInfo ReadPortal(IObjectNode portal, IReadOnlyList<GovernanceSectionSpec> sections)
        => Read("portal", ObjectRoots.Portal, portal, new List<ObjectPathSegmentInfo>(), sections, scopeValues: false);

    private static GovernanceInfo Read(
        string scope,
        string root,
        IObjectNode scopeNode,
        List<ObjectPathSegmentInfo> scopePath,
        IReadOnlyList<GovernanceSectionSpec> sections,
        bool scopeValues)
    {
        var diagnostics = new List<string>();
        var result = new GovernanceInfo { Scope = scope, Root = root, Diagnostics = diagnostics };
        if (scopeValues)
        {
            var unavailable = new List<string>();
            result.Values = ObjectScalarNormalizer.ReadValues(scopeNode, null, unavailable);
        }

        foreach (var section in sections)
        {
            var owner = scopeNode;
            var ownerPath = scopePath.Select(ObjectChildrenPager.Copy).ToList();
            foreach (var step in section.Path)
            {
                owner = owner?.FollowAttribute(step).Node;
                ownerPath.Add(new ObjectPathSegmentInfo { Kind = ObjectPathSegmentKinds.Attribute, Name = step });
            }

            var info = new GovernanceSectionInfo { Name = section.Name };
            result.Sections.Add(info);
            if (owner is null)
            {
                diagnostics.Add($"Section '{section.Name}' is not available.");
                continue;
            }

            // A hidden composition (the Portal's Projects) is never listed; a project object in a
            // listing is reported by name only and never walked into.
            if (ObjectPathRules.IsCompositionHidden(owner, section.Composition))
            {
                continue;
            }

            var elements = DeviceWalker.Elements(owner, section.Composition, diagnostics);
            for (var index = 0; index < elements.Count; index++)
            {
                var element = elements[index];
                var unavailable = new List<string>();
                var item = new GovernanceItemInfo
                {
                    Name = element.TryReadName(),
                    Kind = ObjectPathRules.SimpleName(element.TypeName),
                    ObjectPath = DeviceWalker.Append(ownerPath, section.Composition, index, element.TryReadName()),
                    Values = ObjectScalarNormalizer.ReadValues(element, null, unavailable),
                    Unavailable = unavailable,
                };
                foreach (var reference in section.References)
                {
                    var referenced = element.ReadObjectList(reference);
                    if (referenced is not null)
                    {
                        item.References[reference] = referenced.Select(node => node.TryReadName() ?? string.Empty).ToList();
                    }
                }

                info.Items.Add(item);
            }
        }

        return result;
    }
}
