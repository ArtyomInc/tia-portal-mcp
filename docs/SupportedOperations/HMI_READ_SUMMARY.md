# HMI reads (`hmi_read`)

`hmi_read` reads WinCC Unified (`HmiSoftware`) and WinCC Basic/Comfort/Advanced (`HmiTarget`, called
"classic" here) configuration offline. It is read-only, available in both access modes, and returns
the shared `{ tool, success, batch, error }` document. Each call carries 1–50 operations.

## Selecting the HMI

`hmiName` is the exact HMI software name from `list_hmis`; omit it when the project contains exactly
one HMI. HMIs are found in ungrouped and grouped devices at any device-item depth.

## Operations

| Operation | Unified | Classic | Result |
| --- | --- | --- | --- |
| `list_hmis` | ✓ | ✓ | `hmis[]`: `name`, `runtime`, `deviceName`, `deviceGroupPath`, `objectPath` |
| `list_screens` | Screens and screen groups (`ScreenNumber`, `Width`, `Height`, `Enabled`) | Screens and folders | Listing |
| `list_screen_items` (`screen`, `groupPath?`) | Widgets with every scalar property | `capability_unavailable` (export the screen) | Listing with `target` = the screen |
| `read_screen_scripts` (`screen`, `groupPath?`) | JavaScript of screen and item events, property events, and script dynamizations | `capability_unavailable` | `scripts[]`: `owner`, `kind`, `trigger`, `scriptCode`, `globalDefinitionAreaScriptCode`, `objectPath` |
| `list_hmi_tags` | Tags with `TagTableName`, `DataType`, `Connection`, `PlcName`, `PlcTag`, `Address`, `AccessMode`, … | Tags grouped by tag table | Listing |
| `list_hmi_connections` | ✓ | ✓ | Listing |
| `list_hmi_alarms` | Discrete and analog alarms (`Id`, `AlarmClass`, `Priority`, trigger tags, …) | `capability_unavailable` | Listing |
| `list_hmi_logs` | Data and alarm logs | `capability_unavailable` | Listing |
| `list_hmi_text_lists` | Text and graphic lists | Text and graphic lists | Listing |
| `list_hmi_scripts` | Script modules | VB scripts and folders | Listing |

Listings return `{ hmiName, runtime, items[], totalCount, offset, nextCursor, diagnostics }` with the
same item shape as `plc_read` (`name`, `kind`, `typeName`, `groupPath`, `objectPath`, `values`,
`unavailable`), accept `nameContains`, and are paged with `pageSize`/`cursor`.

## Boundaries

- Nothing is written. Script modules' own code and Classic screens are read with `object_read`
  `export_object` on the returned object paths.
- Runtime settings, audit trails, and OPC UA alarm types are reachable with `object_read`.
