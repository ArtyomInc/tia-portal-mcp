# R4 — HMI reads (`hmi_read`) design

Date: 2026-09-25 · Phase R4 of the [Openness read coverage roadmap](../../roadmap/openness-read-coverage.md)

## Problem statement

HMI configuration is the largest Openness surface (≈560 WinCC Unified types) and currently has no
dedicated read. An engineer reviewing an HMI wants its screens, the tags and their PLC bindings,
connections, alarms, logs, text lists, and the JavaScript behind events and dynamizations —
without walking hundreds of generic objects one call at a time.

## Goals

1. Find every HMI of the project with its runtime (WinCC Unified or Classic).
2. List screens (with groups), tags, connections, text/graphic lists, and scripts for both runtimes,
   and alarms and logs for Unified.
3. List a Unified screen's items with their scalar properties.
4. Return the JavaScript of a Unified screen's events, item events, and script dynamizations.

## Non-goals

- Any HMI mutation (screens, tags, alarms, scripts) — a write roadmap item.
- WinCC Classic screen items and alarms: not exposed by Openness V21 (Classic screens are exported
  with `object_read` `export_object`). These return `capability_unavailable`, never an empty list.
- Runtime settings, audit, and OPC UA alarm types as dedicated operations: reachable with
  `object_read`.

## Requirements (P0)

- **P0.1** New MCP tool `hmi_read` (both modes, read-only annotations, DomainReads framework):
  `list_hmis`, `list_screens`, `list_screen_items`, `read_screen_scripts`, `list_hmi_tags`,
  `list_hmi_connections`, `list_hmi_alarms`, `list_hmi_logs`, `list_hmi_text_lists`,
  `list_hmi_scripts`.
- **P0.2** HMIs are found at any device-item depth in ungrouped and grouped devices (shared
  `DeviceWalker`); `hmiName` selects exactly one (ordinal), optional when the project has one HMI.
- **P0.3** Listings reuse `GroupTreeLister` per runtime: Unified screens under `Screens` and
  `ScreenGroups`/`Groups`; Classic screens under `ScreenFolder`/`Folders`; Classic tags grouped by
  tag table. Every item carries `groupPath` and an R0 `objectPath`; `nameContains` and paging apply.
- **P0.4** `read_screen_scripts` walks `EventHandlers`, `PropertyEventHandlers`, and `Dynamizations`
  of the screen and of every screen item, and returns `{ owner, kind, trigger, scriptCode,
  globalDefinitionAreaScriptCode, objectPath }`; non-script dynamizations are skipped; paged.
- **P0.5** A listing a runtime does not expose fails `capability_unavailable` with a pointer to the
  alternative.
- **P0.6** All worker methods are `Observe`; typed payload contract.

## Acceptance criteria

- On `test.ap21`: `list_hmis` returns `HMI_RT_1` (Unified) on `HMI_1`; `list_screens` returns `sMain`;
  `list_screen_items` and `read_screen_scripts` for `sMain` succeed; `list_hmi_tags`,
  `list_hmi_connections`, `list_hmi_alarms`, `list_hmi_logs`, `list_hmi_text_lists`, and
  `list_hmi_scripts` succeed. Classic behavior is covered offline (no Classic device in the project).
