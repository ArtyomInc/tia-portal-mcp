# Openness Read Coverage Roadmap

Status: R0 (generic object reads), R1 (PLC reads), and R2 (hardware reads) are implemented with
read-only live acceptance. R3–R5 follow in order; R6 (online reads) is deliberately last and gated behind an
explicit opt-in.

This roadmap widens the **read** surface of the server toward the full TIA Portal V21 Openness API
without growing the tool list linearly with the API. It records direction and sequence; each phase
has its own design under `docs/superpowers/specs/` and its own plan under
`docs/superpowers/plans/`.

## Why a layered approach

The V21 Openness assemblies expose roughly 2,400 public types. One MCP tool per Openness function
would produce hundreds of tools, which MCP clients and language models handle poorly. Coverage is
instead built in two layers:

- **Generic layer (R0).** About 415 Openness types implement `IEngineeringObject`
  (`GetAttributeInfos`, `GetAttribute`, `GetCompositionInfos`, `GetComposition`,
  `GetServiceInfos`, `GetService<T>`), and 26 types expose `Export(FileInfo, ExportOptions)`. One
  generic tool, `object_read`, walks that object model by an explicit, evidence-checked object path
  and therefore reaches the long tail — hardware module parameters, HMI screen items, technology
  object properties — without a dedicated operation per type.
- **Domain layer (R1–R5).** One read tool per domain returns typed, task-shaped results where the
  generic layer is too low-level: listings with stable identities, content that is not an attribute
  (watch-table entries, script code, library versions), and comparisons.

Every tool in this roadmap is read-only, registered in both access modes, reuses the canonical JSON
seam (`StructuredToolResult`, `StructuredOperationBatch`, typed worker payload decoding), and never
invokes an Openness action that changes the project, the Portal, a device, or a file outside a
temporary export directory.

## Phases

| Phase | Tool | Scope | Status |
| --- | --- | --- | --- |
| R0 | `object_read` (new) | Object-path addressing, `describe_object`, `list_object_children`, `read_object_attributes`, `export_object`, `list_capabilities` | Done — [acceptance](../superpowers/acceptance/reports/2026-09-25-r0-object-read-live.md) |
| R1 | `plc_read` (new) | Block and type listings with metadata, watch/force tables and entries, technology objects and parameters, alarm text lists, Software Units, external sources, OPC UA server interfaces, block fingerprints, program checksums, offline software comparison (ProDiag deferred: no generic surface in V21; tag-table export is `object_read`) | Done — [acceptance](../superpowers/acceptance/reports/2026-09-25-r1-plc-read-live.md) |
| R2 | `network_read` (extended) | Device-group tree, unplugged items, hardware identifiers, port topology, offline hardware comparison (communication connections were already covered by `list_network_objects`; diagnostics settings via `object_read`) | Done — [acceptance](../superpowers/acceptance/reports/2026-09-25-r2-hardware-read-live.md) |
| R3 | `library_read` (new) | Project and open global libraries: types, versions, status, dependencies, master copies, update check, instance search, library comparison | Implemented — live acceptance in the combined R3–R5 read-only run |
| R4 | `hmi_read` (new) | WinCC Classic (`HmiTarget`) and WinCC Unified (`HmiSoftware`) inventories: screens, tags, connections, alarms, logs, text/graphic lists, scripts | Implemented — live acceptance in the combined R3–R5 read-only run |
| R5 | `governance_read` (new) | Portal processes and settings, project texts, UMAC users/roles, Safety administration and signatures, TestSuite inventory, Multiuser and VCI visibility | Implemented — live acceptance in the combined R3–R5 read-only run |
| R6 | behind `--enable-online` | Online state and online comparison — contacts a real device | Not scheduled |

## Cross-cutting rules

- **Capability detection.** An optional product (WinCC Unified, Safety, TestSuite, Multiuser, …)
  that is not installed or not licensed makes the affected operation fail with
  `capability_unavailable`, never a worker crash.
- **No side effects.** Reads that Openness implements as actions with effects stay out of scope:
  running tests, generating Safety validation reports, going online, opening a global library
  read-write, or any `Invoke`/`Create`/`SetAttribute` call.
- **Credentials.** Operations that need a password (know-how protection, Safety login, UMAC
  passwords, online passwords) are out of scope for the read roadmap.
- **Stubs.** CI builds with `/p:UseTiaPortalReferenceStubs=true`. Any phase that compiles against a
  new Siemens assembly must add its reference stub under `ref/` or reach the API through reflection.
