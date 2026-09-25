# R0 — Generic Openness object read (`object_read`) design

Date: 2026-09-25 · Phase R0 of the [Openness read coverage roadmap](../../roadmap/openness-read-coverage.md)

## Problem statement

The server reads a narrow, hand-picked slice of TIA Portal Openness: project status, the project
tree, blocks, types, tag tables, cross references, and the network model. An automation engineer
who asks an agent about anything else — a module parameter, a technology-object property, the
comment of a subnet, an HMI screen item, the SimaticML of a watch table — gets "not supported",
even though Openness exposes it generically through `IEngineeringObject`. Adding one hand-written
operation per Openness type does not scale (≈2,400 public types), so without a generic read layer
the read coverage stays a few percent of the API.

## Goals

1. An agent can reach any object of the open project that Openness exposes through compositions,
   object-valued attributes, or services, starting from the project root, without a dedicated
   operation for its type.
2. An agent can read every readable dynamic attribute of a reached object as typed JSON values,
   using the same value contract as `inspect_network_object`.
3. An agent can export any reached object that implements `Export(FileInfo, ExportOptions)` as
   SimaticML text, including documents larger than one response, in verifiable chunks.
4. An agent can discover which optional TIA products are installed before it asks for their
   objects.
5. All of the above works in read-only mode and cannot change the project, the Portal, or a device.

## Non-goals

- **Writing attributes** (`SetAttribute`) — a write layer needs the preview/apply safety model and
  is a separate, later initiative.
- **Invoking actions** (`Invoke`, `Create`, `Delete`, `Import`, `Compile`, online services) — they
  have side effects by definition.
- **Typed domain listings** (watch-table entries, library versions, HMI inventories) — those are
  R1–R5. R0 only provides the generic walk they build upon.
- **Other open projects.** The walk starts at the bound project, or at the Portal root with the
  `Projects` composition hidden, so the generic tool cannot sidestep project binding.
- **Online reads.** Online, download, and upload services are refused (R6).

## User stories

- As an automation engineer, I want to list what an object contains (its compositions, the
  objects in them, and the services it offers) so that I can navigate to a setting whose Openness
  path I do not know.
- As an automation engineer, I want to read the attributes of a module, a technology object, or an
  HMI item so that I can audit its configuration without opening the TIA editor.
- As an automation engineer, I want to export a SimaticML document of an exportable object so that
  I can diff or archive it outside TIA Portal.
- As an agent, I want every object in a listing to carry the exact object path I can send back so
  that I never have to guess how to address it.
- As an agent, I want a stale or ambiguous object path to fail explicitly rather than resolve to a
  different object, so that I never report data about the wrong object.
- As an operator running the server read-only, I want the generic tool to be available and unable
  to mutate anything, so that I can give an agent broad visibility safely.

## Requirements

### P0 — must have

**P0.1 Tool registration.** A new MCP tool `object_read` is registered in both access modes,
annotated `readOnly: true`, `destructive: false`, `openWorld: false`, with a declared output schema.
It accepts 1–50 operations per call, each `{ operationId, operation, projectPath?, …fields }`, and
executes them independently in request order. The response document is the shared
`StructuredOperationBatch` envelope; text content and `structuredContent` come from one canonical
serialization.

**P0.2 Object path.** An object is addressed by `root` (`project`, default, or `portal`) and
`objectPath`, an ordered array of segments:

| Segment `kind` | Required | Optional | Meaning |
| --- | --- | --- | --- |
| `composition` | `name`, and at least one of `elementName` / `index` | — | Element of `GetComposition(name)`. `index` is zero-based. With both, the element at `index` must have `Name == elementName` (evidence). With only `elementName`, exactly one element may match (ordinal). |
| `attribute` | `name` | — | The object-valued result of `GetAttribute(name)`. |
| `service` | `name` | — | `GetService<T>()` for the service type whose simple or full name equals `name`, among `GetServiceInfos()`. |

Resolution rules (worker side, fail closed):

- zero matches → `target_not_found`; more than one `elementName` match → `target_ambiguous`;
  `index` out of range → `target_not_found`; index/name evidence mismatch →
  `target_evidence_mismatch`;
- an `attribute` segment whose value is null or not an `IEngineeringObject` → `target_not_found`
  or `target_kind_unsupported`;
- a composition, attribute, or service name that the current object does not declare →
  `target_not_found`;
- a service whose type name contains `Online`, `Download`, or `Upload` → `access_denied`;
- under `root: portal`, the `Projects` composition → `access_denied`;
- at most 32 segments; every name nonblank; `index` ≥ 0.

**P0.3 `describe_object`.** Returns, for the resolved object: its CLR type name, its `Name` when
readable, and ordinal-sorted lists of (a) compositions `{ name, typeName }`, (b) attributes
`{ name, access, supportedTypes, navigable }` where `navigable` is true when a supported type is
an `IEngineeringObject`, (c) services `{ name, typeName, allowed }`, and (d) `exportable`
(true when the type declares a public `Export(FileInfo, ExportOptions)` method). A failing member
read degrades into a diagnostic entry; it does not fail the whole operation.

**P0.4 `list_object_children`.** Enumerates the elements of the object's compositions, optionally
restricted by `compositionNames` (1–50 unique names). Order: compositions ordinal by name, then
element enumeration order. Each child: `{ composition, index, name, typeName, objectPath }`
where `objectPath` is the complete path (parent path + a `composition` segment carrying both
`index` and `elementName` when the name is readable). Paging: `pageSize` 1–200 (default 50) and an
opaque `cursor` bound to the query (root, path, composition filter) and to a snapshot hash of the
complete ordered child identity list. A cursor used with another query →
`cursor_filter_mismatch`; after the children changed → `cursor_snapshot_mismatch`; malformed →
`invalid_cursor`. More than 10,000 children in the selected compositions → `snapshot_too_large`
with guidance to filter by `compositionNames` or descend one level.

**P0.5 `read_object_attributes`.** Reads `attributeNames` (1–200 unique) or, when omitted, every
attribute declared by `GetAttributeInfos()`. Each entry uses the existing `NetworkAttributeInfo`
contract (`name`, `source: dynamic`, `access`, `supportedTypes`, `availability`, typed `value`,
`diagnostic`). An object-valued attribute is reported as `unrepresentable` with its CLR type name
and is reachable with an `attribute` path segment. A requested name that is not declared →
`unknownAttribute`. Write-only attributes are never read.

**P0.6 `export_object`.** Calls `Export(FileInfo, ExportOptions)` into a fresh temporary directory
that is deleted in `finally`. The `DocumentInfo` element (export timestamp and installed products —
facts about the export run, not the object) is removed exactly as `get_block_content` does, so
repeated exports of an unchanged object are byte-identical; live acceptance showed the raw
`Created` timestamp otherwise changes the digest on every call. `exportOptions` is an optional array of `withDefaults`,
`withReadOnly` (unique; absent → `None`). The result is `{ typeName, totalChars, sha256, offset,
content, nextOffset }`: `content` is a window of at most `maxChars` characters (1–30,000, default
16,000 — canonical JSON escapes `<`, `>`, and `"`, so an XML window grows when serialized and must
stay under the 60,000-character item budget) starting at `offset` (default 0), and `nextOffset` is
null on the last window. `sha256` is
over the whole UTF-8 document so chunk consistency is verifiable. An object without the export
method → `target_kind_unsupported`. Classified `TemporaryExport`.

**P0.7 `list_capabilities`.** Returns the Portal's installed products (`name`, `version`, `options`)
from `TiaPortal.GetCurrentProcess().InstalledSoftware`, and for each optional Openness assembly
(`Siemens.Engineering.WinCC`, `…WinCCUnified`, `…Safety`, `…SafetyValidation`, `…TestSuite`,
`…TeamcenterGateway`, `…MC.Drives`) whether it can be resolved by the worker. No project member is
read. When the process product list cannot be read, the list is empty and a diagnostic says why.

**P0.8 Validation before the worker.** The host catalog rejects unknown operations, missing required
fields, inapplicable fields, duplicate `operationId`s, invalid bounds, and malformed segments with
`validation_error` before any worker call, using the same deterministic order as the network
catalog.

**P0.9 Access policy.** Worker methods `describe_object`, `list_object_children`,
`read_object_attributes`, `list_capabilities` are `Observe`; `export_object` is
`TemporaryExport`. All are allowed in read-only mode. The generic walker calls only
`GetAttributeInfos`, `GetAttribute`, `GetCompositionInfos`, `GetComposition`, `GetServiceInfos`,
`GetService<T>`, and — for `export_object` only — `Export`.

**P0.10 Typed payloads.** Each operation declares exactly one result type registered in an
`ObjectReadPayloadContract`; a non-conforming worker payload becomes `protocol_error` and is not
echoed. Results are JSON objects, never nested JSON strings.

**P0.11 Documentation.** `ARCHITECTURE.md` (tool surface counts and the generic read seam),
`README.md`, `docs/SupportedOperations/OBJECT_READ_SUMMARY.md` (new, indexed), and the roadmap are
updated.

### P1 — nice to have

- `includeValues: false` on `list_object_children` is not needed: children carry identity only.
- A `name` filter on `list_object_children` (exact element name across compositions).

### P2 — future

- `set_object_attributes` preview/apply on the same object path (write roadmap).
- Path segments for associations (`IEngineeringAssociation`) as a fourth kind.

## Acceptance criteria

- Given the bound project, when `list_object_children` runs with an empty path, then the result
  includes the `Devices` composition elements with complete `objectPath`s, and feeding one back to
  `describe_object` resolves the same object.
- Given a path whose `index` now points at a renamed element, when any operation runs, then it fails
  with `target_evidence_mismatch` and no other object is read.
- Given a PLC `DeviceItem` path followed by `service: SoftwareContainer` and `attribute: Software`,
  when `describe_object` runs, then the result is the PLC software with its `BlockGroup`,
  `TagTableGroup`, and `TypeGroup` members.
- Given a PLC tag table path, when `export_object` runs with `maxChars: 1000`, then consecutive
  windows reassemble to `totalChars` characters whose SHA-256 equals `sha256`.
- Given `root: portal` and a `Projects` segment, then the operation fails `access_denied`.
- Given read-only mode, every R0 operation succeeds on the bound project, and no write tool appears.
- Given an unknown field on an operation, then the call fails `validation_error` before the worker.

## Success metrics

- Leading: live acceptance reaches at least one object in each of hardware, PLC software, and
  (when installed) HMI through `object_read` alone; zero unhandled worker exceptions in the run.
- Lagging: R1–R5 domain operations are implemented on top of the R0 path resolver without adding a
  second object-addressing mechanism.

## Open questions

- (engineering, non-blocking) Whether `TiaPortal` itself implements `IEngineeringObject` in V21;
  if it does not, `root: portal` fails `target_kind_unsupported` and R3/R5 address the Portal
  through typed operations instead.
- (engineering, non-blocking) Whether any attribute read has observable side effects in V21. The
  live run records the attribute set read on every visited type.

## Timeline and phasing

R0 ships alone (branch `feature/r0-object-read`) because R1–R5 reuse its path resolver, its
catalog helper, and its payload contract. Live acceptance runs read-only against the disposable
project `test.ap21`.
