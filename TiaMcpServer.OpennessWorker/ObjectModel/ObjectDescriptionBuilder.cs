using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.ObjectModel;

/// <summary>
/// Builds <c>describe_object</c>. Each member family is read independently: a failure in one
/// becomes a diagnostic and leaves the others intact, so one unreadable member never hides the
/// navigation menu of an object.
/// </summary>
public static class ObjectDescriptionBuilder
{
    public static ObjectDescriptionInfo Build(
        IObjectNode node,
        string? root,
        IReadOnlyList<ObjectPathSegmentInfo>? path)
    {
        var effectiveRoot = root ?? ObjectRoots.Project;
        var diagnostics = new List<string>();
        var result = new ObjectDescriptionInfo
        {
            Root = effectiveRoot,
            ObjectPath = (path ?? Array.Empty<ObjectPathSegmentInfo>()).Select(ObjectChildrenPager.Copy).ToList(),
            TypeName = node.TypeName,
            Diagnostics = diagnostics,
        };

        result.Name = Guard(node.TryReadName, "name", diagnostics);
        result.Exportable = Guard(() => node.IsExportable, "export capability", diagnostics);

        result.Compositions = (Guard(node.GetCompositions, "compositions", diagnostics) ?? Array.Empty<ObjectMemberDescriptor>())
            .Where(c => !ObjectPathRules.IsCompositionHidden(node, c.Name))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => new ObjectCompositionDescriptorInfo { Name = c.Name, TypeName = c.TypeName })
            .ToList();

        result.Attributes = (Guard(node.GetAttributes, "attributes", diagnostics) ?? Array.Empty<ObjectAttributeDescriptor>())
            .OrderBy(a => a.Name, StringComparer.Ordinal)
            .Select(a => new ObjectAttributeDescriptorInfo
            {
                Name = a.Name,
                Access = a.Access,
                SupportedTypes = a.SupportedTypes.ToList(),
                Navigable = a.Navigable,
            })
            .ToList();

        result.Services = (Guard(node.GetServices, "services", diagnostics) ?? Array.Empty<ObjectMemberDescriptor>())
            .OrderBy(s => ObjectPathRules.SimpleName(s.Name), StringComparer.Ordinal)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new ObjectServiceDescriptorInfo
            {
                Name = ObjectPathRules.SimpleName(s.Name),
                TypeName = s.Name,
                Allowed = ObjectPathRules.IsServiceAllowed(s.Name),
            })
            .ToList();

        return result;
    }

    private static T? Guard<T>(Func<T> read, string what, List<string> diagnostics)
    {
        try
        {
            return read();
        }
        catch (WorkerOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            diagnostics.Add($"The object's {what} could not be read: {ex.GetType().Name}: {ex.Message}");
            return default;
        }
    }
}
