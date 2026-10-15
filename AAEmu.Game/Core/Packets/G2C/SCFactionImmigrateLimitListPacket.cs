using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public readonly record struct FactionImmigrateLimit(uint ToFactionId, uint FromFactionId, uint MaxCount, uint UseCount, uint PreCount);

/// <summary>
/// This week's exile slots for every route. The client lists them per accepting faction in the alliance info
/// ("accepting exiles" and the slots left per origin = max - use - pre).
/// </summary>
public class SCFactionImmigrateLimitListPacket(uint weekNum, IReadOnlyCollection<FactionImmigrateLimit> limits)
    : GamePacket(SCOffsets.SCFactionImmigrateLimitListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(weekNum);
        stream.Write((uint)limits.Count); // dataCount
        stream.Write((uint)limits.Count);
        foreach (var limit in limits)
        {
            stream.Write((int)limit.ToFactionId);
            stream.Write((int)limit.FromFactionId);
            stream.Write(limit.MaxCount);
            stream.Write(limit.UseCount);
            stream.Write(limit.PreCount);
        }

        return stream;
    }
}
