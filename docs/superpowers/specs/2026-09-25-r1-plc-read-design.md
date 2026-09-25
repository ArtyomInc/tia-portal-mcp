# R1 — PLC read domain (`plc_read`) design

Date: 2026-09-25 · Phase R1 of the [Openness read coverage roadmap](../../roadmap/openness-read-coverage.md)
Builds on: [R0 generic object read](2026-09-25-r0-object-read-design.md)

## Problem statement

`object_read` can reach every PLC object, but only one object or one composition at a time. The
questions an automation engineer actually asks about a PLC program — "which blocks are
inconsistent or know-how protected?", "what is in this watch table?", "how is this axis
parameterized?", "did the program change since the last checksum?", "how do PLC_A and PLC_B
differ?" — need tens of generic calls, or are not answerable at all because the data is behind a
method (`GetFingerprints`, `CompareTo`) rather than an attribute. Existing reads cover block and
UDT source and tag tables only.

## Goals

1. One call lists every block, UDT, watch/force table, technology object, external source,
   Software Unit, alarm text list, or OPC UA server interface of a PLC — across nested groups —
   with the metadata used for audits and an R0 `objectPath` for drill-down.
2. One call returns the content that is not exposed as a scalar attribute: watch/force table
   entries, technology-object parameters, block fingerprints, program checksums.
3. One call compares two PLCs of the project offline and returns the differing elements.
4. Everything is read-only and available in read-only mode.

## Non-goals

- Online comparison and online fingerprints (`CompareToOnline`, `FingerprintDataProvider`) — they
  contact a device (R6).
- ProDiag supervisions: `SupervisionProvider` exposes no attribute or composition surface in V21;
  it needs a separate method-level investigation.
- Tag-table SimaticML export: already served by `object_read` `export_object`.
- Block and UDT source content: already served by `execute_read_batch`.
- Writes of any kind.

## User stories

- As a PLC engineer, I want the list of blocks with number, language, consistency, protection,
  and modification dates so that I can audit a program without opening every block.
- As a commissioning engineer, I want the entries of a watch or force table so that I can review
  what will be monitored or forced.
- As a motion engineer, I want a technology object's parameters by name so that I can check an
  axis configuration.
- As a quality engineer, I want the program and text-list checksums and per-block fingerprints so
  that I can detect unreviewed changes.
- As an integrator, I want the offline differences between two PLCs of the project so that I can
  verify a copy or a variant.
- As an agent, I want each listed object to carry its `objectPath` so that I can read any other
  attribute with `object_read`.

## Requirements (P0)

**P0.1 Tool.** New MCP tool `plc_read` on the shared DomainReads framework; 1–50 operations;
registered in both access modes; read-only annotations; `DomainReadResponse` output schema.

**P0.2 PLC selection.** Every operation except `list_plcs` accepts optional `plcName` (exact,
ordinal PLC software name). Omitted: the project must contain exactly one PLC software. Zero
matches → `target_not_found`; several → `target_ambiguous`. PLCs are found in `Devices` and
recursively in `DeviceGroups`, in every nested device item.

**P0.3 Listing shape.** `list_blocks`, `list_types`, `list_watch_tables`,
`list_technology_objects`, `list_external_sources`, `list_software_units`,
`list_alarm_text_lists`, `list_opcua_server_interfaces` return
`{ plcName, items[], totalCount, offset, nextCursor, diagnostics }`; each item is
`{ name, kind, typeName, groupPath[], objectPath[], values{}, unavailable[] }`. `kind` is the
simple type name (`OB`, `FB`, `GlobalDB`, `PlcStruct`, `PlcWatchTable`, …). `values` holds the
operation's documented attributes that the object declares, as JSON scalars (enums as their
symbol, dates as ISO-8601 UTC strings, versions as strings); `unavailable` names documented
attributes that are declared but could not be read or represented. Items are ordered by group
depth-first (groups by name), then by composition order. Optional `nameContains`
(case-insensitive) filter; `pageSize` 1–200 (default 50) and `cursor` with R0 cursor semantics.

**P0.4 Detail reads.** `read_watch_table` (watch or force table) and `read_technology_object`
select one object by `name` and optional `groupPath` (exactly one match) and return
`{ plcName, target, entries[], totalCount, offset, nextCursor }` with entries
`{ name, values{}, unavailable[] }`, paged. `read_technology_object` accepts `parameterNames`
(1–200) to restrict parameters.

**P0.5 Fingerprints and checksums.** `read_block_fingerprints` (`name`, optional `groupPath`)
returns `{ plcName, target, fingerprints[{ id, value }] }` from the block's offline
`FingerprintProvider`. `read_checksums` returns the `PlcChecksumProvider` `software` and
`textLists` checksums.

**P0.6 Comparison.** `compare_software` compares the selected PLC (`plcName`) with
`comparePlcName` through `PlcSoftware.CompareTo`. The result tree is flattened depth-first into
`{ path[], depth, leftName, rightName, state, detail }`; by default only elements whose state is
not `ObjectsIdentical`/`FolderContentsIdentical` are returned (`includeIdentical: true` returns
all). Paged like listings.

**P0.7 Policy and safety.** All worker methods are `Observe`. Only getters, compositions,
services, `GetFingerprints()`, and `CompareTo()` are called. A compared or listed object is never
modified.

## Acceptance criteria

- Given `test.ap21`, `list_plcs` returns `PLC_1` with its R0 path, and `list_blocks` without
  `plcName` returns `Main` (OB 1, LAD), `fbValve`, `DiFbValve` (instance of `fbValve`), and `DbHmi`
  with ISO dates and consistency flags; each `objectPath` resolves with `object_read`.
- `list_types` returns the three UDTs; `list_watch_tables` returns the force table.
- `read_block_fingerprints` for `Main` returns non-empty fingerprint values.
- `read_checksums` returns the two checksums shown by the generic reader.
- `compare_software` of `PLC_1` with itself returns zero differences, and all elements with
  `includeIdentical: true`.
- A `plcName` that does not exist fails `target_not_found`; an unknown operation or field fails
  `validation_error` before any worker call.

## Open questions (non-blocking)

- The live project has no technology objects, external sources, units, alarm text lists, or OPC UA
  interfaces; those listings are verified live only for their empty case and offline on fixtures.

## Phasing

Branch `feature/r1-plc-read`, stacked on R0.
