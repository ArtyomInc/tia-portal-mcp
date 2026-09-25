# Acceptance Test Report — R2 hardware reads on `network_read`

Acceptance date: 2026-09-25

Result: **PASS with one documented limit** (read-only) for branch `feature/r2-hardware-read`, the
already-open TIA Portal V21 Update 2 project `test.ap21`, and the public MCP calls recorded here.
`compare_hardware` was exercised live only on TIA's refusal path (see below).

## Environment

Same host, client, and project as the [R0](2026-09-25-r0-object-read-live.md) and
[R1](2026-09-25-r1-plc-read-live.md) runs (`--read-only`, MCP stdio). The rebuilt worker triggered
TIA Portal's "Openness access" prompt, which the operator approved before this run; every rebuilt
worker binary needs that approval again.

## Automated evidence

- Both builds succeed; `dotnet test TiaMcpServer.Tests`: 3110 passed, the same 11
  environment-caused failures as `main`.
- New tests: `DeviceWalker` order, selection, and paths; device-group tree; HW identifiers with
  paging; catalog rules; payload contracts; FakeWorker end-to-end through `network_read`. The
  existing network census and field-forwarding tests were extended for the five operations.

## Live observations

| Operation | Observed |
| --- | --- |
| `list_device_groups` | Both devices (`S7-1500/ET200MP station_1`, `HMI_1`) ungrouped; no groups |
| `list_unplugged_items` | No unplugged items |
| `list_hw_identifiers` (station) | 17 HW IDs: station 32, `Rail_0` 256, CPU 48/49/50/52/135/136/42, card reader 51, display 54, OPC UA 117, PROFINET interface 1 = 64 (ports 65, 66), interface 2 = 72 (port 73) |
| `read_port_topology` | 6 ports (3 PLC, 3 HMI) with `Label`, `MediumAttachmentType`, `CableName`, `TransmissionRateAndDuplex`, … and no configured partners |
| `list_hw_identifiers` `Nope` | `target_not_found` |
| `compare_hardware` station ↔ station, station ↔ `HMI_1` | `worker_operation_failed` carrying TIA's message "Hardware comparison of … is not supported." |

## Defect found and fixed

`read_port_topology` reported the non-scalar `ConnectedPorts` attribute as unreadable on every port.
When a listing reads every declared attribute, readable non-scalar values are now skipped silently;
only failed reads are reported. Verified offline; the next live run re-checks it.

## Not covered live

- A successful hardware comparison: TIA refuses self-comparison and dissimilar devices, and the
  project has only one PLC station. The flattening is shared with `compare_software`, which was
  verified live in R1.
- Device groups, unplugged items, and configured port partners (none in the project; covered
  offline).
