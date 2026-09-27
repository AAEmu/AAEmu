using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Nation:RequestDiplomacy(charId, factionId): a seated hero asks a hero of a hostile nation for a
/// peace agreement. Only the character id travels; the faction id is the client's own pre-check.
/// </summary>
/// <remarks>
/// Body serializer (folded with every single-u64 packet): one u64 named
/// "type", filled from the charId argument at the send site.
/// </remarks>
public class CSFactionRelationRequestPacket() : GamePacket(CSOffsets.CSFactionRelationRequestPacket, 1)
{
    public ulong TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadUInt64();

        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        FactionDiplomacyManager.Instance.Request(character, TypeValue);
    }
}
