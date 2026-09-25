# R3 `library_read` implementation plan

Design: [2026-09-25-r3-library-read-design.md](../specs/2026-09-25-r3-library-read-design.md)
Branch: `feature/r3-library-read` (stacked on R2)

## Tasks

1. **Core additions.** `IObjectNode.ReadObjectList` (associations such as `Dependencies`),
   `FileSystemInfo` values normalized to full paths.
2. **Pure builder.** `LibraryRead/LibraryReadBuilder.cs`: library discovery (project library, open
   global libraries through the Portal root), exact library selection, type and master-copy
   listings (`GroupTreeLister`), type versions with dependency references.
3. **Siemens handlers.** `Openness/LibraryReadService.cs`: `UpdateCheck` (report mode) flattening and
   `FindInstances` scoped to one `PlcSoftware`.
4. **Contracts and wiring.** `LibraryReadInfo.cs`; `WorkerRequest.LibraryName`,
   `LibraryIncludeUpToDate`, `LibraryVersion`; 6 `Observe` entries; capability `library-read`;
   worker dispatch with the Portal.
5. **Host.** `TiaMcpServer/LibraryRead/` request, catalog, invoker, contract, and tool; registration
   in both modes; census 17/7.
6. **Tests.** Builder on an in-memory project + Portal (roots, folders, versions, dependencies,
   ambiguity, missing Portal); catalog; contract; FakeWorker end-to-end.
7. **Docs and live acceptance** (combined read-only run with R4 and R5, one TIA Openness approval).
