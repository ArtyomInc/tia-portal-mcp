# R2 — Hardware reads on `network_read` design

Date: 2026-09-25 · Phase R2 of the [Openness read coverage roadmap](../../roadmap/openness-read-coverage.md)
Builds on: [R0](2026-09-25-r0-object-read-design.md), [R1](2026-09-25-r1-plc-read-design.md)

## Problem statement

`network_read` answers "what hardware is in the project and how is it networked", but not several
questions commissioning and maintenance engineers ask daily: how devices are organized in groups,
which configured modules are not plugged, which hardware identifiers (HW IDs) the program must use,
how ports are cabled in the offline topology, and how two stations differ. Some of these are not
reachable generically at all: unplugged items are a CLR collection and port partners an
association, neither of which `object_read` can walk.

## Goals

1. List the device-group tree with the devices of each group.
2. List unplugged device items per device.
3. List every HW ID of a device and its nested device items, each with its owner path.
4. Report each port's configured partner ports (offline topology) and scalar port attributes.
5. Compare two devices offline and return their differences.

## Non-goals

- A new MCP tool: these are hardware observations and belong to `network_read`, which already owns
  the hardware model. Its tool count stays unchanged.
- Communication connections and structured I/O: already served by `list_network_objects` /
  `inspect_network_object` and `read_hardware_config` (`includeIoDetails`).
- System-diagnostics settings and CAx export: reachable with `object_read`
  (`SystemdiagnosticsSettingsDataProvider`) or file-producing (CAx), deferred.
- Online topology (`read online topology`) and any online comparison (R6).

## Requirements (P0)

- **P0.1** New `network_read` read operations: `list_device_groups`, `list_unplugged_items`
  (`deviceName?`), `list_hw_identifiers` (`deviceName`, `pageSize?`, `cursor?`),
  `read_port_topology` (`deviceName?`), `compare_hardware` (`deviceName`, `compareDeviceName`,
  `includeIdentical?`, `pageSize?`, `cursor?`). Strict catalog validation in the existing order;
  they cannot run in `network_write`.
- **P0.2** Device selection by `deviceName` is ordinal-ignore-case, like `read_hardware_config`, but
  fails closed: no match → `target_not_found`, several → `target_ambiguous`. Devices are enumerated
  ungrouped first, then depth-first through device groups (the worker's project enumeration order).
- **P0.3** Every group, device, HW identifier, and port result carries an R0 `objectPath`.
- **P0.4** `compare_hardware` flattens `HardwareObject.CompareTo` exactly like `compare_software`
  (shared flattener): `{ path, depth, leftName, rightName, state, detail }`, identical elements
  omitted unless `includeIdentical`.
- **P0.5** Each operation declares one typed result, validated in `NetworkPayloadContract`
  (`protocol_error` without echo); all worker methods are `Observe`.

## Acceptance criteria

- On `test.ap21`, `list_device_groups` lists both devices as ungrouped; `list_hw_identifiers` for
  the PLC station returns the station's and every module's HW IDs; `read_port_topology` lists the
  PLC's PROFINET ports with their partners (none configured, or the configured ones);
  `compare_hardware` of the station with itself returns no differences; an unknown device fails
  `target_not_found`.
- The existing network contract and forwarding tests pass unchanged except for the added
  operations.
