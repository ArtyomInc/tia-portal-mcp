using TiaMcpServer.OpennessWorker.ObjectModel;

namespace TiaMcpServer.Tests.ObjectRead;

/// <summary>In-memory <see cref="IObjectNode"/> used to exercise the Siemens-free object walk.</summary>
internal sealed class FakeObjectNode : IObjectNode
{
    private readonly Dictionary<string, List<FakeObjectNode>> _compositions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _attributes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FakeObjectNode?> _services = new(StringComparer.Ordinal);

    public FakeObjectNode(string typeName, string? name = null)
    {
        TypeName = typeName;
        Name = name;
    }

    public string TypeName { get; }

    public string? Name { get; set; }

    public bool IsExportable { get; set; }

    public HashSet<string> FailingCompositions { get; } = new(StringComparer.Ordinal);

    public bool FailAttributes { get; set; }

    public int EnumeratedElements { get; private set; }

    public FakeObjectNode Add(string composition, params FakeObjectNode[] elements)
    {
        if (!_compositions.TryGetValue(composition, out var list))
        {
            list = new List<FakeObjectNode>();
            _compositions[composition] = list;
        }

        list.AddRange(elements);
        return this;
    }

    public FakeObjectNode WithAttribute(string name, object? value)
    {
        _attributes[name] = value;
        return this;
    }

    public FakeObjectNode WithService(string fullTypeName, FakeObjectNode? service)
    {
        _services[fullTypeName] = service;
        return this;
    }

    public string? TryReadName() => Name;

    public IReadOnlyList<ObjectMemberDescriptor> GetCompositions()
        => _compositions.Keys.Select(name => new ObjectMemberDescriptor(name, name + "Composition")).ToList();

    public IReadOnlyList<IObjectNode> GetCompositionElements(string compositionName, int limit)
    {
        if (FailingCompositions.Contains(compositionName))
        {
            throw new InvalidOperationException("enumeration failed");
        }

        var elements = _compositions[compositionName].Take(limit + 1).ToList();
        EnumeratedElements += elements.Count;
        return elements;
    }

    public IReadOnlyList<ObjectAttributeDescriptor> GetAttributes()
    {
        if (FailAttributes)
        {
            throw new InvalidOperationException("attribute metadata failed");
        }

        return _attributes.Select(pair => new ObjectAttributeDescriptor(
            pair.Key,
            "readOnly",
            new[] { pair.Value?.GetType().FullName ?? "System.Object" },
            pair.Value is IObjectNode)).ToList();
    }

    public ObjectFollowResult FollowAttribute(string attributeName) => _attributes[attributeName] switch
    {
        null => ObjectFollowResult.Null,
        IObjectNode node => ObjectFollowResult.Object(node),
        var value => ObjectFollowResult.NotAnObject(value.GetType().FullName!),
    };

    public HashSet<string> FailingValues { get; } = new(StringComparer.Ordinal);

    public ObjectValueRead ReadValue(string name)
    {
        if (FailingValues.Contains(name))
        {
            return ObjectValueRead.Failed("read failed");
        }

        if (string.Equals(name, "Name", StringComparison.Ordinal) && Name is not null && !_attributes.ContainsKey(name))
        {
            return ObjectValueRead.Of(Name);
        }

        return _attributes.TryGetValue(name, out var value) ? ObjectValueRead.Of(value) : ObjectValueRead.Undeclared;
    }

    public IReadOnlyList<ObjectMemberDescriptor> GetServices()
        => _services.Keys.Select(name => new ObjectMemberDescriptor(name, name)).ToList();

    public IObjectNode? GetService(string serviceTypeFullName) => _services[serviceTypeFullName];

    /// <summary>A small project: two devices, a PLC software reachable through a service and attribute.</summary>
    public static FakeObjectNode Project(out FakeObjectNode plcSoftware)
    {
        plcSoftware = new FakeObjectNode("Siemens.Engineering.SW.PlcSoftware", "PLC_1")
            .Add("BlockGroup", new FakeObjectNode("Siemens.Engineering.SW.Blocks.PlcBlockSystemGroup", "Program blocks"));
        var container = new FakeObjectNode("Siemens.Engineering.HW.Features.SoftwareContainer")
            .WithAttribute("Software", plcSoftware);
        var cpu = new FakeObjectNode("Siemens.Engineering.HW.DeviceItem", "PLC_1")
            .WithService("Siemens.Engineering.HW.Features.SoftwareContainer", container)
            .WithService("Siemens.Engineering.Online.OnlineProvider", new FakeObjectNode("Siemens.Engineering.Online.OnlineProvider"))
            .WithAttribute("Comment", null)
            .WithAttribute("OrderNumber", "6ES7 516-3AN02-0AB0");
        var plc = new FakeObjectNode("Siemens.Engineering.HW.Device", "PLC_1").Add("DeviceItems", cpu);
        var hmi = new FakeObjectNode("Siemens.Engineering.HW.Device", "HMI_1");
        return new FakeObjectNode("Siemens.Engineering.Project", "Fixture")
            .Add("Devices", plc, hmi)
            .Add("DeviceGroups");
    }
}
