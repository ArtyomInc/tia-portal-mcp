using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.OpennessWorker.ObjectModel;

/// <summary>
/// Turns an attribute's declared <c>SupportedTypes</c> into public type names. Openness can
/// declare a type it cannot resolve as a null entry (WinCC Unified color attributes do this for
/// <c>System.Drawing.Color</c>); such entries are dropped instead of failing the whole attribute
/// listing.
/// </summary>
public static class SupportedTypeList
{
    public static IReadOnlyList<Type> Resolved(IEnumerable<Type?>? supportedTypes)
        => supportedTypes is null
            ? Array.Empty<Type>()
            : supportedTypes.Where(type => type is not null).Select(type => type!).ToList();

    public static IReadOnlyList<string> Names(IEnumerable<Type> resolved)
        => resolved.Select(type => type.FullName ?? type.Name).ToList();
}
