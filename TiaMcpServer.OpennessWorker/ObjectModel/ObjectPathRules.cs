using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.ObjectModel;

/// <summary>
/// Scope rules of the generic reader that are independent of any particular object graph.
/// </summary>
public static class ObjectPathRules
{
    /// <summary>Composition of the Portal root that would reach other open projects.</summary>
    public const string PortalProjectsComposition = "Projects";

    private static readonly string[] DeniedServiceMarkers = { "Online", "Download", "Upload" };

    /// <summary>
    /// False for services that talk to a device or move data to or from one (R6 territory).
    /// Merely obtaining such a provider is harmless, but reading its state can contact hardware.
    /// </summary>
    public static bool IsServiceAllowed(string serviceTypeFullName)
        => !DeniedServiceMarkers.Any(marker =>
            serviceTypeFullName.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);

    /// <summary>Type of the Portal object whose <c>Projects</c> composition is never readable.</summary>
    public const string PortalTypeName = "Siemens.Engineering.TiaPortal";

    /// <summary>
    /// True when a composition must stay hidden. The Portal's <c>Projects</c> composition would
    /// reach other open projects and sidestep project binding, so it is hidden on every Portal
    /// object — at the portal root and wherever else a walk might arrive at one.
    /// </summary>
    public static bool IsCompositionHidden(IObjectNode owner, string compositionName)
        => string.Equals(compositionName, PortalProjectsComposition, StringComparison.Ordinal)
            && string.Equals(owner.TypeName, PortalTypeName, StringComparison.Ordinal);

    /// <summary>
    /// CLR properties never offered as attribute steps: <c>Parent</c> walks upward and could
    /// climb out of the bound project to the Portal.
    /// </summary>
    public static bool IsNavigationPropertyExcluded(string propertyName)
        => string.Equals(propertyName, "Parent", StringComparison.Ordinal);

    /// <summary>True for project object types: the Openness project and its Multiuser counterpart.</summary>
    public static bool IsProjectType(string typeName)
        => string.Equals(typeName, "Siemens.Engineering.Project", StringComparison.Ordinal)
            || string.Equals(typeName, "Siemens.Engineering.Multiuser.MultiuserProject", StringComparison.Ordinal);

    /// <summary>Simple type name of a full type name (text after the last '.' or '+').</summary>
    public static string SimpleName(string fullTypeName)
    {
        var index = Math.Max(fullTypeName.LastIndexOf('.'), fullTypeName.LastIndexOf('+'));
        return index < 0 ? fullTypeName : fullTypeName.Substring(index + 1);
    }

    /// <summary>
    /// Structural validation of a path, identical to the host catalog's so the worker never
    /// trusts that validation happened upstream. Throws <see cref="WorkerOperationException"/>.
    /// </summary>
    public static void Validate(string? root, IReadOnlyList<ObjectPathSegmentInfo>? path)
    {
        if (root is not null && !ObjectRoots.IsKnown(root))
        {
            throw Invalid($"ObjectRoot must be one of: {string.Join(", ", ObjectRoots.All)}.");
        }

        if (path is null)
        {
            return;
        }

        if (path.Count > ObjectReadLimits.MaxPathSegments)
        {
            throw Invalid($"ObjectPath must contain at most {ObjectReadLimits.MaxPathSegments} segments.");
        }

        for (var i = 0; i < path.Count; i++)
        {
            var segment = path[i] ?? throw Invalid($"ObjectPath segment {i} is null.");
            if (!ObjectPathSegmentKinds.IsKnown(segment.Kind))
            {
                throw Invalid($"ObjectPath segment {i}: kind must be one of: {string.Join(", ", ObjectPathSegmentKinds.All)}.");
            }

            if (string.IsNullOrWhiteSpace(segment.Name) || segment.Name.Length > ObjectReadLimits.MaxNameLength)
            {
                throw Invalid($"ObjectPath segment {i}: name must be nonblank and at most {ObjectReadLimits.MaxNameLength} characters.");
            }

            if (string.Equals(segment.Kind, ObjectPathSegmentKinds.Composition, StringComparison.Ordinal))
            {
                if (segment.Index is null && segment.ElementName is null)
                {
                    throw Invalid($"ObjectPath segment {i}: a composition step requires elementName, index, or both.");
                }

                if (segment.Index is < 0)
                {
                    throw Invalid($"ObjectPath segment {i}: index must be 0 or greater.");
                }

                if (segment.ElementName is not null
                    && (segment.ElementName.Length == 0 || segment.ElementName.Length > ObjectReadLimits.MaxNameLength))
                {
                    throw Invalid($"ObjectPath segment {i}: elementName must be nonempty and at most {ObjectReadLimits.MaxNameLength} characters.");
                }
            }
            else if (segment.Index is not null || segment.ElementName is not null)
            {
                throw Invalid($"ObjectPath segment {i}: elementName and index apply only to composition steps.");
            }
        }
    }

    private static WorkerOperationException Invalid(string message)
        => new WorkerOperationException(WorkerFailureCategories.ValidationError, message);
}
