using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.GameDetection;

public interface IGameDetectionService
{
    // Detect from xEdit executable name
    GameType DetectFromExecutable(string executablePath);

    // Detect from load order file (first master)
    Task<GameType> DetectFromLoadOrderAsync(string loadOrderPath, CancellationToken ct = default);

    /// <summary>
    /// Detect game variant (TTW, Enderal) by scanning the load order for marker plugins.
    /// </summary>
    GameVariant DetectVariant(GameType baseGame, IReadOnlyList<string> pluginNames);

    // Validate game type detection
    bool IsValidGameType(GameType gameType);

    // Get game-specific information
    string GetGameDisplayName(GameType gameType);
    string GetDefaultLoadOrderFileName(GameType gameType);
}
