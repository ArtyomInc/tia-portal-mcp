using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.ObjectModel;

/// <summary>
/// Walks an object path from a root, one declared member at a time, and fails closed.
///
/// <para>
/// Every step must name a member the current object declares. A composition step selects exactly
/// one element — by index, by exact ordinal name, or by index verified against the name — and never
/// falls back to a "closest" match. The resolver therefore either reaches the object the caller
/// addressed or reports why it could not; it never returns a different object.
/// </para>
/// </summary>
public static class ObjectPathResolver
{
    public static IObjectNode Resolve(IObjectNode root, string? rootKind, IReadOnlyList<ObjectPathSegmentInfo>? path)
    {
        ObjectPathRules.Validate(rootKind, path);
        var current = root;
        if (path is null)
        {
            return current;
        }

        for (var depth = 0; depth < path.Count; depth++)
        {
            var segment = path[depth];
            current = segment.Kind switch
            {
                ObjectPathSegmentKinds.Composition => StepComposition(current, depth, segment),
                ObjectPathSegmentKinds.Attribute => StepAttribute(current, depth, segment),
                _ => StepService(current, depth, segment),
            };

            // The walk never enters another project object: only the root may be one. This keeps
            // every route (a session, a library, a Portal node) inside the bound project.
            if (ObjectPathRules.IsProjectType(current.TypeName))
            {
                throw Fail(
                    WorkerFailureCategories.AccessDenied,
                    depth,
                    "the step reaches a project object; only the bound project root is readable.");
            }
        }

        return current;
    }

    private static IObjectNode StepComposition(IObjectNode current, int depth, ObjectPathSegmentInfo segment)
    {
        if (ObjectPathRules.IsCompositionHidden(current, segment.Name))
        {
            throw Fail(
                WorkerFailureCategories.AccessDenied,
                depth,
                $"the Portal's '{segment.Name}' composition is not readable; use root 'project' for the bound project.");
        }

        if (!current.GetCompositions().Any(c => string.Equals(c.Name, segment.Name, StringComparison.Ordinal)))
        {
            throw Fail(WorkerFailureCategories.TargetNotFound, depth, $"{Describe(current)} declares no composition '{segment.Name}'.");
        }

        var elements = current.GetCompositionElements(segment.Name, ObjectReadLimits.MaxChildrenSnapshot);
        if (segment.Index is int index)
        {
            if (index >= elements.Count)
            {
                throw Fail(
                    WorkerFailureCategories.TargetNotFound,
                    depth,
                    $"composition '{segment.Name}' has no element at index {index} ({elements.Count} element(s)).");
            }

            var element = elements[index];
            if (segment.ElementName is not null
                && !string.Equals(element.TryReadName(), segment.ElementName, StringComparison.Ordinal))
            {
                throw Fail(
                    WorkerFailureCategories.TargetEvidenceMismatch,
                    depth,
                    $"the element at index {index} of '{segment.Name}' is no longer named '{segment.ElementName}'.");
            }

            return element;
        }

        if (elements.Count > ObjectReadLimits.MaxChildrenSnapshot)
        {
            throw Fail(
                WorkerFailureCategories.SnapshotTooLarge,
                depth,
                $"composition '{segment.Name}' has more than {ObjectReadLimits.MaxChildrenSnapshot} elements; address the element by index.");
        }

        var matches = elements
            .Where(element => string.Equals(element.TryReadName(), segment.ElementName, StringComparison.Ordinal))
            .Take(2)
            .ToList();
        return matches.Count switch
        {
            0 => throw Fail(
                WorkerFailureCategories.TargetNotFound,
                depth,
                $"composition '{segment.Name}' has no element named '{segment.ElementName}'."),
            1 => matches[0],
            _ => throw Fail(
                WorkerFailureCategories.TargetAmbiguous,
                depth,
                $"composition '{segment.Name}' has more than one element named '{segment.ElementName}'; add its index."),
        };
    }

    private static IObjectNode StepAttribute(IObjectNode current, int depth, ObjectPathSegmentInfo segment)
    {
        if (ObjectPathRules.IsNavigationPropertyExcluded(segment.Name))
        {
            throw Fail(WorkerFailureCategories.AccessDenied, depth, $"attribute '{segment.Name}' walks upward and is not a readable step.");
        }

        if (!current.GetAttributes().Any(a => string.Equals(a.Name, segment.Name, StringComparison.Ordinal)))
        {
            throw Fail(WorkerFailureCategories.TargetNotFound, depth, $"{Describe(current)} declares no attribute '{segment.Name}'.");
        }

        var result = current.FollowAttribute(segment.Name);
        if (result.Node is not null)
        {
            return result.Node;
        }

        if (result.IsNull)
        {
            throw Fail(WorkerFailureCategories.TargetNotFound, depth, $"attribute '{segment.Name}' is null.");
        }

        throw Fail(
            WorkerFailureCategories.TargetKindUnsupported,
            depth,
            $"attribute '{segment.Name}' holds a {result.ValueTypeName} value, not an engineering object; read it with read_object_attributes.");
    }

    private static IObjectNode StepService(IObjectNode current, int depth, ObjectPathSegmentInfo segment)
    {
        var matches = current.GetServices()
            .Where(service => string.Equals(service.Name, segment.Name, StringComparison.Ordinal)
                || string.Equals(ObjectPathRules.SimpleName(service.Name), segment.Name, StringComparison.Ordinal))
            .ToList();
        if (matches.Count == 0)
        {
            throw Fail(WorkerFailureCategories.TargetNotFound, depth, $"{Describe(current)} offers no service '{segment.Name}'.");
        }

        if (matches.Count > 1)
        {
            throw Fail(
                WorkerFailureCategories.TargetAmbiguous,
                depth,
                $"service name '{segment.Name}' matches more than one service; use the full type name.");
        }

        var serviceType = matches[0].Name;
        if (!ObjectPathRules.IsServiceAllowed(serviceType))
        {
            throw Fail(
                WorkerFailureCategories.AccessDenied,
                depth,
                $"service '{serviceType}' can contact a device and is not available to the generic reader.");
        }

        return current.GetService(serviceType)
            ?? throw Fail(WorkerFailureCategories.TargetNotFound, depth, $"service '{serviceType}' returned no instance.");
    }

    private static string Describe(IObjectNode node) => $"'{ObjectPathRules.SimpleName(node.TypeName)}'";

    private static WorkerOperationException Fail(string category, int depth, string message)
        => new WorkerOperationException(category, $"ObjectPath segment {depth}: {message}");
}
