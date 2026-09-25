using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.DomainReads;

/// <summary>One read operation of a domain tool: its operation-scoped fields and extra checks.</summary>
public sealed record DomainReadOperationSpec<TRequest>(
    string Name,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<string> OptionalFields,
    Action<TRequest, List<string>>? Validate = null);

/// <summary>One operation-scoped wire field of a domain request and how to tell it was supplied.</summary>
public sealed record DomainReadField<TRequest>(
    string Name,
    Func<TRequest, bool> IsSet,
    Func<TRequest, bool>? IsPresent = null);

public sealed record DomainValidationResult(bool IsValid, string Error)
{
    public static DomainValidationResult Valid() => new(true, string.Empty);

    public static DomainValidationResult Invalid(string error) => new(false, error);
}

/// <summary>
/// Spec-driven validation shared by every structured domain read tool (R0 <c>object_read</c> and
/// the R1–R5 domain tools). Pure and Siemens-free.
///
/// <para>
/// The order is the same as the network catalog's so every tool rejects the same mistakes with the
/// same wording: batch size, then per operation identity (operationId present, bounded, unique),
/// operation name, inapplicable fields, missing required fields, and finally the operation's own
/// bounds and shape checks. Nothing reaches a worker until the whole batch is valid.
/// </para>
/// </summary>
public sealed class DomainReadCatalog<TRequest>
    where TRequest : class, IOperationBatchItem
{
    public const int MaxBatchSize = 50;
    public const int MaxOperationIdLength = 256;

    private readonly string _domain;
    private readonly IReadOnlyDictionary<string, DomainReadOperationSpec<TRequest>> _specs;
    private readonly IReadOnlyList<DomainReadField<TRequest>> _fields;

    public DomainReadCatalog(
        string domain,
        IEnumerable<DomainReadOperationSpec<TRequest>> specs,
        IEnumerable<DomainReadField<TRequest>> fields)
    {
        _domain = domain;
        _specs = specs.ToDictionary(spec => spec.Name, StringComparer.Ordinal);
        _fields = fields.ToArray();
        OperationNames = _specs.Keys.ToArray();

        var known = new HashSet<string>(_fields.Select(field => field.Name), StringComparer.Ordinal);
        foreach (var spec in _specs.Values)
        {
            foreach (var field in spec.RequiredFields.Concat(spec.OptionalFields))
            {
                if (!known.Contains(field))
                {
                    throw new InvalidOperationException(
                        $"{domain} operation '{spec.Name}' declares unknown field '{field}'.");
                }
            }
        }
    }

    /// <summary>Operation names in declaration order.</summary>
    public IReadOnlyList<string> OperationNames { get; }

    public IReadOnlyCollection<DomainReadOperationSpec<TRequest>> All => _specs.Values.ToArray();

    public bool TryGetSpec(string operation, out DomainReadOperationSpec<TRequest>? spec)
        => _specs.TryGetValue(operation, out spec);

    public DomainValidationResult Validate(IReadOnlyList<TRequest>? operations)
    {
        if (operations is null || operations.Count == 0)
        {
            return DomainValidationResult.Invalid("Batch must contain at least one operation.");
        }

        if (operations.Count > MaxBatchSize)
        {
            return DomainValidationResult.Invalid(
                $"Batch exceeds the maximum of {MaxBatchSize} operations (received {operations.Count}).");
        }

        var errors = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            if (operation is null)
            {
                errors.Add("Batch contains a null operation.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(operation.OperationId))
            {
                errors.Add("Each operation requires a unique operationId.");
                continue;
            }

            if (operation.OperationId.Length > MaxOperationIdLength)
            {
                errors.Add($"Each operationId must be at most {MaxOperationIdLength} characters.");
                continue;
            }

            if (!seenIds.Add(operation.OperationId))
            {
                errors.Add($"Duplicate operationId '{operation.OperationId}'.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(operation.Operation))
            {
                errors.Add($"Operation name is required for operationId '{operation.OperationId}'.");
                continue;
            }

            if (!_specs.TryGetValue(operation.Operation, out var spec))
            {
                errors.Add(
                    $"Unknown operation '{operation.Operation}' for operationId '{operation.OperationId}'. "
                    + $"Valid {_domain} operations: {string.Join(", ", OperationNames)}.");
                continue;
            }

            var applicable = new HashSet<string>(spec.RequiredFields.Concat(spec.OptionalFields), StringComparer.Ordinal);
            foreach (var field in _fields.Where(field => !applicable.Contains(field.Name) && field.IsSet(operation)))
            {
                var valid = spec.OptionalFields.Count > 0 ? string.Join(", ", spec.OptionalFields) : "(none)";
                errors.Add(
                    $"Operation '{operation.Operation}' (operationId '{operation.OperationId}'): '{field.Name}' is not valid for "
                    + $"{operation.Operation}. Valid optional fields: {valid}.");
            }

            var missing = spec.RequiredFields
                .Where(name => !IsPresent(operation, name))
                .ToArray();
            if (missing.Length > 0)
            {
                errors.Add(
                    $"Operation '{operation.Operation}' (operationId '{operation.OperationId}') is missing required field(s): {string.Join(", ", missing)}.");
            }

            if (spec.Validate is not null)
            {
                var specific = new List<string>();
                spec.Validate(operation, specific);
                errors.AddRange(specific.Select(error =>
                    $"Operation '{operation.Operation}' (operationId '{operation.OperationId}'): {error}"));
            }
        }

        return errors.Count == 0
            ? DomainValidationResult.Valid()
            : DomainValidationResult.Invalid(string.Join("\n", errors));
    }

    public IReadOnlyList<string> ValidateAccessMode(IReadOnlyList<TRequest> operations, McpAccessMode mode)
        => operations
            .Where(operation => operation is not null
                && !string.IsNullOrWhiteSpace(operation.Operation)
                && !OperationPolicyCatalog.IsAllowed(mode, operation.Operation))
            .Select(operation =>
                $"Operation '{operation.Operation}' (operationId '{operation.OperationId}') is not permitted in read-only mode.")
            .ToArray();

    private bool IsPresent(TRequest operation, string name)
    {
        var field = _fields.First(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        return (field.IsPresent ?? field.IsSet)(operation);
    }
}
