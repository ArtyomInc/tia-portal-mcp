# R2 hardware reads implementation plan

Design: [2026-09-25-r2-hardware-read-design.md](../specs/2026-09-25-r2-hardware-read-design.md)
Branch: `feature/r2-hardware-read` (stacked on R1)

## Tasks

1. **Shared walkers.** `ObjectModel/DeviceWalker.cs` (devices, device groups, device items, exact
   device selection) — `PlcLocator` now uses it. `Openness/CompareResultFlattener.cs` shared by
   `compare_software` and `compare_hardware`; `PlcCompareElementInfo` renamed `CompareElementInfo`.
2. **Pure builders.** `HardwareRead/HardwareReadBuilder.cs`: device-group tree and HW identifiers
   over `IObjectNode`.
3. **Siemens handlers.** `Openness/HardwareReadService.cs`: `Device.UnpluggedItems`,
   `NetworkPort.ConnectedPorts` with partner owner chains, `Device.CompareTo`.
4. **Contracts and wiring.** `HardwareReadInfo.cs`, `WorkerRequest.CompareDeviceName`, 5 `Observe`
   policy entries, protocol capability `hardware-read`, worker dispatch.
5. **Host.** `NetworkOperationRequest` (`compareDeviceName`, `includeIdentical`), catalog specs and
   `ValidateHardwareRead`, invoker through `ReadDomainAsync`, `HardwareReadContract` validators in
   `NetworkPayloadContract`, tool description and retry guidance.
6. **Tests.** Walker order/selection, group tree, HW IDs and paging on in-memory graphs; catalog and
   payload contract; FakeWorker end-to-end through `network_read`; updated network census and
   forwarding tests (generic paging fields for domain reads).
7. **Docs and live acceptance** (read-only, `test.ap21`).
