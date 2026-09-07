using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProjectTreeSelectorSegment
{
    public string NodeType { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
