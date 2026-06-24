namespace AutoQAC.Services.MO2;

public sealed record Mo2InstanceInfo(
    string BaseDirectory,
    string ModsDirectory,
    string ProfilesDirectory,
    string OverwriteDirectory,
    string? IniSelectedProfile,
    string? GameName,
    bool IsAutoDetected,
    string? IniPath);
