using Siemens.Engineering;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Siemens-facing <c>governance_read</c> handler for the Portal process list, which comes from the
/// static <see cref="TiaPortal.GetProcesses"/> rather than from the object model. Listing processes
/// does not attach to them.
/// </summary>
internal static class GovernanceReadService
{
    public static PortalProcessListInfo ListPortalProcesses(int? attachedProcessId)
    {
        var result = new PortalProcessListInfo();
        foreach (var process in TiaPortal.GetProcesses())
        {
            try
            {
                result.Processes.Add(new PortalProcessInfo
                {
                    Id = process.Id,
                    Mode = process.Mode.ToString(),
                    ProjectPath = process.ProjectPath?.FullName,
                    AcquisitionTime = process.AcquisitionTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                    IsAttached = attachedProcessId == process.Id,
                });
            }
            catch (EngineeringException ex)
            {
                result.Diagnostics.Add($"A TIA Portal process could not be described: {ex.Message}");
            }
        }

        result.Processes = result.Processes.OrderBy(process => process.Id).ToList();
        return result;
    }
}
