using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Answer to CSFactionRelationHistoryGet: past agreements, oldest first. The client appends each
/// row and drops the oldest past faction_diplomacy_history_size,, and
/// the history window reads the list from the tail.
/// </summary>
/// <remarks>
/// Body from the client's serializer:
/// count(i32), Size(i32), then Size relation entries (FactionRelationWire).
/// </remarks>
public sealed class SCFactionRelationHistoryPacket(IReadOnlyList<FactionDiplomacyAgreement> histories)
    : GamePacket(SCOffsets.SCFactionRelationHistoryPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(histories.Count);
        stream.Write(histories.Count);
        foreach (var entry in histories)
            FactionRelationWire.Write(stream, entry);
        return stream;
    }
}
