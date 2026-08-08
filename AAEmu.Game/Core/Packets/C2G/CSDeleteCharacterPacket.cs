using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Client layout (serialize at RVA 0xC688D0): a single u64 "type" - the character id.
/// </summary>
/// <remarks>
/// We read four bytes where the client writes eight, so the id we ended up with was never the one the
/// player clicked. CharacterManager then found no such character on the account and answered with a
/// rejection - which the client could not read either, because that packet had the same fault. The
/// visible result was a delete that did nothing at all and said nothing about it.
/// </remarks>
public class CSDeleteCharacterPacket() : GamePacket(CSOffsets.CSDeleteCharacterPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var characterId = stream.ReadUInt64(); // u64 type
        CharacterManager.Instance.SetDeleteCharacter(Connection, (uint)characterId);
    }
}
