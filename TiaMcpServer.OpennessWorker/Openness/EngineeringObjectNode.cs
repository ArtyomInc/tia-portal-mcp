using System.Collections;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using TiaMcpServer.OpennessWorker.ObjectModel;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Adapts <see cref="IEngineeringObject"/> to the Siemens-free <see cref="IObjectNode"/>. Only the
/// read-only generic members are touched: <c>GetAttributeInfos</c>, <c>GetAttribute</c>,
/// <c>GetCompositionInfos</c>, <c>GetComposition</c>, <c>GetServiceInfos</c>, and
/// <c>GetService&lt;T&gt;</c>.
/// </summary>
internal sealed class EngineeringObjectNode : IObjectNode
{
    private static readonly MethodInfo GetServiceDefinition =
        typeof(IEngineeringServiceProvider).GetMethod(nameof(IEngineeringServiceProvider.GetService))
        ?? throw new MissingMethodException(nameof(IEngineeringServiceProvider), nameof(IEngineeringServiceProvider.GetService));

    public EngineeringObjectNode(IEngineeringObject engineeringObject)
    {
        EngineeringObject = engineeringObject;
    }

    public IEngineeringObject EngineeringObject { get; }

    public string TypeName => PublicTypeName(EngineeringObject.GetType());

    public bool IsExportable => FindExportMethod(EngineeringObject.GetType()) is not null;

    public string? TryReadName()
    {
        try
        {
            if (!EngineeringObject.GetAttributeInfos().Any(info => info.Name == "Name"
                && info.AccessMode is EngineeringAttributeAccessMode.Read or EngineeringAttributeAccessMode.ReadWrite))
            {
                return null;
            }

            return EngineeringObject.GetAttribute("Name") as string;
        }
        catch (EngineeringException)
        {
            return null;
        }
    }

    public IReadOnlyList<ObjectMemberDescriptor> GetCompositions()
        => EngineeringObject.GetCompositionInfos()
            .Select(info => new ObjectMemberDescriptor(info.Name, info.Type?.FullName))
            .ToList();

    public IReadOnlyList<IObjectNode> GetCompositionElements(string compositionName, int limit)
    {
        var elements = new List<IObjectNode>();
        if (EngineeringObject.GetComposition(compositionName) is not IEnumerable composition)
        {
            return elements;
        }

        foreach (var element in composition)
        {
            if (element is IEngineeringObject engineeringObject)
            {
                elements.Add(new EngineeringObjectNode(engineeringObject));
                if (elements.Count > limit)
                {
                    break;
                }
            }
        }

        return elements;
    }

    public IReadOnlyList<ObjectAttributeDescriptor> GetAttributes()
    {
        var attributes = EngineeringObject.GetAttributeInfos()
            .Select(info =>
            {
                var supportedTypes = info.SupportedTypes ?? (IEnumerable<Type>)Array.Empty<Type>();
                return new ObjectAttributeDescriptor(
                    info.Name,
                    Access(info.AccessMode),
                    supportedTypes.Select(type => type.FullName ?? type.Name).ToList(),
                    supportedTypes.Any(type => typeof(IEngineeringObject).IsAssignableFrom(type)));
            })
            .ToList();
        var declared = new HashSet<string>(attributes.Select(attribute => attribute.Name), StringComparer.Ordinal);
        attributes.AddRange(NavigationProperties()
            .Where(property => !declared.Contains(property.Name))
            .Select(property => new ObjectAttributeDescriptor(
                property.Name,
                "readOnly",
                new[] { property.PropertyType.FullName ?? property.PropertyType.Name },
                navigable: true)));
        return attributes;
    }

    public ObjectFollowResult FollowAttribute(string attributeName)
    {
        object? value;
        if (EngineeringObject.GetAttributeInfos().Any(info => string.Equals(info.Name, attributeName, StringComparison.Ordinal)))
        {
            value = EngineeringObject.GetAttribute(attributeName);
        }
        else
        {
            var property = NavigationProperties().FirstOrDefault(p => string.Equals(p.Name, attributeName, StringComparison.Ordinal));
            value = property is null ? null : ReadProperty(property);
        }

        return value switch
        {
            null => ObjectFollowResult.Null,
            IEngineeringObject engineeringObject => ObjectFollowResult.Object(new EngineeringObjectNode(engineeringObject)),
            _ => ObjectFollowResult.NotAnObject(value.GetType().FullName ?? value.GetType().Name),
        };
    }

    /// <summary>
    /// Public CLR properties of the object's public API type that return an engineering object but
    /// are not dynamic attributes (for example <c>SoftwareContainer.Software</c>). They are offered
    /// as read-only, navigable attribute steps; <c>Parent</c> is excluded.
    /// </summary>
    public IReadOnlyList<PropertyInfo> NavigationProperties()
        => PublicType(EngineeringObject.GetType())
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead
                && property.GetIndexParameters().Length == 0
                && typeof(IEngineeringObject).IsAssignableFrom(property.PropertyType)
                && !ObjectPathRules.IsNavigationPropertyExcluded(property.Name))
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();

    public object? ReadProperty(PropertyInfo property)
    {
        try
        {
            return property.GetValue(EngineeringObject);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    public ObjectValueRead ReadValue(string name)
    {
        try
        {
            var info = EngineeringObject.GetAttributeInfos()
                .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (info is not null)
            {
                return info.AccessMode is EngineeringAttributeAccessMode.Read or EngineeringAttributeAccessMode.ReadWrite
                    ? ObjectValueRead.Of(EngineeringObject.GetAttribute(name))
                    : ObjectValueRead.Failed("The attribute is not readable.");
            }

            var property = PublicType(EngineeringObject.GetType())
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property is null || !property.CanRead || property.GetIndexParameters().Length != 0
                || ObjectPathRules.IsNavigationPropertyExcluded(property.Name))
            {
                return ObjectValueRead.Undeclared;
            }

            return ObjectValueRead.Of(ReadProperty(property));
        }
        catch (Exception ex) when (ex is EngineeringException or InvalidOperationException or NotSupportedException
            or TargetInvocationException or ArgumentException)
        {
            return ObjectValueRead.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public IReadOnlyList<ObjectMemberDescriptor> GetServices()
    {
        if (EngineeringObject is not IEngineeringServiceProvider provider)
        {
            return Array.Empty<ObjectMemberDescriptor>();
        }

        return provider.GetServiceInfos()
            .Where(info => info.Type is not null)
            .Select(info => new ObjectMemberDescriptor(info.Type.FullName ?? info.Type.Name, info.Type.FullName))
            .ToList();
    }

    public IObjectNode? GetService(string serviceTypeFullName)
    {
        if (EngineeringObject is not IEngineeringServiceProvider provider)
        {
            return null;
        }

        var serviceType = provider.GetServiceInfos()
            .Select(info => info.Type)
            .FirstOrDefault(type => type is not null && string.Equals(type.FullName, serviceTypeFullName, StringComparison.Ordinal));
        if (serviceType is null)
        {
            return null;
        }

        object? service;
        try
        {
            service = GetServiceDefinition.MakeGenericMethod(serviceType).Invoke(provider, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        return service is IEngineeringObject engineeringObject
            ? new EngineeringObjectNode(engineeringObject)
            : null;
    }

    /// <summary>
    /// Openness hands out internal implementation classes (for example <c>DeviceImpl</c> for
    /// <c>Device</c>). The documented name is the nearest public base type.
    /// </summary>
    public static string PublicTypeName(Type type)
    {
        var chosen = PublicType(type);
        return chosen.FullName ?? chosen.Name;
    }

    /// <summary>The nearest public type in <paramref name="type"/>'s base chain, or the type itself.</summary>
    public static Type PublicType(Type type)
    {
        var current = type;
        while (!current.IsPublic && current.BaseType is not null && current.BaseType != typeof(object))
        {
            current = current.BaseType;
        }

        return current.IsPublic ? current : type;
    }

    /// <summary>The public <c>Export(FileInfo, ExportOptions)</c> method of a type, or null.</summary>
    public static MethodInfo? FindExportMethod(Type type)
        => type.GetMethod(
            "Export",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(FileInfo), typeof(ExportOptions) },
            modifiers: null);

    private static string Access(EngineeringAttributeAccessMode accessMode) => accessMode switch
    {
        EngineeringAttributeAccessMode.None => "none",
        EngineeringAttributeAccessMode.Read => "readOnly",
        EngineeringAttributeAccessMode.Write => "writeOnly",
        EngineeringAttributeAccessMode.ReadWrite => "readWrite",
        _ => "unknown",
    };
}
