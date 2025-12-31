using System.Threading.Tasks;

namespace AutoQAC.Services.MO2;

public interface IMo2ValidationService
{
    // Check if MO2 is running
    bool IsMo2Running();

    // Validate MO2 executable
    Task<bool> ValidateMo2ExecutableAsync(string mo2Path);

    // Get warning message if MO2 is running
    string GetMo2RunningWarning();
}
