using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Faction power standings: the superior and inferior factions (blue and red in the nation list), the week,
/// every faction's points this week, and the factions that may conduct diplomacy (the client only lets a hero
/// whose root faction is in <c>configs</c> open the alliance request).
/// </summary>
public class SCFactionPowerScorePacket(
    IReadOnlyCollection<uint> superiors,
    IReadOnlyCollection<uint> inferiors,
    uint weekNum,
    IReadOnlyCollection<FactionPowerScore> scores,
    IReadOnlyCollection<uint> diplomacyFactionIds)
    : GamePacket(SCOffsets.SCFactionPowerScorePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        WriteSet(stream, superiors);
        WriteSet(stream, inferiors);
        stream.Write(weekNum);
        stream.Write((uint)(scores?.Count ?? 0));
        foreach (var score in scores ?? [])
            stream.Write(score);
        stream.Write(true); // isDiplomacyConfig
        WriteSet(stream, diplomacyFactionIds);
        return stream;
    }

    private static void WriteSet(PacketStream stream, IReadOnlyCollection<uint> ids)
    {
        var sorted = (ids ?? []).Distinct().OrderBy(id => id).ToArray();
        stream.Write((uint)sorted.Length);
        foreach (var id in sorted)
            stream.Write(id);
    }
}
