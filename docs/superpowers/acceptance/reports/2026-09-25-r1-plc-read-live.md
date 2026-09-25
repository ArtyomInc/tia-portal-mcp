# Acceptance Test Report — R1 PLC read domain (`plc_read`)

Acceptance date: 2026-09-25

Result: **PASS** (read-only) for branch `feature/r1-plc-read`, the already-open TIA Portal V21
Update 2 project `test.ap21`, and the public MCP calls recorded here. The host ran with
`--read-only`; no write tool exists in that mode.

## Environment

Same host, client, and project as the
[R0 acceptance](2026-09-25-r0-object-read-live.md): `TiaMcpServer.exe --read-only --project
<test.ap21>` built from the branch working tree, driven over MCP stdio. The project contains one
S7-1500 PLC (`PLC_1`, CPU 1515-2 PN) in station `S7-1500/ET200MP station_1`.

## Automated evidence

- Both builds (real V21 assemblies and reference stubs) succeed.
- `dotnet test TiaMcpServer.Tests`: 3092 passed; the same 11 environment-caused failures as `main`
  (missing `pwsh`, fixture line endings).
- New R1 tests: PLC locator (grouped devices, exact and ambiguous selection), scalar normalizer,
  group-tree listings with system groups, name filter and paging, table entries, TO parameters,
  checksums, catalog matrix, payload contract, and FakeWorker end-to-end through the MCP SDK.

## Live observations

| Operation | Observed |
| --- | --- |
| `list_plcs` | `PLC_1` on `S7-1500/ET200MP station_1`, 4-step `objectPath` |
| `list_blocks` (no `plcName`) | `Main` (OB 1, LAD, `ProgramCycle`), `fbValve` (FB 1, SCL), `DiFbValve` (InstanceDB, `InstanceOfName: fbValve`), `DbHmi` (GlobalDB 3); ISO-8601 dates, `MemoryLayout: Optimized`, sizes, consistency and protection flags; `unavailable` empty |
| `list_blocks` paging | `pageSize: 3` returned 3 blocks with a cursor |
| `objectPath` drill-down | `object_read` on the `DiFbValve` path returned `Name` and `InstanceOfName: fbValve` |
| `list_types` | `iHmiValve`, `iHmiValveSp`, `iHmiValvePv` (`PlcStruct`, library-conformant) |
| `list_watch_tables` / `read_watch_table` | `Force table` (`PlcForceTable`), 0 entries |
| `read_block_fingerprints` `Main` | `Code`, `Comments`, `Events`, `Interface`, `ProgramCode`, `Properties` values |
| `read_checksums` | `software: E2 7F 6E 8B 53 9F A7 78`, `textLists: FA 70 E8 75 1D 5A 8E 29` (identical to the R0 generic read of `PlcChecksumProvider`) |
| `compare_software` `PLC_1` ↔ `PLC_1` | 0 differences; with `includeIdentical: true`, 14 elements over two pages (page 2 at offset 10, 4 elements, no further cursor) |
| `list_technology_objects`, `list_external_sources`, `list_software_units`, `list_alarm_text_lists`, `list_opcua_server_interfaces` | Succeeded with 0 items (the project has none) |
| `plcName: PLC_9` | `target_not_found`, message lists `'PLC_1'` |
| `read_watch_table` `Nope` | `target_not_found` |

## Not covered live

- Non-empty technology objects, external sources, Software Units, alarm text lists, and OPC UA
  interfaces (none in the project; covered offline on in-memory fixtures).
- A comparison with real differences (only one PLC; the self-comparison proves traversal and
  paging).
- Projects with several PLCs (selection rules covered offline).
