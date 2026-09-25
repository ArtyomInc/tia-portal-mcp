using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.DomainReads;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.PlcRead;

/// <summary>
/// The only decoder of <c>plc_read</c> worker success payloads: one declared result type per
/// operation, strictly decoded, rejected as <c>protocol_error</c> without echo otherwise.
/// </summary>
public static class PlcReadPayloadContract
{
    public static StructuredOperationItem Project(PlcReadOperationRequest operation, WorkerCallResult workerResult)
        => DomainPayloadProjector.Project(operation, workerResult, payload => Decode(operation.Operation, payload));

    public static JsonElement Decode(string operation, string payload)
    {
        if (PlcReadCatalog.ListingOperations.Contains(operation))
        {
            return DomainPayloadProjector.Decode<PlcObjectListInfo>(payload, ValidateList);
        }

        return operation switch
        {
            "list_plcs" => DomainPayloadProjector.Decode<PlcListInfo>(payload, ValidatePlcs),
            "read_watch_table" or "read_technology_object" => DomainPayloadProjector.Decode<PlcObjectEntriesInfo>(payload, ValidateEntries),
            "read_block_fingerprints" => DomainPayloadProjector.Decode<PlcFingerprintsInfo>(payload, ValidateFingerprints),
            "read_checksums" => DomainPayloadProjector.Decode<PlcChecksumsInfo>(payload, ValidateChecksums),
            "compare_software" => DomainPayloadProjector.Decode<PlcCompareInfo>(payload, ValidateCompare),
            _ => throw new JsonException($"No declared result contract for plc_read operation '{operation}'."),
        };
    }

    private static void ValidatePlcs(PlcListInfo value)
    {
        DomainPayloadProjector.RequireNotNull(value.Plcs, "plcs");
        DomainPayloadProjector.RequireNotNull(value.Diagnostics, "diagnostics");
        foreach (var plc in value.Plcs)
        {
            DomainPayloadProjector.RequireNotNull(plc?.Name, "plcs[].name");
            DomainPayloadProjector.RequireNotNull(plc!.DeviceName, "plcs[].deviceName");
            DomainPayloadProjector.RequireNotNull(plc.DeviceGroupPath, "plcs[].deviceGroupPath");
            ValidatePath(plc.ObjectPath, "plcs[].objectPath");
        }
    }

    private static void ValidateList(PlcObjectListInfo value)
    {
        DomainPayloadProjector.RequireNotNull(value.PlcName, "plcName");
        DomainPayloadProjector.RequireNotNull(value.Items, "items");
        DomainPayloadProjector.RequireNotNull(value.Diagnostics, "diagnostics");
        ValidatePage(value.TotalCount, value.Offset, value.Items.Count, value.NextCursor);
        foreach (var item in value.Items)
        {
            ValidateObject(item, "items[]");
        }
    }

    private static void ValidateEntries(PlcObjectEntriesInfo value)
    {
        DomainPayloadProjector.RequireNotNull(value.PlcName, "plcName");
        ValidateObject(value.Target, "target");
        DomainPayloadProjector.RequireNotNull(value.Entries, "entries");
        DomainPayloadProjector.RequireNotNull(value.Diagnostics, "diagnostics");
        ValidatePage(value.TotalCount, value.Offset, value.Entries.Count, value.NextCursor);
        foreach (var entry in value.Entries)
        {
            DomainPayloadProjector.RequireNotNull(entry, "entries[]");
            DomainPayloadProjector.RequireNotNull(entry.Values, "entries[].values");
            DomainPayloadProjector.RequireNotNull(entry.Unavailable, "entries[].unavailable");
            ValidateScalars(entry.Values, "entries[].values");
        }
    }

    private static void ValidateFingerprints(PlcFingerprintsInfo value)
    {
        DomainPayloadProjector.RequireNotNull(value.PlcName, "plcName");
        ValidateObject(value.Target, "target");
        DomainPayloadProjector.RequireNotNull(value.Fingerprints, "fingerprints");
        foreach (var fingerprint in value.Fingerprints)
        {
            DomainPayloadProjector.RequireNotNull(fingerprint?.Id, "fingerprints[].id");
        }
    }

    private static void ValidateChecksums(PlcChecksumsInfo value)
    {
        DomainPayloadProjector.RequireNotNull(value.PlcName, "plcName");
        DomainPayloadProjector.RequireNotNull(value.Diagnostics, "diagnostics");
    }

    private static void ValidateCompare(PlcCompareInfo value)
    {
        DomainPayloadProjector.RequireNotNull(value.LeftPlcName, "leftPlcName");
        DomainPayloadProjector.RequireNotNull(value.RightPlcName, "rightPlcName");
        DomainPayloadProjector.RequireNotNull(value.Elements, "elements");
        ValidatePage(value.TotalCount, value.Offset, value.Elements.Count, value.NextCursor);
        foreach (var element in value.Elements)
        {
            DomainPayloadProjector.RequireNotNull(element, "elements[]");
            DomainPayloadProjector.RequireNotNull(element.Path, "elements[].path");
            if (string.IsNullOrEmpty(element.State) || element.Depth < 0 || element.Depth != element.Path.Count)
            {
                throw new JsonException("A comparison element is inconsistent.");
            }
        }
    }

    private static void ValidateObject(DomainObjectInfo? item, string member)
    {
        DomainPayloadProjector.RequireNotNull(item, member);
        DomainPayloadProjector.RequireNotNull(item!.Name, member + ".name");
        DomainPayloadProjector.RequireNotNull(item.Kind, member + ".kind");
        DomainPayloadProjector.RequireNotNull(item.TypeName, member + ".typeName");
        DomainPayloadProjector.RequireNotNull(item.GroupPath, member + ".groupPath");
        DomainPayloadProjector.RequireNotNull(item.Values, member + ".values");
        DomainPayloadProjector.RequireNotNull(item.Unavailable, member + ".unavailable");
        ValidatePath(item.ObjectPath, member + ".objectPath");
        ValidateScalars(item.Values, member + ".values");
    }

    private static void ValidatePath(List<ObjectPathSegmentInfo>? path, string member)
    {
        DomainPayloadProjector.RequireNotNull(path, member);
        if (path!.Count == 0 || path.Any(segment => segment is null || !ObjectPathSegmentKinds.IsKnown(segment.Kind) || string.IsNullOrEmpty(segment.Name)))
        {
            throw new JsonException($"'{member}' is not a valid object path.");
        }
    }

    /// <summary>Values must use the published listing shapes (<see cref="DomainPayloadProjector.IsListingValue"/>).</summary>
    private static void ValidateScalars(Dictionary<string, object?> values, string member)
    {
        if (!values.Values.All(DomainPayloadProjector.IsListingValue))
        {
            throw new JsonException($"'{member}' must hold scalar values.");
        }
    }

    private static void ValidatePage(int totalCount, int offset, int count, string? nextCursor)
    {
        if (totalCount < 0 || offset < 0 || offset + count > totalCount
            || (nextCursor is null) != (offset + count >= totalCount))
        {
            throw new JsonException("The page position is inconsistent.");
        }
    }
}
