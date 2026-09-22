using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Answer to CSFactionRelationCountGet. The client's map of diplomacy counters: the own row
/// (viewer, 0) holds today's concluded agreements and each (hero, viewer) row that hero's denials,
/// which X2Nation:GetRelationCount turns into the "3 per day" and "denied 3 times" gates.
/// </summary>
/// <remarks>
/// Body from the client's serializer (x2game-dev.dll 0x39c86b00 -> 0x39c85b60, pair 0x39c7d910):
/// Size(i32) then per entry k.first(u64), k.second(u64), v(u32).
/// </remarks>
public sealed class SCFactionRelationCountPacket(IReadOnlyList<FactionDiplomacyCount> counts)
    : GamePacket(SCOffsets.SCFactionRelationCountPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(counts.Count);
        foreach (var count in counts)
        {
            stream.Write((ulong)count.CharacterId);
            stream.Write((ulong)count.OtherId);
            stream.Write(count.Count);
        }

        return stream;
    }
}
