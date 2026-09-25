using System.IO;
using System.Reflection;
using Siemens.Engineering;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.ObjectModel;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Siemens-facing handlers of the generic <c>object_read</c> operations. Path resolution, paging,
/// description, and export windows are delegated to the Siemens-free <c>ObjectModel</c> core.
/// </summary>
internal static class ObjectReadService
{
    /// <summary>Optional Openness assemblies whose presence gates R1–R5 domains.</summary>
    internal static readonly string[] OptionalAssemblies =
    {
        "Siemens.Engineering.WinCC",
        "Siemens.Engineering.WinCCUnified",
        "Siemens.Engineering.Safety",
        "Siemens.Engineering.SafetyValidation",
        "Siemens.Engineering.TestSuite",
        "Siemens.Engineering.TeamcenterGateway",
        "Siemens.Engineering.MC.Drives",
    };

    public static IObjectNode Resolve(Project project, TiaPortal? portal, WorkerRequest request)
    {
        IEngineeringObject root = request.ObjectRoot switch
        {
            ObjectRoots.Portal => portal ?? throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                "The worker is not attached to a TIA Portal instance."),
            _ => project,
        };

        return ObjectPathResolver.Resolve(new EngineeringObjectNode(root), request.ObjectRoot, request.ObjectPath);
    }

    public static ObjectDescriptionInfo Describe(Project project, TiaPortal? portal, WorkerRequest request)
        => ObjectDescriptionBuilder.Build(Resolve(project, portal, request), request.ObjectRoot, request.ObjectPath);

    public static ObjectChildrenPageInfo ListChildren(Project project, TiaPortal? portal, WorkerRequest request)
        => ObjectChildrenPager.Build(
            Resolve(project, portal, request),
            request.ObjectRoot,
            request.ObjectPath,
            request.ObjectCompositionNames,
            request.ObjectPageSize,
            request.ObjectCursor);

    public static ObjectAttributesInfo ReadAttributes(Project project, TiaPortal? portal, WorkerRequest request)
    {
        var names = request.ObjectAttributeNames;
        if (names is not null
            && (names.Count == 0
                || names.Count > ObjectReadLimits.MaxAttributeNames
                || names.Any(string.IsNullOrWhiteSpace)
                || names.Distinct(StringComparer.Ordinal).Count() != names.Count))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"ObjectAttributeNames must contain between 1 and {ObjectReadLimits.MaxAttributeNames} unique, nonblank names when supplied.");
        }

        var node = (EngineeringObjectNode)Resolve(project, portal, request);
        var dynamic = EngineeringAttributeInspector.Inspect(node.EngineeringObject, names);
        var dynamicNames = new HashSet<string>(dynamic.Observations.Select(o => o.Name), StringComparer.Ordinal);

        // Navigation properties are reported as modeled, read-only entries so every attribute step
        // describe_object offers is also visible here (as an unrepresentable object reference).
        var modeled = node.NavigationProperties()
            .Where(property => !dynamicNames.Contains(property.Name)
                && (names is null || names.Contains(property.Name, StringComparer.Ordinal)))
            .Select(property => new NetworkAttributeObservation
            {
                Name = property.Name,
                ReadValue = () => node.ReadProperty(property),
                CanRead = true,
                CanWrite = false,
                SupportedTypes = new[] { property.PropertyType.FullName ?? property.PropertyType.Name },
            })
            .ToList();
        return new ObjectAttributesInfo
        {
            Root = request.ObjectRoot ?? ObjectRoots.Project,
            ObjectPath = (request.ObjectPath ?? new List<ObjectPathSegmentInfo>()).Select(ObjectChildrenPager.Copy).ToList(),
            TypeName = node.TypeName,
            Attributes = NetworkAttributeResultBuilder.Build(
                modeled,
                dynamic.Observations,
                names).ToList(),
            Diagnostics = dynamic.Diagnostics.Distinct(StringComparer.Ordinal).ToList(),
        };
    }

    public static ObjectExportInfo Export(Project project, TiaPortal? portal, WorkerRequest request)
    {
        var options = ParseExportOptions(request.ObjectExportOptions);
        var node = (EngineeringObjectNode)Resolve(project, portal, request);
        var exportMethod = EngineeringObjectNode.FindExportMethod(node.EngineeringObject.GetType())
            ?? throw new WorkerOperationException(
                WorkerFailureCategories.TargetKindUnsupported,
                $"'{ObjectPathRules.SimpleName(node.TypeName)}' cannot be exported as SimaticML.");

        var directory = Path.Combine(Path.GetTempPath(), "TiaMcpServer", "object-export", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var file = new FileInfo(Path.Combine(directory, "object.xml"));
            try
            {
                exportMethod.Invoke(node.EngineeringObject, new object[] { file, options });
            }
            catch (TargetInvocationException ex) when (ex.InnerException is EngineeringException engineering)
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.WorkerOperationFailed,
                    $"TIA Portal could not export '{ObjectPathRules.SimpleName(node.TypeName)}': {engineering.Message}");
            }

            file.Refresh();
            if (!file.Exists)
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.WorkerOperationFailed,
                    "TIA Portal reported a completed export but produced no file.");
            }

            // DocumentInfo describes the export run (timestamp, installed products), not the object.
            // Removing it, as get_block_content does, makes repeated exports byte-identical so the
            // whole-document digest stays valid across the windows of one object state.
            var document = BlockXmlSanitizer.RemoveDocumentInfo(File.ReadAllText(file.FullName));
            var result = new ObjectExportInfo
            {
                Root = request.ObjectRoot ?? ObjectRoots.Project,
                ObjectPath = (request.ObjectPath ?? new List<ObjectPathSegmentInfo>()).Select(ObjectChildrenPager.Copy).ToList(),
                TypeName = node.TypeName,
                ExportOptions = (request.ObjectExportOptions ?? new List<string>()).OrderBy(o => o, StringComparer.Ordinal).ToList(),
            };
            ObjectExportWindow.Apply(result, document, request.ObjectExportOffset, request.ObjectExportMaxChars);
            return result;
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not remove temporary export directory: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public static OpennessCapabilitiesInfo ListCapabilities(TiaPortal? portal)
    {
        var result = new OpennessCapabilitiesInfo();
        try
        {
            var process = portal?.GetCurrentProcess();
            if (process is null)
            {
                result.Diagnostics.Add("The worker is not attached to a TIA Portal instance.");
            }
            else
            {
                foreach (var product in process.InstalledSoftware ?? Enumerable.Empty<TiaPortalProduct>())
                {
                    result.Products.Add(new InstalledProductInfo
                    {
                        Name = product.Name ?? string.Empty,
                        Version = product.Version,
                        Options = (product.Options ?? Enumerable.Empty<TiaPortalProduct>())
                            .Select(option => string.IsNullOrEmpty(option.Version) ? option.Name : $"{option.Name} {option.Version}")
                            .Where(text => !string.IsNullOrWhiteSpace(text))
                            .ToList(),
                    });
                }
            }
        }
        catch (Exception ex) when (ex is EngineeringException or InvalidOperationException or NotSupportedException)
        {
            result.Diagnostics.Add($"Installed products could not be read: {ex.Message}");
        }

        result.Products = result.Products
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ThenBy(p => p.Version, StringComparer.Ordinal)
            .ToList();

        // Presence is read from assembly metadata only: nothing is loaded into the worker, and the
        // exact-version rule of AssemblyResolver still governs any later real load.
        string? directory = null;
        try
        {
            directory = AssemblyResolver.GetOpennessInstallPath();
        }
        catch (Exception ex)
        {
            result.Diagnostics.Add($"The Openness assembly directory could not be located: {ex.Message}");
        }

        foreach (var name in OptionalAssemblies)
        {
            var entry = new OpennessAssemblyAvailabilityInfo { Name = name };
            if (directory is not null)
            {
                var path = Path.Combine(directory, name + ".dll");
                try
                {
                    if (File.Exists(path))
                    {
                        entry.Version = AssemblyName.GetAssemblyName(path).Version?.ToString();
                        entry.Available = true;
                    }
                }
                catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
                {
                    result.Diagnostics.Add($"'{name}' is present but unreadable: {ex.GetType().Name}.");
                }
            }

            result.Assemblies.Add(entry);
        }

        return result;
    }

    private static ExportOptions ParseExportOptions(IReadOnlyList<string>? names)
    {
        var options = ExportOptions.None;
        if (names is null)
        {
            return options;
        }

        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "ObjectExportOptions must be unique.");
        }

        foreach (var name in names)
        {
            options |= name switch
            {
                ObjectExportOptionNames.WithDefaults => ExportOptions.WithDefaults,
                ObjectExportOptionNames.WithReadOnly => ExportOptions.WithReadOnly,
                _ => throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    $"ObjectExportOptions values must be one of: {string.Join(", ", ObjectExportOptionNames.All)}."),
            };
        }

        return options;
    }
}
