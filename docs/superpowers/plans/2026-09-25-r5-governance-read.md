# R5 `governance_read` implementation plan

Design: [2026-09-25-r5-governance-read-design.md](../specs/2026-09-25-r5-governance-read-design.md)
Branch: `feature/r5-governance-read` (stacked on R4)

## Tasks

1. **Pure builder.** `GovernanceRead/GovernanceReadBuilder.cs`: section specs for UMAC, Safety,
   TestSuite, VCI, and Multiuser; project-service and Safety-service resolution through the R0
   resolver (`capability_unavailable` when absent); section listing with scalar values and
   association names; Portal `Projects` never listed.
2. **Siemens handler.** `Openness/GovernanceReadService.cs`: `TiaPortal.GetProcesses()` with the
   attached process marked.
3. **Wiring.** `GovernanceReadInfo.cs`; 6 `Observe` entries; capability `governance-read`; worker
   dispatch.
4. **Host.** `TiaMcpServer/GovernanceRead/GovernanceReadTools.cs`; registration; census 19/9.
5. **Tests.** UMAC references, missing service, Safety on fail-safe and standard CPUs, Portal
   sessions without projects; catalog; contract; FakeWorker end-to-end.
6. **Docs and the combined R3–R5 live acceptance** (read-only, one TIA Openness approval).
