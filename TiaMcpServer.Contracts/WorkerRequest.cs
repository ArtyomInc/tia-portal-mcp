using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Flat request envelope for one host→worker call, serialized as newline-delimited JSON.
///
/// The shape is deliberately flat rather than one DTO per operation: the protocol is stable
/// and per-operation types would cost more churn than they save. See "Deferred / explicitly
/// not planned" in docs/IMPROVEMENT_LOG.md.
///
/// <para>
/// Only the fields relevant to <see cref="Method"/> are read; everything else is ignored.
/// Regions below group fields by the operation family that reads them, and each field
/// documents the exact operations that forward it. That list is the contract — a field not
/// named for an operation is silently dropped for that operation.
/// </para>
/// </summary>
public class WorkerRequest
{
    #region Common — dispatch and write-confirmation flags

    /// <summary>Operation name, dispatched by the worker's switch in Program.cs.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Exact wire protocol version. The worker checks it before authorization, Attach(), or any
    /// Siemens API call. The hello request uses the same value.
    /// </summary>
    public string? ProtocolVersion { get; set; }

    /// <summary>Target project path. Resolved against the session binding before sending.</summary>
    public string? ProjectPath { get; set; }

    /// <summary>
    /// Exact worker/Portal/project identity the host has verified for this session. The worker
    /// validates it before a bound operation, so a restarted worker or changed project fails
    /// before any Siemens mutation. Null is allowed only for unbound reads and the explicit
    /// open/create operations that establish a new binding.
    /// </summary>
    public WorkerSessionIdentity? ExpectedSessionIdentity { get; set; }

    /// <summary>
    /// Set by every write operation EXCEPT update_block_logic, which forwards only
    /// AllowTiaConfirmations. Never set by reads.
    /// </summary>
    public bool Confirm { get; set; }

    /// <summary>Set by every write operation, including update_block_logic. Never set by reads.</summary>
    public bool AllowTiaConfirmations { get; set; }

    #endregion

    #region Block operations

    /// <summary>
    /// Forwarded by: get_block_content, update_block_logic, compile_check (optional, scopes
    /// the compile to one block), create_block, delete_block, create_block_group,
    /// delete_block_group.
    /// </summary>
    public string? BlockPath { get; set; }

    /// <summary>Forwarded by: update_block_logic.</summary>
    public string? YamlContent { get; set; }

    /// <summary>Forwarded by: create_block. Valid values: FB, FC, OB, GlobalDB.</summary>
    public string? BlockType { get; set; }

    /// <summary>
    /// Forwarded by: create_block. Passed through as-is including null — the worker applies
    /// the LAD default, not the host.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Forwarded by: create_block. Passed through as-is including null — the worker applies
    /// the ProgramCycle default, not the host.
    /// </summary>
    public string? OBEventClass { get; set; }

    /// <summary>Forwarded by: get_type_content, update_type_content.</summary>
    public string? TypePath { get; set; }

    /// <summary>Forwarded by: update_type_content.</summary>
    public string? SourceContent { get; set; }

    /// <summary>
    /// Forwarded by: get_type_content, update_type_content, get_block_content,
    /// update_block_logic. Normalized by SourceFormatNames on the host before sending, so the
    /// worker never sees an unrecognized value.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Forwarded by: get_block_content, get_type_content. Selects GenerateOptions.WithDependencies
    /// over GenerateOptions.None on the export. Never forwarded by a write: the safety token binds
    /// to the single-object form of the object being written.
    /// </summary>
    public bool? WithDependencies { get; set; }

    #endregion

    #region PLC scoping, tag tables, tags, and user constants

    /// <summary>
    /// Forwarded by: read_cross_references, compile_check, list_tag_tables, start_plc,
    /// stop_plc, read_hardware_config (optional — selects the PLC used for tag matching),
    /// and every tag-table, tag, and user-constant operation.
    /// </summary>
    public string? PlcName { get; set; }

    /// <summary>Forwarded by: every tag-table, tag, and user-constant operation.</summary>
    public string? TableName { get; set; }

    /// <summary>Forwarded by: every tag-table, tag, and user-constant operation.</summary>
    public string? FolderPath { get; set; }

    /// <summary>
    /// Forwarded by: create_tag, update_tag, delete_tag, create_user_constant,
    /// update_user_constant, delete_user_constant.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Forwarded by: update_tag ONLY. Not forwarded by update_user_constant, which has no
    /// rename path despite exposing a similar shape.
    /// </summary>
    public string? NewName { get; set; }

    /// <summary>
    /// Forwarded by: create_tag, update_tag, create_user_constant, update_user_constant.
    /// </summary>
    public string? DataType { get; set; }

    /// <summary>Forwarded by: create_tag, update_tag.</summary>
    public string? LogicalAddress { get; set; }

    /// <summary>Forwarded by: create_user_constant, update_user_constant.</summary>
    public string? Value { get; set; }

    /// <summary>Forwarded by: update_tag ONLY. create_tag does not forward it.</summary>
    public bool? ExternalAccessible { get; set; }

    /// <summary>Forwarded by: update_tag ONLY. create_tag does not forward it.</summary>
    public bool? ExternalVisible { get; set; }

    /// <summary>Forwarded by: update_tag ONLY. create_tag does not forward it.</summary>
    public bool? ExternalWritable { get; set; }

    /// <summary>Forwarded by: update_tag ONLY. create_tag does not forward it.</summary>
    public bool? IsSafety { get; set; }

    #endregion

    #region Project tree, catalog, and cross-references

    /// <summary>Forwarded by: browse_project_tree.</summary>
    public int? Depth { get; set; }

    /// <summary>Forwarded by: browse_project_tree.</summary>
    public string? StartPath { get; set; }

    /// <summary>
    /// Forwarded by: browse_project_tree_v3_snapshot. The legacy <see cref="StartPath"/>
    /// remains available for browse_project_tree until the v3 public cutover.
    /// </summary>
    public List<ProjectTreeSelectorSegment>? StartSelector { get; set; }

    /// <summary>Forwarded by: search_equipment_catalog.</summary>
    public string? Query { get; set; }

    /// <summary>Forwarded by: search_equipment_catalog, read_cross_references.</summary>
    public int? MaxResults { get; set; }

    /// <summary>
    /// Forwarded by: read_cross_references. Populated from the batch item's `filter` field —
    /// the names differ — after CrossReferenceFilterNames.TryNormalize validates it. That
    /// validation runs BEFORE the session binds so an invalid filter cannot bind the session.
    /// </summary>
    public string? CrossReferenceFilter { get; set; }

    #endregion

    #region Network devices

    /// <summary>Forwarded by: add_network_device.</summary>
    public string? TypeIdentifier { get; set; }

    /// <summary>Forwarded by: add_network_device, configure_network_device, read_hardware_config (optional device filter).</summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// Forwarded by: configure_network_device. Identifies exactly one node on the named device,
    /// because a device may expose several interfaces and nodes.
    /// </summary>
    public string? NodeId { get; set; }

    /// <summary>
    /// Forwarded by: add_network_device ONLY. configure_network_device does not forward it —
    /// setting it on that operation is silently dropped. The fallback to DeviceName when the
    /// caller omits it is applied by BatchWorkerInvoker.ResolveDeviceItemName before the call.
    /// </summary>
    public string? DeviceItemName { get; set; }

    /// <summary>Forwarded by: configure_network_device.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Forwarded by: configure_network_device.</summary>
    public string? SubnetMask { get; set; }

    /// <summary>Forwarded by: configure_network_device.</summary>
    public string? PnDeviceName { get; set; }

    /// <summary>
    /// Forwarded by: configure_network_device (the subnet to connect the node to), update_subnet
    /// and delete_subnet (the exact existing subnet targeted by the operation). create_subnet never
    /// forwards it — a new subnet's id is assigned by Openness, not supplied by the caller.
    /// </summary>
    public string? SubnetId { get; set; }

    /// <summary>
    /// Forwarded by: configure_network_device. The subnet that owns the requested IO system. Kept
    /// separate from <see cref="SubnetId"/> so an IO-system change can be requested without also
    /// requesting a subnet connection change.
    /// </summary>
    public string? IoSystemSubnetId { get; set; }

    /// <summary>Forwarded by: configure_network_device. The IO system's number within its subnet.</summary>
    public int? IoSystemNumber { get; set; }

    #endregion

    #region I/O map extraction (read_hardware_config)

    /// <summary>
    /// Forwarded by: read_hardware_config ONLY. When true, each device item carries structured
    /// <c>ioDetails</c> (addresses and channels). Never set by the internal network-write
    /// state snapshot, which stays lightweight and hash-identical.
    /// </summary>
    public bool IncludeIoDetails { get; set; }

    /// <summary>
    /// Forwarded by: read_hardware_config ONLY. When true, each channel carries deterministic
    /// <c>tagMatches</c> against the PLC selected by <see cref="PlcName"/> (or the single PLC
    /// when <see cref="PlcName"/> is omitted). Only meaningful together with
    /// <see cref="IncludeIoDetails"/>.
    /// </summary>
    public bool IncludeTagMatches { get; set; }

    /// <summary>Forwarded by: paged read_hardware_config only. Distinct from network object paging.</summary>
    public int? HardwarePageSize { get; set; }

    /// <summary>Forwarded by: paged read_hardware_config only. Opaque host-validated continuation evidence.</summary>
    public HardwarePageContinuationInfo? HardwarePageContinuation { get; set; }

    #endregion

    #region Network object discovery and inspection (Phase 3)

    /// <summary>
    /// Forwarded by: list_network_objects. Contains only valid kind strings (validated by the host
    /// catalog before sending). Prefixed to avoid collision with the existing flat DeviceName field.
    /// </summary>
    public List<string>? NetworkObjectKinds { get; set; }

    /// <summary>
    /// Forwarded by: list_network_objects (optional device filter). Prefixed to avoid collision
    /// with the existing flat DeviceName field used by add_network_device / configure_network_device.
    /// </summary>
    public string? NetworkObjectDeviceName { get; set; }

    /// <summary>Forwarded by: list_network_objects. Validated to the range [1, 200] by the host.</summary>
    public int? NetworkObjectPageSize { get; set; }

    /// <summary>Forwarded by: list_network_objects. Opaque cursor from a previous paged response.</summary>
    public string? NetworkObjectCursor { get; set; }

    /// <summary>
    /// Forwarded by: inspect_network_object, probe_network_object_attributes. Mapped from the
    /// host's <c>NetworkObjectTarget</c>
    /// to a fresh <see cref="NetworkObjectSelectorInfo"/>; item-path segments are deep-copied so
    /// the worker never holds a reference to the caller's mutable list.
    /// </summary>
    public NetworkObjectSelectorInfo? NetworkObjectTarget { get; set; }

    /// <summary>
    /// Forwarded by: inspect_network_object, probe_network_object_attributes (optional). The
    /// public inspection path validates [1, 200] unique names; the internal probe repeats that
    /// validation because direct probe callers bypass the public host validation path.
    /// </summary>
    public List<string>? NetworkAttributeNames { get; set; }

    #endregion

    #region Network subnet lifecycle (Phase 4)

    /// <summary>
    /// Forwarded by: create_subnet (new subnet's name), update_subnet (new name for the targeted
    /// subnet; omitted means leave it unchanged).
    /// </summary>
    public string? SubnetName { get; set; }

    /// <summary>Forwarded by: create_subnet ONLY. Valid values: Ethernet, Profibus. Not changeable
    /// on an existing subnet, so update_subnet never forwards it.</summary>
    public string? SubnetNetworkType { get; set; }

    /// <summary>
    /// Forwarded by: create_subnet (optional, PROFIBUS only), update_subnet (optional; omitted
    /// means leave it unchanged).
    /// </summary>
    public int? SubnetHighestAddress { get; set; }

    /// <summary>
    /// Forwarded by: create_subnet (optional, PROFIBUS only), update_subnet (optional; omitted
    /// means leave it unchanged).
    /// </summary>
    public string? SubnetTransmissionSpeed { get; set; }

    #endregion

    #region Internal Network Phase 4 live mutation probe

    /// <summary>Forwarded only by: probe_subnet_lifecycle_mutations.</summary>
    public string? ProbeRunId { get; set; }

    /// <summary>Forwarded only by: probe_subnet_lifecycle_mutations.</summary>
    public string? ProbeConnectedEthernetSubnetId { get; set; }

    /// <summary>Forwarded only by: probe_subnet_lifecycle_mutations.</summary>
    public string? ProbeConnectedProfibusSubnetId { get; set; }

    /// <summary>Forwarded only by: probe_subnet_lifecycle_mutations.</summary>
    public int? ProbeProfibusHighestAddress { get; set; }

    /// <summary>Forwarded only by: probe_subnet_lifecycle_mutations.</summary>
    public string? ProbeProfibusTransmissionSpeed { get; set; }

    #endregion

    #region Generic object read (R0 object_read)

    /// <summary>
    /// Forwarded by: describe_object, list_object_children, read_object_attributes, export_object.
    /// One of <see cref="ObjectRoots"/>; null means <see cref="ObjectRoots.Project"/>.
    /// </summary>
    public string? ObjectRoot { get; set; }

    /// <summary>
    /// Forwarded by: describe_object, list_object_children, read_object_attributes, export_object.
    /// Null or empty addresses the root itself.
    /// </summary>
    public List<ObjectPathSegmentInfo>? ObjectPath { get; set; }

    /// <summary>Forwarded by: read_object_attributes (optional; null reads every declared attribute).</summary>
    public List<string>? ObjectAttributeNames { get; set; }

    /// <summary>Forwarded by: list_object_children (optional composition filter).</summary>
    public List<string>? ObjectCompositionNames { get; set; }

    /// <summary>
    /// Forwarded by: list_object_children and every paged domain read (1..200; null means 50).
    /// </summary>
    public int? ObjectPageSize { get; set; }

    /// <summary>Forwarded by: list_object_children and every paged domain read (opaque continuation).</summary>
    public string? ObjectCursor { get; set; }

    /// <summary>Forwarded by: export_object. Values from <see cref="ObjectExportOptionNames"/>.</summary>
    public List<string>? ObjectExportOptions { get; set; }

    /// <summary>Forwarded by: export_object (character offset of the window; null means 0).</summary>
    public int? ObjectExportOffset { get; set; }

    /// <summary>Forwarded by: export_object (window length, 1..30,000; null means 16,000).</summary>
    public int? ObjectExportMaxChars { get; set; }

    #endregion

    #region PLC read domain (R1 plc_read)

    // PLC selection reuses PlcName (exact PLC software name; null means the only PLC).

    /// <summary>Forwarded by: read_watch_table, read_technology_object, read_block_fingerprints.</summary>
    public string? PlcObjectName { get; set; }

    /// <summary>
    /// Forwarded by: read_watch_table, read_technology_object, read_block_fingerprints (optional
    /// group names below the listing root; null matches any group).
    /// </summary>
    public List<string>? PlcGroupPath { get; set; }

    /// <summary>Forwarded by: every plc_read listing (optional case-insensitive name filter).</summary>
    public string? PlcNameContains { get; set; }

    /// <summary>Forwarded by: compare_software (the right-hand PLC software name).</summary>
    public string? PlcComparePlcName { get; set; }

    /// <summary>Forwarded by: compare_software (true returns identical elements too).</summary>
    public bool? PlcIncludeIdentical { get; set; }

    /// <summary>Forwarded by: read_technology_object (optional parameter-name filter).</summary>
    public List<string>? PlcParameterNames { get; set; }

    #endregion

    #region Hardware reads (R2 network_read extension)

    // Device selection reuses DeviceName (ordinal, case-insensitive, exactly one device).
    // Paging reuses ObjectPageSize/ObjectCursor; comparison reuses PlcIncludeIdentical.

    /// <summary>Forwarded by: compare_hardware (the right-hand device name).</summary>
    public string? CompareDeviceName { get; set; }

    #endregion

    #region Library reads (R3 library_read)

    // Object selection reuses PlcObjectName (type name) and PlcGroupPath (folder path);
    // find_type_instances reuses PlcName; paging reuses ObjectPageSize/ObjectCursor.

    /// <summary>
    /// Forwarded by: every library_read operation except list_libraries. Null selects the project
    /// library; otherwise the exact name of an open global library.
    /// </summary>
    public string? LibraryName { get; set; }

    /// <summary>Forwarded by: check_library_updates (true reports up-to-date types too).</summary>
    public bool? LibraryIncludeUpToDate { get; set; }

    /// <summary>Forwarded by: find_type_instances (optional exact version number, e.g. "1.0.2").</summary>
    public string? LibraryVersion { get; set; }

    #endregion

    #region HMI reads (R4 hmi_read)

    // Screen selection reuses PlcObjectName/PlcGroupPath, the name filter PlcNameContains, and
    // paging ObjectPageSize/ObjectCursor.

    /// <summary>Forwarded by: every hmi_read operation except list_hmis (exact HMI software name; null means the only HMI).</summary>
    public string? HmiName { get; set; }

    #endregion

    #region Project lifecycle

    /// <summary>Forwarded by: create_project.</summary>
    public string? ProjectDirectory { get; set; }

    /// <summary>Forwarded by: create_project.</summary>
    public string? ProjectName { get; set; }

    /// <summary>Forwarded by: create_project.</summary>
    public string? Author { get; set; }

    /// <summary>Forwarded by: create_project.</summary>
    public string? Comment { get; set; }

    /// <summary>Forwarded by: save_project_as.</summary>
    public string? TargetDirectory { get; set; }

    /// <summary>Forwarded by: save_project_as.</summary>
    public string? TargetName { get; set; }

    /// <summary>Forwarded by: open_project. The session-rebind escape hatch.</summary>
    public bool ForceRebind { get; set; }

    /// <summary>
    /// Forwarded by: save_project_as. Whether the session rebinds to the saved copy.
    /// Distinct from ForceRebind.
    /// </summary>
    public bool Rebind { get; set; } = true;

    /// <summary>Forwarded by: archive_project.</summary>
    public string? ArchiveDirectory { get; set; }

    /// <summary>Forwarded by: archive_project.</summary>
    public string? ArchiveName { get; set; }

    /// <summary>Forwarded by: archive_project.</summary>
    public string? ArchiveMode { get; set; }

    /// <summary>Forwarded by: archive_project.</summary>
    public bool SaveBeforeArchive { get; set; } = true;

    /// <summary>Forwarded by: close_project.</summary>
    public bool SaveBeforeClose { get; set; } = true;

    #endregion
}
