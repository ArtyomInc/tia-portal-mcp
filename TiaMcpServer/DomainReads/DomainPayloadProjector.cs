using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.DomainReads;

/// <summary>
/// Projects one worker outcome of a structured domain read into its batch item, decoding a
/// success payload strictly as the operation's single declared result type.
///
/// <para>
/// A payload that does not decode — malformed, unknown member, wrong casing, wrong type, or
/// rejected by the contract's validator — becomes a failed item with category
/// <see cref="WorkerFailureCategories.ProtocolError"/>. The rejected payload is never echoed: the
/// caller asked for contract-shaped data, not the bytes that failed the contract.
/// </para>
/// </summary>
public static class DomainPayloadProjector
{
    public static StructuredOperationItem Project(
        IOperationBatchItem operation,
        WorkerCallResult workerResult,
        Func<string, JsonElement> decode,
        Action<string>? writeProtocolDiagnostic = null)
    {
        var warnings = workerResult.Warnings ?? Array.Empty<string>();
        if (!workerResult.Success)
        {
            return Failed(
                operation,
                workerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                workerResult.Error ?? $"Operation '{operation.Operation}' failed.",
                warnings);
        }

        JsonElement result;
        try
        {
            result = decode(workerResult.Payload);
        }
        catch (JsonException)
        {
            try
            {
                (writeProtocolDiagnostic ?? Console.Error.WriteLine)(
                    $"TiaMcpServer: worker payload contract rejection: operation={operation.Operation}.");
            }
            catch
            {
                // Diagnostics must never replace the stable fail-closed protocol_error response.
            }

            return Failed(
                operation,
                WorkerFailureCategories.ProtocolError,
                $"The worker payload for '{operation.Operation}' did not match its declared result "
                    + "contract and was rejected.",
                warnings);
        }

        return new StructuredOperationItem(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Succeeded,
            result,
            Failure: null,
            Omission: null,
            SkipReason: null,
            warnings);
    }

    /// <summary>Strict typed decode through the canonical JSON gate.</summary>
    public static JsonElement Decode<T>(string payload, Action<T>? validate = null)
        => Json.CanonicalJson.Normalize(payload, validate).Element;

    /// <summary>
    /// Whether a domain-listing value has one of the published plain shapes: a JSON scalar, a
    /// color <c>{"hex": "#RRGGBB", "alpha": 0-255}</c>, a multilingual text (an object of culture
    /// name → string), or an array of those that holds no nested array.
    /// </summary>
    public static bool IsListingValue(object? value)
        => value is not JsonElement element || IsListingElement(element, allowArray: true);

    private static bool IsListingElement(JsonElement element, bool allowArray)
        => element.ValueKind switch
        {
            JsonValueKind.Object => IsColor(element) || element.EnumerateObject().All(text => text.Value.ValueKind == JsonValueKind.String),
            JsonValueKind.Array => allowArray && element.EnumerateArray().All(item => IsListingElement(item, allowArray: false)),
            _ => true,
        };

    private static bool IsColor(JsonElement element)
        => element.EnumerateObject().Count() == 2
            && element.TryGetProperty("hex", out var hex)
            && hex.ValueKind == JsonValueKind.String
            && System.Text.RegularExpressions.Regex.IsMatch(hex.GetString()!, "^#[0-9A-F]{6}$")
            && element.TryGetProperty("alpha", out var alpha)
            && alpha.TryGetInt32(out var channel)
            && channel is >= 0 and <= 255;

    /// <summary>Validator helper: rejects an explicit JSON null in a non-nullable member.</summary>
    public static void RequireNotNull(object? value, string member)
    {
        if (value is null)
        {
            throw new JsonException($"Required member '{member}' was null.");
        }
    }

    public static StructuredOperationItem Failed(
        IOperationBatchItem operation,
        string category,
        string message,
        IReadOnlyList<string>? warnings = null)
        => new(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Failed,
            Result: null,
            new StructuredOperationFailure(category, message),
            Omission: null,
            SkipReason: null,
            warnings ?? Array.Empty<string>());
}
