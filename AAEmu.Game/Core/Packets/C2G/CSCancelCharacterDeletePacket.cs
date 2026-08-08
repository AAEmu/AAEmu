using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Client layout: a single u64 "type" - the character id. This shares its serialize function
/// (RVA 0xC688D0) with CSDeleteCharacter, so the two are byte-for-byte the same on the wire.
/// </summary>
public class CSCancelCharacterDeletePacket() : GamePacket(CSOffsets.CSCancelCharacterDeletePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var characterId = stream.ReadUInt64(); // u64 type
        CharacterManager.Instance.SetRestoreCharacter(Connection, (uint)characterId);
    }
}
