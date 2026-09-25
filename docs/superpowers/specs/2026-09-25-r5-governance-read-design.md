# R5 — Governance reads (`governance_read`) design

Date: 2026-09-25 · Phase R5 of the [Openness read coverage roadmap](../../roadmap/openness-read-coverage.md)

## Problem statement

Audits and handovers ask questions that span the whole engineering environment rather than one
program: which TIA Portal instances are running, who may do what in the project (UMAC), what the
Safety program signature is, which tests exist, whether the project is shared through Multiuser or
version control. None of this is readable today, and much of it sits behind optional products.

## Goals

1. List running TIA Portal processes and their open projects without attaching to them.
2. Read project users, roles with their engineering rights, UMC users and groups, and function rights.
3. Read a fail-safe PLC's Safety administration: login and password-set flags, settings, program
   signatures, runtime groups.
4. Inventory TestSuite application tests, test sets, style-guide rule sets, and system tests.
5. List Multiuser project servers and local sessions, and version-control workspaces.

## Non-goals

- Anything that needs a password or changes security state: Safety login, UMAC user creation or
  role assignment, certificate changes.
- Running tests (`TestCaseExecutor`, `RuleSetExecutor`) or generating a Safety validation report —
  both have effects and produce files.
- Multiuser session actions (open, refresh, mark, commit) and VCI export/import.
- Portal settings and project-text export: settings are reachable with `object_read` (`root:
  portal`, `SettingsFolders`); project-text export writes a spreadsheet file.

## Requirements (P0)

- **P0.1** New MCP tool `governance_read` (both modes, read-only annotations, DomainReads framework):
  `list_portal_processes`, `read_umac`, `read_safety` (`plcName?`), `list_test_suite`,
  `list_multiuser`, `list_vci_workspaces`.
- **P0.2** Sectioned results `{ scope, root, values, sections[{ name, items[] }], diagnostics }`; an
  item carries `name`, `kind`, `objectPath`, every scalar `values`, `unavailable`, and `references`
  (names of associated objects: a user's roles, a role's engineering rights).
- **P0.3** A service the project does not offer (TestSuite, VCI, UMAC not configured) or a
  standard CPU for `read_safety` fails `capability_unavailable`.
- **P0.4** `list_multiuser` reads the Portal; it never lists or enters another project (sessions
  report their scalar attributes only).
- **P0.5** `list_portal_processes` uses `TiaPortal.GetProcesses()` and marks the process this server
  is attached to; it does not attach to any other process.
- **P0.6** All worker methods are `Observe`; typed payload contract.

## Acceptance criteria

- On the live Portal: `list_portal_processes` lists both running instances and marks the attached
  one; `read_umac`, `list_test_suite`, `list_vci_workspaces` succeed or fail
  `capability_unavailable` with a reason; `read_safety` on the standard CPU 1515-2 PN fails
  `capability_unavailable`; `list_multiuser` succeeds.
