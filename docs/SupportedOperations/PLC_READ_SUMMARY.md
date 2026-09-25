# PLC program reads (`plc_read`)

`plc_read` returns typed, audit-oriented views of a PLC program. It is read-only, available in both
access modes, works offline, and returns one canonical JSON document as both `content` and
`structuredContent` (`{ tool, success, batch, error }`). Each call carries 1–50 operations
`{ operationId, operation, projectPath?, plcName?, …fields }`, run independently.

## Selecting the PLC

`plcName` is the exact PLC software name returned by `list_plcs`. It may be omitted when the
project contains exactly one PLC. PLCs are found in ungrouped devices and in every device group, at
any device-item depth. An unknown name fails `target_not_found` and lists the available PLCs;
several PLCs without `plcName`, or a duplicated name, fail `target_ambiguous`.

## Operations

| Operation | Fields | Result |
| --- | --- | --- |
| `list_plcs` | — | `plcs[]`: `name`, `deviceName`, `deviceGroupPath`, `objectPath` |
| `list_blocks` | `plcName?`, `nameContains?`, `pageSize?`, `cursor?` | Blocks of user and system groups (see values below) |
| `list_types` | same | PLC data types |
| `list_watch_tables` | same | Watch and force tables |
| `list_technology_objects` | same | Technology objects |
| `list_external_sources` | same | External source files |
| `list_software_units` | same | Software Units and Safety Units |
| `list_alarm_text_lists` | same | User and system alarm text lists |
| `list_opcua_server_interfaces` | same | OPC UA server interfaces |
| `read_watch_table` | `name`, `groupPath?`, `plcName?`, paging | Entries of one watch or force table |
| `read_technology_object` | `name`, `groupPath?`, `parameterNames?`, `plcName?`, paging | Parameters (`name`, `values.Value`) of one technology object |
| `read_block_fingerprints` | `name`, `groupPath?`, `plcName?` | Offline fingerprints of one block (`Code`, `Interface`, `Comments`, `Properties`, …) |
| `read_checksums` | `plcName?` | `software` and `textLists` checksums of the PLC |
| `compare_software` | `comparePlcName`, `includeIdentical?`, `plcName?`, paging | Offline comparison of two PLCs of the project |

### Listing items

Every listing returns `{ plcName, items[], totalCount, offset, nextCursor, diagnostics }`. An item
is `{ name, kind, typeName, groupPath, objectPath, values, unavailable }`:

- `kind` is the simple type (`OB`, `FB`, `FC`, `GlobalDB`, `InstanceDB`, `PlcStruct`,
  `PlcWatchTable`, `PlcForceTable`, …); `groupPath` lists the group names below the listing root
  (system block groups included).
- `objectPath` addresses the object for `object_read`, so any attribute not in `values` is one call
  away.
- `values` holds the documented attributes the object declares, as JSON scalars: enums as their
  symbol, dates as ISO-8601 strings, versions as strings. `unavailable` names declared attributes
  that could not be read or represented.

| Listing | Documented values |
| --- | --- |
| Blocks | `Number`, `ProgrammingLanguage`, `SecondaryType`, `EventClass`, `InstanceOfName`, `InstanceOfType`, `IsConsistent`, `IsKnowHowProtected`, `IsWriteProtected`, `MemoryLayout`, `Namespace`, `HeaderAuthor`, `HeaderFamily`, `HeaderName`, `HeaderVersion`, `CreationDate`, `ModifiedDate`, `CodeModifiedDate`, `InterfaceModifiedDate`, `CompileDate`, `LoadMemoryLength`, `WorkMemoryLength` |
| Types | `IsConsistent`, `IsKnowHowProtected`, `IsFailsafeCompliant`, `LibraryConformanceStatus`, `Namespace`, `CreationDate`, `ModifiedDate`, `InterfaceModifiedDate` |
| Watch/force tables | `IsConsistent` |
| Technology objects | `Number`, `OfSystemLibElement`, `OfSystemLibVersion`, `InstanceOfName`, `IsConsistent`, `IsKnowHowProtected`, `ProgrammingLanguage`, `Namespace`, `CreationDate`, `ModifiedDate`, `CompileDate` |
| External sources, units, text lists, OPC UA interfaces | Every declared scalar attribute |

Watch and force table entries report `Address`, `DisplayFormat`, `MonitorTrigger`,
`ModifyIntention`, `ModifyTrigger`, `ModifyValue`, `ForceIntention`, and `ForceValue` where
declared.

### Selecting one object

`read_watch_table`, `read_technology_object`, and `read_block_fingerprints` select by exact `name`.
When several objects share the name, add `groupPath` (as returned by the listing; `[]` for the root
group), otherwise the call fails `target_ambiguous`.

### Comparison

`compare_software` runs `PlcSoftware.CompareTo` between `plcName` and `comparePlcName`. The result
tree is flattened depth-first into `{ path, depth, leftName, rightName, state, detail }`. By default
identical elements (`ObjectsIdentical`, `FolderContentsIdentical`) are omitted; `includeIdentical:
true` returns them. States include `ObjectsDifferent`, `LeftMissing`, `RightMissing`, and
`FolderContentsDifferent`.

### Paging

Listings, table entries, parameters, and comparisons are paged with `pageSize` (1–200, default 50)
and `cursor`, with the same rules as `object_read`: resend the same fields, and expect
`cursor_filter_mismatch` or `cursor_snapshot_mismatch` if the query or the listed objects changed.

## Boundaries

- Nothing is written, compiled, or downloaded.
- Online comparison and online fingerprints contact a device and are not offered (roadmap R6).
- ProDiag supervisions are not listed: V21 exposes no generic attribute or composition surface for
  them.
- Block and UDT source content: `execute_read_batch`. Tag-table SimaticML: `object_read`
  `export_object`.
- Live evidence: [R1 read-only acceptance](../superpowers/acceptance/reports/2026-09-25-r1-plc-read-live.md).
