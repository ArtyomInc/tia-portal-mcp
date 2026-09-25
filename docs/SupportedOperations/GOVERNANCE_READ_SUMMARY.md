# Governance reads (`governance_read`)

`governance_read` reads governance, security, and collaboration state. It is read-only, available in
both access modes, never reads or asks for a password, and returns the shared
`{ tool, success, batch, error }` document. Each call carries 1–50 operations.

## Operations

| Operation | Fields | Result |
| --- | --- | --- |
| `list_portal_processes` | — | `processes[]`: `id`, `mode`, `projectPath`, `acquisitionTime`, `isAttached` (no process is attached by this call) |
| `read_umac` | — | Sections `projectUsers`, `customRoles`, `systemRoles`, `umcUsers`, `umcUserGroups`, `engineeringFunctionRights`, `customDeviceFunctionRights`; users and groups reference their `Roles`, roles their `AssignedEngineeringRights` |
| `read_safety` | `plcName?` | Scope values `IsLoggedOnToSafetyOfflineProgram`, `IsSafetyOfflineProgramPasswordSet`, `Settings.*`; sections `programSignatures`, `runtimeGroups` |
| `list_test_suite` | — | Sections `testCases`, `applicationTestSets`, `styleGuideRuleSets`, `systemTestCases` (nothing is executed) |
| `list_multiuser` | — | Portal sections `projectServers` (`ServerName`, `Host`, `Port`) and `localSessions` |
| `list_vci_workspaces` | — | Sections `workspaces` (`RootPath`, `GlobalLibraryPath`, …) and `workspaceGroups` |

Sectioned results are `{ scope, root, values, sections[{ name, items[] }], diagnostics }`. An item is
`{ name, kind, objectPath, values, unavailable, references }`; `objectPath` starts at `root`
(`project`, or `portal` for `list_multiuser`) and is accepted by `object_read`.

A product the project does not use — TestSuite, version control, UMAC — or a standard CPU for
`read_safety` fails `capability_unavailable` with the reason.

## Boundaries

- No login, password, user, role, or certificate change; no test execution; no Safety validation
  report; no Multiuser or VCI action.
- Other open projects are never read: local sessions report their own attributes only.
- Portal settings: `object_read` with `root: "portal"` and the `SettingsFolders` composition.
