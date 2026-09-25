# Library reads (`library_read`)

`library_read` reads the project library and the global libraries already open in TIA Portal. It is
read-only and available in both access modes; libraries are never opened, updated, or modified.
Each call carries 1–50 operations `{ operationId, operation, projectPath?, libraryName?, … }`, and
returns the shared `{ tool, success, batch, error }` document.

## Selecting a library

Omit `libraryName` for the project library. Otherwise give the exact name of a global library that
is already open (see `list_libraries`); an unknown name fails `target_not_found` and lists the open
ones. Every result reports `libraryKind` (`project` or `global`) and `root`: object paths of a
global library start at the Portal's `GlobalLibraries`, so pass `root: "portal"` to `object_read`.

## Operations

| Operation | Fields | Result |
| --- | --- | --- |
| `list_libraries` | — | `libraries[]`: `name`, `kind`, `root`, `objectPath`, `values` (global libraries: `Author`, `Version`, `IsReadOnly`, `IsWriteProtected`, `IsModified`, `LastModified`, `Path`, …) |
| `list_library_types` | `libraryName?`, `pageSize?`, `cursor?` | Types across folders: `name`, `kind`, `groupPath`, `objectPath`, `values` (`Author`, `Guid`, `Status`, `DoNotUse`, `SetForUpdate`, `Namespace`, `MinimumTargetDeviceVersion`) |
| `read_library_type` | `name`, `folderPath?`, `libraryName?` | The type plus `versions[]`: `values` (`VersionNumber`, `State`, `IsDefault`, `Author`, `ModifiedDate`, `Guid`, `OriginalLibrary`), `dependencies[]`, `dependents[]` (`typeName`, `versionNumber`), `objectPath` |
| `list_master_copies` | `libraryName?`, paging | Master copies across folders with every scalar attribute |
| `check_library_updates` | `libraryName?`, `includeUpToDate?`, paging | The library update-check report, flattened: `messages[]` (`depth`, `description`) |
| `find_type_instances` | `name`, `folderPath?`, `version?`, `plcName?`, `libraryName?` | Instances of the type (all versions, or `version`) in one PLC: `name`, `kind`, `versionNumber`, `groupPath` |

`read_library_type` and `find_type_instances` select one type by exact `name`; add `folderPath`
(`[]` for the root folder) when several types share the name. `find_type_instances` searches one
PLC (`plcName`, optional when the project has exactly one), the only instance scope Openness V21
offers.

## Boundaries

- Opening or managing global libraries, releasing or editing versions, and updating the project
  from a library are not offered.
- `CompareToLibrary` is not offered yet.
