using System.Linq;
using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// One step of a generic Openness object path, walked from <see cref="ObjectRoots"/>.
///
/// <para>
/// A <c>composition</c> step selects one element of <c>GetComposition(Name)</c> by
/// <see cref="Index"/>, by <see cref="ElementName"/>, or by both — in which case the name is
/// evidence the indexed element must still carry. An <c>attribute</c> step follows the
/// object-valued result of <c>GetAttribute(Name)</c>. A <c>service</c> step follows
/// <c>GetService&lt;T&gt;()</c> for the declared service type named <see cref="Name"/>.
/// </para>
/// </summary>
public sealed class ObjectPathSegmentInfo
{
    /// <summary>One of <see cref="ObjectPathSegmentKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Composition, attribute, or service name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Composition steps only: the element's <c>Name</c> (selector or evidence).</summary>
    public string? ElementName { get; set; }

    /// <summary>Composition steps only: the element's zero-based position.</summary>
    public int? Index { get; set; }
}

/// <summary>Closed vocabulary of <see cref="ObjectPathSegmentInfo.Kind"/> values.</summary>
public static class ObjectPathSegmentKinds
{
    public const string Composition = "composition";
    public const string Attribute = "attribute";
    public const string Service = "service";

    public static readonly IReadOnlyList<string> All = new[] { Composition, Attribute, Service };

    public static bool IsKnown(string? kind)
        => kind is not null && All.Contains(kind);
}

/// <summary>Closed vocabulary of object-path roots.</summary>
public static class ObjectRoots
{
    /// <summary>The bound project (default).</summary>
    public const string Project = "project";

    /// <summary>The attached TIA Portal instance, with its <c>Projects</c> composition hidden.</summary>
    public const string Portal = "portal";

    public static readonly IReadOnlyList<string> All = new[] { Project, Portal };

    public static bool IsKnown(string? root)
        => root is not null && All.Contains(root);
}

/// <summary>Shared limits for generic object reads, enforced on both sides of the process boundary.</summary>
public static class ObjectReadLimits
{
    public const int MaxPathSegments = 32;
    public const int MaxNameLength = 512;
    public const int MaxAttributeNames = 200;
    public const int MaxCompositionNames = 50;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int MaxChildrenSnapshot = 10_000;
    public const int DefaultExportMaxChars = 16_000;
    public const int MaxExportMaxChars = 30_000;
}

/// <summary>Closed vocabulary of <c>export_object</c> options, mapped to Siemens <c>ExportOptions</c> flags.</summary>
public static class ObjectExportOptionNames
{
    public const string WithDefaults = "withDefaults";
    public const string WithReadOnly = "withReadOnly";

    public static readonly IReadOnlyList<string> All = new[] { WithDefaults, WithReadOnly };

    public static bool IsKnown(string? option)
        => option is not null && All.Contains(option);
}
