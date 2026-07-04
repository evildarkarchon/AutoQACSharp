using System.Threading.Tasks;

namespace AutoQAC.Services.MO2;

public sealed class Mo2ValidationService : IMo2ValidationService
{
    public bool IsMo2Running()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("ModOrganizer");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public Task<bool> ValidateMo2ExecutableAsync(string mo2Path)
    {
        return Task.FromResult(Mo2ExecutablePathValidator.IsValidExecutablePath(mo2Path));
    }

    public string GetMo2RunningWarning()
    {
        return "Warning: Mod Organizer 2 is currently running. " +
               "For best results, close MO2 before cleaning plugins.";
    }
}
