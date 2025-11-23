using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AutoQAC.Services.MO2;

public sealed class MO2ValidationService : IMO2ValidationService
{
    public bool IsMO2Running()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("ModOrganizer");
        return processes.Length > 0;
    }

    public Task<bool> ValidateMO2ExecutableAsync(string mo2Path)
    {
        if (!File.Exists(mo2Path))
            return Task.FromResult(false);

        var fileName = Path.GetFileName(mo2Path).ToLowerInvariant();
        return Task.FromResult(fileName == "modorganizer.exe");
    }

    public string GetMO2RunningWarning()
    {
        return "Warning: Mod Organizer 2 is currently running. " +
               "For best results, close MO2 before cleaning plugins.";
    }
}
