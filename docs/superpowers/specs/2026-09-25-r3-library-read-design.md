# R3 — Library reads (`library_read`) design

Date: 2026-09-25 · Phase R3 of the [Openness read coverage roadmap](../../roadmap/openness-read-coverage.md)

## Problem statement

Standardized engineering relies on libraries: types with released versions, master copies, and
instances across PLCs. Today an agent cannot tell which library types exist, which version is
released or in work, what a version depends on, whether the project uses out-of-date types, or where
a type is instantiated. `object_read` can walk to a type, but versions, dependencies, the update
check, and the instance search are either associations or methods.

## Goals

1. List the project library and the global libraries already open in TIA Portal.
2. List types and master copies across folders, with metadata and object paths.
3. Read one type's versions with state, default flag, dates, dependencies, and dependents.
4. Run the library update check as a report.
5. Find the instances of a type (optionally one version) in a PLC.

## Non-goals

- Opening, closing, saving, archiving, or upgrading a global library — each changes Portal state.
  Only libraries the user already opened are visible.
- Any library mutation: `UpdateProject`, `UpdateLibrary`, `HarmonizeProject`, `CleanUpLibrary`,
  release/edit/discard of versions, instantiation, master-copy creation.
- `CompareToLibrary` (needs two libraries; deferred until a global library can be verified live).

## Requirements (P0)

- **P0.1** New MCP tool `library_read` (both access modes, read-only annotations, DomainReads
  framework, 1–50 operations): `list_libraries`, `list_library_types`, `read_library_type`,
  `list_master_copies`, `check_library_updates`, `find_type_instances`.
- **P0.2** `libraryName` selects an open global library by exact name; omitted, the project library.
  An unknown name fails `target_not_found`, lists the open global libraries, and states that
  libraries are never opened.
- **P0.3** Global-library results use the `portal` root (their object paths start at
  `GlobalLibraries`), project-library results the `project` root; the root is reported with every
  result.
- **P0.4** `read_library_type` / `find_type_instances` select one type by `name` and optional
  `folderPath` (exactly one match). Versions report `VersionNumber`, `State`, `IsDefault`, `Author`,
  `ModifiedDate`, `Guid`, `OriginalLibrary`, plus dependency and dependent type versions.
- **P0.5** `check_library_updates` calls `UpdateCheck(project, mode)` — a report, out-of-date types
  only unless `includeUpToDate` — and flattens the message tree with depths; paged.
- **P0.6** `find_type_instances` calls `LibraryTypeVersion.FindInstances(PlcSoftware)` for every
  version (or `version`) of the type in the selected PLC and returns each instance's name, kind,
  connected version, and group path.
- **P0.7** All worker methods are `Observe`; typed payload contract; `protocol_error` without echo.

## Acceptance criteria

- On `test.ap21`: `list_libraries` returns the project library (and any open global library);
  `list_library_types` returns `logo_groupe_e`, `fpValvePopup`, `fpValve`, `tlProject`, and the
  script-module folder's type; `read_library_type` of `fpValve` returns its four versions with
  states; `check_library_updates` returns a report; an unknown `libraryName` fails
  `target_not_found`.
