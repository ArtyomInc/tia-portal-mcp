# Acceptance Test Report — R0 generic object read (`object_read`)

Acceptance date: 2026-09-25

Result: **PASS** (read-only) for branch `feature/r0-object-read`, the already-open TIA Portal V21
Update 2 project `test.ap21`, and the public MCP calls recorded here. No write tool was called and
the host ran with `--read-only`, so the project could not be modified.

## Environment

- Host: built from the branch working tree, `TiaMcpServer.exe --read-only --project <test.ap21>`,
  driven by a minimal MCP stdio client (`initialize`, `tools/list`, `tools/call`).
- Two TIA Portal V21 processes were running (`test.ap21` and an unrelated project); the explicit
  project path selected `test.ap21`, and no other project was touched.
- Installed products reported by `list_capabilities`: STEP 7 Professional V21 Update 2 (options
  STEP 7 Safety V21, Tech WebApplication V21), TIA Portal V21 Update 2 (Test Suite Advanced V21),
  WinCC Basic/Comfort/Advanced V21 Update 2, WinCC Unified V21 Update 2.

## Automated evidence

- `dotnet build TiaMcpServer.sln -m:1` against the real V21 assemblies and against the reference
  stubs: both succeed.
- `dotnet test TiaMcpServer.Tests`: 3054 passed; 11 failures, identical to `main` before this
  branch, caused by the environment (missing `pwsh` for script tests, fixture line endings).
- 90 R0 tests: path resolver, children pager and cursor, export windows, description builder,
  catalog matrix, payload contract, and FakeWorker end-to-end through the real MCP SDK.

## Live observations

| Check | Observed |
| --- | --- |
| `tools/list` in read-only mode | 5 tools, `object_read` annotated `readOnlyHint: true`, `destructiveHint: false` |
| `list_capabilities` | 4 products; WinCC, WinCC Unified, Safety, SafetyValidation, TestSuite, Teamcenter assemblies available at 21.0.0.0; `Siemens.Engineering.MC.Drives` not installed |
| `list_object_children` on the project root | 24 children (`Devices`, `HistoryEntries`, `HwUtilities`, `Subnets`, `TextCategories`, …), public type names such as `Siemens.Engineering.HW.Device` |
| Paging (`pageSize: 5`) | Page 2 from `nextCursor` started at offset 5; the same cursor with `compositionNames` added failed `cursor_filter_mismatch` |
| Device → `PLC_1` → `SoftwareContainer` → `Software` | `Siemens.Engineering.SW.PlcSoftware` with `BlockGroup`, `ExternalSourceGroup`, `PlcAlarmTextlistGroup`, `TagTableGroup`, `TechnologicalObjectGroup`, `TypeGroup`, `WatchAndForceTableGroup` |
| `read_object_attributes` on the CPU's `SoftwareContainer` | 68 entries, 61 available typed values (order number `6ES7 515-2AN03-0AB0`, firmware `V4.1`, cycle times, enums with symbol and numeric value); `MultilingualText`, `DateTime`, `string[]`, and object values reported `unrepresentable`; an unknown name reported `unknownAttribute` |
| WinCC Unified | `HMI_1` → `HMI_RT_1` → `SoftwareContainer` → `Software` resolved to `Siemens.Engineering.HmiUnified.HmiSoftware`; `Screens` listed `sMain` (`HmiScreen`) |
| `export_object` on the default tag table | 235 characters without `DocumentInfo`; four independent 60-character window exports shared one SHA-256 and reassembled byte-identically; `withDefaults` + `withReadOnly` accepted |
| `export_object` on a device | `target_kind_unsupported` |
| Index 0 with `elementName: PLC_1` | `target_evidence_mismatch` |
| `OnlineProvider` service step | `access_denied`; `describe_object` lists `OnlineProvider`, `DownloadProvider`, `ParameterUploadProvider` as `allowed: false` |
| `Parent` step | `access_denied` |
| Portal root | `describe_object` lists `GlobalLibraries`, `HardwareCatalog`, `LocalSessions`, `ProjectServers`, `SettingsFolders` (no `Projects`); `compositionNames: ["Projects"]` fails `access_denied` |

## Defects found and fixed during the run

1. Optional assemblies were all reported unavailable: the worker's exact-version assembly resolver
   rejects a simple-name `Assembly.Load`. `list_capabilities` now reads assembly metadata from the
   Openness directory without loading anything.
2. Type names were internal implementation classes (`DeviceImpl`). The worker now reports the
   nearest public API type.
3. `SoftwareContainer.Software` is a CLR property, not a dynamic attribute, so the PLC software was
   unreachable. Public CLR properties returning engineering objects are now read-only attribute
   steps (reported `source: modeled` by `read_object_attributes`).
4. That change exposed `Parent`, which could climb from the project to the Portal and its other
   projects. `Parent` is excluded, the Portal's `Projects` is hidden on every Portal object, and a
   step landing on any project object is denied.
5. Every export carried a fresh `DocumentInfo/Created` timestamp, so window digests never matched.
   `DocumentInfo` is now removed, as `get_block_content` already did.

## Not covered

- Objects larger than one 30,000-character window in the live project (window chaining was
  verified with small windows instead).
- `root: portal` navigation into global libraries (none open) and Multiuser sessions (none).
- Online reads (phase R6) — refused by design.
