using System.Threading.Tasks;

namespace AutoQAC.Services.MO2;

public interface IMO2ValidationService
{
    // Check if MO2 is running
    bool IsMO2Running();

    // Validate MO2 executable
    Task<bool> ValidateMO2ExecutableAsync(string mo2Path);

    // Get warning message if MO2 is running
    string GetMO2RunningWarning();
}
