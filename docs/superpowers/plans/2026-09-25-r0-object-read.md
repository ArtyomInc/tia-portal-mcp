# R0 `object_read` implementation plan

Design: [2026-09-25-r0-object-read-design.md](../specs/2026-09-25-r0-object-read-design.md)
Branch: `feature/r0-object-read`

## Global constraints

- Read-only. The generic walker never calls `Invoke`, `Create`, `SetAttribute`, `Delete`,
  `Import`, or any online service. `Export` is called only by `export_object`, into a temporary
  directory removed in `finally`.
- Reuse the canonical JSON seam (`StructuredToolResult`, `StructuredOperationBatch`,
  `StructuredOperationBatchPayloadBudget`, `CanonicalJson`). No second rendering path.
- Siemens-free logic (path resolution, child ordering, cursors, export windows) lives in files that
  `TiaMcpServer.Tests` links, and is tested against an in-memory object graph.
- A compile, stub build, FakeWorker run, or contract test is not evidence of live TIA behavior.
  Live evidence comes only from the read-only acceptance run in Task 8.

## Shared domain-read framework (reused by R1–R5)

R0 introduces the host pieces every later read tool uses, so R1–R5 add catalog entries, worker
readers, and result DTOs only:

| File | Role |
| --- | --- |
| `TiaMcpServer/DomainReads/DomainReadCatalog.cs` | Spec-driven batch validation: count, `operationId` identity/length/uniqueness, known operation, inapplicable → required → operation-specific checks; access-mode check |
| `TiaMcpServer/DomainReads/DomainPayloadProjector.cs` | Worker outcome → `StructuredOperationItem`; typed decode through `CanonicalJson.Normalize<T>`; `protocol_error` without echo |
| `TiaMcpServer/DomainReads/DomainReadToolRunner.cs` | validate → access → execute → budget → `StructuredToolResult` |
| `TiaMcpServer/DomainReads/DomainReadToolRunner.cs` (`DomainReadResponse`) | Output schema `{ tool, success, batch, error }` |
| `TiaMcpServer/Worker/OpennessWorkerClient.cs` `ReadDomainAsync` | One generic bound read call: method + request configurator |

## Tasks

1. **Contracts.** `ObjectPathSegmentInfo`, `ObjectDescriptionInfo`, `ObjectChildrenPageInfo`,
   `ObjectAttributesInfo`, `ObjectExportInfo`, `OpennessCapabilitiesInfo`; `WorkerRequest` object
   fields; `capability_unavailable` failure category; policy entries (4 × Observe,
   `export_object` TemporaryExport); protocol capability `generic-object-read`.
2. **Worker pure core.** `ObjectModel/IObjectNode.cs`, `ObjectPathResolver.cs`,
   `ObjectChildrenPager.cs` (ordering, snapshot hash, cursor codec, 10,000 cap),
   `ObjectExportWindow.cs` (window + SHA-256), `ObjectPathRules.cs` (denied services, portal
   `Projects`, limits). Linked into the test project.
3. **Worker Siemens adapter and handlers.** `Openness/EngineeringObjectNode.cs`
   (`IEngineeringObject` → `IObjectNode`, reflection `GetService<T>` over `GetServiceInfos()`),
   `Openness/ObjectReadService.cs` (describe, children, attributes via
   `EngineeringAttributeInspector` + `NetworkAttributeResultBuilder`, export, capabilities),
   dispatch in `Program.cs`.
4. **Host framework** (table above) and **object_read tool**: `ObjectRead/ObjectReadOperationRequest.cs`,
   `ObjectReadCatalog.cs`, `ObjectReadWorkerInvoker.cs`, `ObjectReadPayloadContract.cs`,
   `ObjectReadTools.cs`; register in `Program.cs` for both modes.
5. **FakeWorker.** Scripted responses for the five methods so IPC tests cover the full path.
6. **Tests** (see test strategy in the acceptance report): catalog validation matrix, payload
   contract rejection, path resolver rules on an in-memory graph, pager/cursor, export window,
   policy classification, read-only registration, FakeWorker end-to-end.
7. **Docs.** `ARCHITECTURE.md`, `README.md`, `OBJECT_READ_SUMMARY.md`, `SupportedOperations/README.md`,
   `docs/README.md`, roadmap status.
8. **Live read-only acceptance** against `test.ap21` through the built host over MCP stdio in
   `--read-only` mode; record `docs/superpowers/acceptance/reports/2026-09-25-r0-object-read-live.md`.
