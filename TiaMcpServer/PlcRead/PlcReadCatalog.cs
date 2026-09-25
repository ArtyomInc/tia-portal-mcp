using TiaMcpServer.Contracts;
using TiaMcpServer.DomainReads;

namespace TiaMcpServer.PlcRead;

/// <summary>Whitelists and validates the <c>plc_read</c> operations. Pure and Siemens-free.</summary>
public static class PlcReadCatalog
{
    public const int MaxNameLength = 512;
    public const int MaxParameterNames = 200;
    public const int MaxGroupPathDepth = 32;

    /// <summary>Operations that list a PLC group hierarchy.</summary>
    public static readonly IReadOnlyList<string> ListingOperations = new[]
    {
        "list_blocks", "list_types", "list_watch_tables", "list_technology_objects", "list_external_sources",
        "list_software_units", "list_alarm_text_lists", "list_opcua_server_interfaces",
    };

    private static readonly string[] Paging = { "pageSize", "cursor" };
    private static readonly string[] Selection = { "plcName" };

    public static DomainReadCatalog<PlcReadOperationRequest> Instance { get; } = new(
        "plc_read",
        new[] { new DomainReadOperationSpec<PlcReadOperationRequest>("list_plcs", Array.Empty<string>(), Array.Empty<string>()) }
            .Concat(ListingOperations.Select(name => new DomainReadOperationSpec<PlcReadOperationRequest>(
                name,
                Array.Empty<string>(),
                Selection.Concat(new[] { "nameContains" }).Concat(Paging).ToArray(),
                ValidateListing)))
            .Concat(new[]
            {
                new DomainReadOperationSpec<PlcReadOperationRequest>(
                    "read_watch_table", new[] { "name" }, Selection.Concat(new[] { "groupPath" }).Concat(Paging).ToArray(), ValidateTarget),
                new DomainReadOperationSpec<PlcReadOperationRequest>(
                    "read_technology_object",
                    new[] { "name" },
                    Selection.Concat(new[] { "groupPath", "parameterNames" }).Concat(Paging).ToArray(),
                    ValidateTarget),
                new DomainReadOperationSpec<PlcReadOperationRequest>(
                    "read_block_fingerprints", new[] { "name" }, Selection.Concat(new[] { "groupPath" }).ToArray(), ValidateTarget),
                new DomainReadOperationSpec<PlcReadOperationRequest>("read_checksums", Array.Empty<string>(), Selection, ValidateSelection),
                new DomainReadOperationSpec<PlcReadOperationRequest>(
                    "compare_software",
                    new[] { "comparePlcName" },
                    Selection.Concat(new[] { "includeIdentical" }).Concat(Paging).ToArray(),
                    ValidateCompare),
            }),
        new[]
        {
            new DomainReadField<PlcReadOperationRequest>("plcName", o => o.PlcName is not null),
            new DomainReadField<PlcReadOperationRequest>("name", o => o.Name is not null, o => !string.IsNullOrWhiteSpace(o.Name)),
            new DomainReadField<PlcReadOperationRequest>("groupPath", o => o.GroupPath is not null),
            new DomainReadField<PlcReadOperationRequest>("nameContains", o => o.NameContains is not null),
            new DomainReadField<PlcReadOperationRequest>("parameterNames", o => o.ParameterNames is not null),
            new DomainReadField<PlcReadOperationRequest>(
                "comparePlcName", o => o.ComparePlcName is not null, o => !string.IsNullOrWhiteSpace(o.ComparePlcName)),
            new DomainReadField<PlcReadOperationRequest>("includeIdentical", o => o.IncludeIdentical is not null),
            new DomainReadField<PlcReadOperationRequest>("pageSize", o => o.PageSize is not null),
            new DomainReadField<PlcReadOperationRequest>("cursor", o => o.Cursor is not null),
        });

    public static IReadOnlyList<string> OperationNames => Instance.OperationNames;

    private static void ValidateSelection(PlcReadOperationRequest operation, List<string> errors)
        => ValidateText(operation.PlcName, "plcName", errors);

    private static void ValidateListing(PlcReadOperationRequest operation, List<string> errors)
    {
        ValidateSelection(operation, errors);
        ValidateText(operation.NameContains, "nameContains", errors);
        ValidatePaging(operation, errors);
    }

    private static void ValidateTarget(PlcReadOperationRequest operation, List<string> errors)
    {
        ValidateSelection(operation, errors);
        if (operation.Name is not null && operation.Name.Length > MaxNameLength)
        {
            errors.Add($"'name' must be at most {MaxNameLength} characters.");
        }

        if (operation.GroupPath is not null
            && (operation.GroupPath.Count > MaxGroupPathDepth
                || operation.GroupPath.Any(segment => segment is null || segment.Length > MaxNameLength)))
        {
            errors.Add($"'groupPath' must contain at most {MaxGroupPathDepth} non-null group names.");
        }

        if (operation.ParameterNames is not null
            && (operation.ParameterNames.Count == 0 || operation.ParameterNames.Count > MaxParameterNames
                || operation.ParameterNames.Any(string.IsNullOrWhiteSpace)
                || operation.ParameterNames.Distinct(StringComparer.Ordinal).Count() != operation.ParameterNames.Count))
        {
            errors.Add($"'parameterNames' must contain between 1 and {MaxParameterNames} unique, nonblank names when supplied.");
        }

        ValidatePaging(operation, errors);
    }

    private static void ValidateCompare(PlcReadOperationRequest operation, List<string> errors)
    {
        ValidateSelection(operation, errors);
        if (operation.ComparePlcName is not null && operation.ComparePlcName.Length > MaxNameLength)
        {
            errors.Add($"'comparePlcName' must be at most {MaxNameLength} characters.");
        }

        ValidatePaging(operation, errors);
    }

    private static void ValidatePaging(PlcReadOperationRequest operation, List<string> errors)
    {
        if (operation.PageSize is < 1 or > ObjectReadLimits.MaxPageSize)
        {
            errors.Add($"'pageSize' must be between 1 and {ObjectReadLimits.MaxPageSize}.");
        }

        if (operation.Cursor is not null && string.IsNullOrWhiteSpace(operation.Cursor))
        {
            errors.Add("'cursor' must not be blank.");
        }
    }

    private static void ValidateText(string? value, string field, List<string> errors)
    {
        if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > MaxNameLength))
        {
            errors.Add($"'{field}' must be nonblank and at most {MaxNameLength} characters when supplied.");
        }
    }
}
