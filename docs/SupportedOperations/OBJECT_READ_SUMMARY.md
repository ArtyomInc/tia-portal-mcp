# Generic object reads (`object_read`)

`object_read` reads any Openness object of the open project that no dedicated tool covers. It is
read-only, available in both access modes, and returns one canonical JSON document as both the
`content` text block and `structuredContent` (envelope `{ tool, success, batch, error }`, the same
`StructuredOperationBatch` used by `network_read`).

Each call carries 1–50 operations `{ operationId, operation, projectPath?, …fields }`, executed
independently in request order.

## Addressing objects

An object is addressed by `root` and `objectPath`.

- `root`: `project` (default, the bound project) or `portal` (the attached TIA Portal instance).
- `objectPath`: ordered steps from the root; omit it or send `[]` for the root itself.

| `kind` | Fields | Follows |
| --- | --- | --- |
| `composition` | `name`, plus `elementName`, `index`, or both | One element of the named composition. With both, the element at `index` must still be named `elementName`. |
| `attribute` | `name` | An object-valued attribute or navigation property (for example `Software` on `SoftwareContainer`). |
| `service` | `name` (simple or full type name) | The object's service of that type, for example `SoftwareContainer`. |

Copy paths from `list_object_children` results instead of building them by hand: every child
carries its complete `objectPath` with both `index` and `elementName`.

Example — the software of PLC `PLC_1` in station `Station_1`:

```json
[
  { "kind": "composition", "name": "Devices", "elementName": "Station_1" },
  { "kind": "composition", "name": "DeviceItems", "elementName": "PLC_1" },
  { "kind": "service", "name": "SoftwareContainer" },
  { "kind": "attribute", "name": "Software" }
]
```

Failures are explicit and never resolve to another object:

| Situation | Category |
| --- | --- |
| Undeclared composition, attribute, or service; missing element; null attribute | `target_not_found` |
| Several elements with the requested `elementName` (no `index`) | `target_ambiguous` |
| The element at `index` no longer carries `elementName` | `target_evidence_mismatch` |
| An `attribute` step on a scalar value | `target_kind_unsupported` |
| Online, download, or upload services; `Parent`; the Portal's `Projects`; any step reaching a project object | `access_denied` |
| A composition with more than 10,000 elements addressed by name only | `snapshot_too_large` |

## Operations

| Operation | Fields | Result |
| --- | --- | --- |
| `describe_object` | `root?`, `objectPath?` | `typeName`, `name`, sorted `compositions`, `attributes` (`access`, `supportedTypes`, `navigable`), `services` (`allowed`), `exportable`, `diagnostics` |
| `list_object_children` | `root?`, `objectPath?`, `compositionNames?` (1–50), `pageSize?` (1–200, default 50), `cursor?` | `children[]` (`composition`, `index`, `name`, `typeName`, `objectPath`), `totalCount`, `offset`, `nextCursor` |
| `read_object_attributes` | `root?`, `objectPath?`, `attributeNames?` (1–200) | `attributes[]` in the `inspect_network_object` value contract; all declared attributes when `attributeNames` is omitted |
| `export_object` | `root?`, `objectPath?`, `exportOptions?` (`withDefaults`, `withReadOnly`), `offset?`, `maxChars?` (1–30,000, default 16,000) | SimaticML window: `content`, `offset`, `nextOffset`, `totalChars`, whole-document `sha256` |
| `list_capabilities` | — | Installed TIA products and options; availability of the optional Openness assemblies (WinCC, WinCC Unified, Safety, SafetyValidation, TestSuite, Teamcenter, Startdrive) |

### Paging

`list_object_children` orders compositions by name, then elements in Openness enumeration order.
Follow `nextCursor` until it is null and resend the same `root`, `objectPath`, and
`compositionNames`. A cursor used with other query fields fails `cursor_filter_mismatch`; after the
listed children changed it fails `cursor_snapshot_mismatch`.

### Export windows

`export_object` removes the SimaticML `DocumentInfo` element (the export timestamp and installed
products), as `get_block_content` does, so repeated exports of an unchanged object are identical.
Request consecutive windows with `offset = nextOffset` and check every window's `sha256` against
the first: a different digest means the object changed between windows. Objects without
`Export(FileInfo, ExportOptions)` fail `target_kind_unsupported`.

### Attribute values

Values are `null`, `string`, `boolean`, `integer`, `number`, or `enum`. Other CLR values —
engineering objects, `MultilingualText`, `DateTime`, arrays — are reported as `unrepresentable`
with their CLR type; object-valued ones are reachable with an `attribute` step.

## Boundaries

- Nothing is written: no `SetAttribute`, `Invoke`, `Create`, `Delete`, `Import`, or compile.
- Online, download, and upload providers are refused; online reads are roadmap phase R6.
- The walk stays inside the bound project: other open projects are not reachable.
- Live evidence: [R0 read-only acceptance](../superpowers/acceptance/reports/2026-09-25-r0-object-read-live.md).
