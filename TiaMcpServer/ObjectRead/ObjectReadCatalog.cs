using TiaMcpServer.Contracts;
using TiaMcpServer.DomainReads;

namespace TiaMcpServer.ObjectRead;

/// <summary>Whitelists and validates the generic <c>object_read</c> operations. Pure and Siemens-free.</summary>
public static class ObjectReadCatalog
{
    private static readonly string[] PathFields = { "root", "objectPath" };

    public static DomainReadCatalog<ObjectReadOperationRequest> Instance { get; } = new(
        "object_read",
        new[]
        {
            new DomainReadOperationSpec<ObjectReadOperationRequest>(
                "describe_object", Array.Empty<string>(), PathFields, ValidatePath),
            new DomainReadOperationSpec<ObjectReadOperationRequest>(
                "list_object_children",
                Array.Empty<string>(),
                PathFields.Concat(new[] { "compositionNames", "pageSize", "cursor" }).ToArray(),
                ValidateChildren),
            new DomainReadOperationSpec<ObjectReadOperationRequest>(
                "read_object_attributes",
                Array.Empty<string>(),
                PathFields.Concat(new[] { "attributeNames" }).ToArray(),
                ValidateAttributes),
            new DomainReadOperationSpec<ObjectReadOperationRequest>(
                "export_object",
                Array.Empty<string>(),
                PathFields.Concat(new[] { "exportOptions", "offset", "maxChars" }).ToArray(),
                ValidateExport),
            new DomainReadOperationSpec<ObjectReadOperationRequest>(
                "list_capabilities", Array.Empty<string>(), Array.Empty<string>()),
        },
        new[]
        {
            new DomainReadField<ObjectReadOperationRequest>("root", o => o.Root is not null),
            new DomainReadField<ObjectReadOperationRequest>("objectPath", o => o.ObjectPath is not null),
            new DomainReadField<ObjectReadOperationRequest>("attributeNames", o => o.AttributeNames is not null),
            new DomainReadField<ObjectReadOperationRequest>("compositionNames", o => o.CompositionNames is not null),
            new DomainReadField<ObjectReadOperationRequest>("pageSize", o => o.PageSize is not null),
            new DomainReadField<ObjectReadOperationRequest>("cursor", o => o.Cursor is not null),
            new DomainReadField<ObjectReadOperationRequest>("exportOptions", o => o.ExportOptions is not null),
            new DomainReadField<ObjectReadOperationRequest>("offset", o => o.Offset is not null),
            new DomainReadField<ObjectReadOperationRequest>("maxChars", o => o.MaxChars is not null),
        });

    public static IReadOnlyList<string> OperationNames => Instance.OperationNames;

    private static void ValidatePath(ObjectReadOperationRequest operation, List<string> errors)
    {
        if (operation.Root is not null && !ObjectRoots.IsKnown(operation.Root))
        {
            errors.Add($"'root' must be one of: {string.Join(", ", ObjectRoots.All)}.");
        }

        if (operation.ObjectPath is null)
        {
            return;
        }

        if (operation.ObjectPath.Count > ObjectReadLimits.MaxPathSegments)
        {
            errors.Add($"'objectPath' must contain at most {ObjectReadLimits.MaxPathSegments} segments.");
            return;
        }

        for (var i = 0; i < operation.ObjectPath.Count; i++)
        {
            var segment = operation.ObjectPath[i];
            if (segment is null)
            {
                errors.Add($"'objectPath[{i}]' must not be null.");
                continue;
            }

            if (!ObjectPathSegmentKinds.IsKnown(segment.Kind))
            {
                errors.Add($"'objectPath[{i}].kind' must be one of: {string.Join(", ", ObjectPathSegmentKinds.All)}.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(segment.Name) || segment.Name.Length > ObjectReadLimits.MaxNameLength)
            {
                errors.Add($"'objectPath[{i}].name' must be nonblank and at most {ObjectReadLimits.MaxNameLength} characters.");
            }

            if (segment.Kind == ObjectPathSegmentKinds.Composition)
            {
                if (segment.Index is null && segment.ElementName is null)
                {
                    errors.Add($"'objectPath[{i}]' is a composition step and requires elementName, index, or both.");
                }

                if (segment.Index is < 0)
                {
                    errors.Add($"'objectPath[{i}].index' must be 0 or greater.");
                }

                if (segment.ElementName is not null
                    && (segment.ElementName.Length == 0 || segment.ElementName.Length > ObjectReadLimits.MaxNameLength))
                {
                    errors.Add($"'objectPath[{i}].elementName' must be nonempty and at most {ObjectReadLimits.MaxNameLength} characters.");
                }
            }
            else if (segment.Index is not null || segment.ElementName is not null)
            {
                errors.Add($"'objectPath[{i}]': elementName and index apply only to composition steps.");
            }
        }
    }

    private static void ValidateChildren(ObjectReadOperationRequest operation, List<string> errors)
    {
        ValidatePath(operation, errors);
        ValidateNames(operation.CompositionNames, "compositionNames", ObjectReadLimits.MaxCompositionNames, errors);
        if (operation.PageSize is < 1 or > ObjectReadLimits.MaxPageSize)
        {
            errors.Add($"'pageSize' must be between 1 and {ObjectReadLimits.MaxPageSize}.");
        }

        if (operation.Cursor is not null && string.IsNullOrWhiteSpace(operation.Cursor))
        {
            errors.Add("'cursor' must not be blank.");
        }
    }

    private static void ValidateAttributes(ObjectReadOperationRequest operation, List<string> errors)
    {
        ValidatePath(operation, errors);
        ValidateNames(operation.AttributeNames, "attributeNames", ObjectReadLimits.MaxAttributeNames, errors);
    }

    private static void ValidateExport(ObjectReadOperationRequest operation, List<string> errors)
    {
        ValidatePath(operation, errors);
        if (operation.ExportOptions is not null)
        {
            if (operation.ExportOptions.Any(option => !ObjectExportOptionNames.IsKnown(option)))
            {
                errors.Add($"'exportOptions' values must be one of: {string.Join(", ", ObjectExportOptionNames.All)}.");
            }
            else if (operation.ExportOptions.Distinct(StringComparer.Ordinal).Count() != operation.ExportOptions.Count)
            {
                errors.Add("'exportOptions' values must be unique.");
            }
        }

        if (operation.Offset is < 0)
        {
            errors.Add("'offset' must be 0 or greater.");
        }

        if (operation.MaxChars is < 1 or > ObjectReadLimits.MaxExportMaxChars)
        {
            errors.Add($"'maxChars' must be between 1 and {ObjectReadLimits.MaxExportMaxChars}.");
        }
    }

    private static void ValidateNames(IReadOnlyList<string>? names, string field, int max, List<string> errors)
    {
        if (names is null)
        {
            return;
        }

        if (names.Count == 0 || names.Count > max
            || names.Any(string.IsNullOrWhiteSpace)
            || names.Distinct(StringComparer.Ordinal).Count() != names.Count)
        {
            errors.Add($"'{field}' must contain between 1 and {max} unique, nonblank names when supplied.");
        }
    }
}
