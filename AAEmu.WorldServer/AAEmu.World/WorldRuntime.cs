using AAEmu.World.Models;

namespace AAEmu.World;

/// <summary>Process-wide World settings after Config bind (zone handlers need activate radius, etc.).</summary>
public static class WorldRuntime
{
    public static WorldAppConfiguration Config { get; set; } = new();
}
