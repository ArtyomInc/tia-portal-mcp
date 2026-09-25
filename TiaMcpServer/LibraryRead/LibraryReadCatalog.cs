using TiaMcpServer.Contracts;
using TiaMcpServer.DomainReads;

namespace TiaMcpServer.LibraryRead;

/// <summary>Whitelists and validates the <c>library_read</c> operations. Pure and Siemens-free.</summary>
public static class LibraryReadCatalog
{
    public const int MaxNameLength = 512;
    public const int MaxFolderDepth = 32;

    private static readonly string[] Paging = { "pageSize", "cursor" };

    public static DomainReadCatalog<LibraryReadOperationRequest> Instance { get; } = new(
        "library_read",
        new[]
        {
            new DomainReadOperationSpec<LibraryReadOperationRequest>("list_libraries", Array.Empty<string>(), Array.Empty<string>()),
            new DomainReadOperationSpec<LibraryReadOperationRequest>(
                "list_library_types", Array.Empty<string>(), new[] { "libraryName" }.Concat(Paging).ToArray(), Validate),
            new DomainReadOperationSpec<LibraryReadOperationRequest>(
                "read_library_type", new[] { "name" }, new[] { "libraryName", "folderPath" }, Validate),
            new DomainReadOperationSpec<LibraryReadOperationRequest>(
                "list_master_copies", Array.Empty<string>(), new[] { "libraryName" }.Concat(Paging).ToArray(), Validate),
            new DomainReadOperationSpec<LibraryReadOperationRequest>(
                "check_library_updates", Array.Empty<string>(), new[] { "libraryName", "includeUpToDate" }.Concat(Paging).ToArray(), Validate),
            new DomainReadOperationSpec<LibraryReadOperationRequest>(
                "find_type_instances", new[] { "name" }, new[] { "libraryName", "folderPath", "plcName", "version" }, Validate),
        },
        new[]
        {
            new DomainReadField<LibraryReadOperationRequest>("libraryName", o => o.LibraryName is not null),
            new DomainReadField<LibraryReadOperationRequest>("name", o => o.Name is not null, o => !string.IsNullOrWhiteSpace(o.Name)),
            new DomainReadField<LibraryReadOperationRequest>("folderPath", o => o.FolderPath is not null),
            new DomainReadField<LibraryReadOperationRequest>("plcName", o => o.PlcName is not null),
            new DomainReadField<LibraryReadOperationRequest>("version", o => o.Version is not null),
            new DomainReadField<LibraryReadOperationRequest>("includeUpToDate", o => o.IncludeUpToDate is not null),
            new DomainReadField<LibraryReadOperationRequest>("pageSize", o => o.PageSize is not null),
            new DomainReadField<LibraryReadOperationRequest>("cursor", o => o.Cursor is not null),
        });

    public static IReadOnlyList<string> OperationNames => Instance.OperationNames;

    private static void Validate(LibraryReadOperationRequest operation, List<string> errors)
    {
        Text(operation.LibraryName, "libraryName", errors);
        Text(operation.PlcName, "plcName", errors);
        Text(operation.Version, "version", errors);
        if (operation.Name is not null && operation.Name.Length > MaxNameLength)
        {
            errors.Add($"'name' must be at most {MaxNameLength} characters.");
        }

        if (operation.FolderPath is not null
            && (operation.FolderPath.Count > MaxFolderDepth || operation.FolderPath.Any(segment => segment is null || segment.Length > MaxNameLength)))
        {
            errors.Add($"'folderPath' must contain at most {MaxFolderDepth} non-null folder names.");
        }

        if (operation.PageSize is < 1 or > ObjectReadLimits.MaxPageSize)
        {
            errors.Add($"'pageSize' must be between 1 and {ObjectReadLimits.MaxPageSize}.");
        }

        if (operation.Cursor is not null && string.IsNullOrWhiteSpace(operation.Cursor))
        {
            errors.Add("'cursor' must not be blank.");
        }
    }

    private static void Text(string? value, string field, List<string> errors)
    {
        if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > MaxNameLength))
        {
            errors.Add($"'{field}' must be nonblank and at most {MaxNameLength} characters when supplied.");
        }
    }
}
