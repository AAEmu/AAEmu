using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Client layout (read at RVA 0xC59CA0): u64 "type" - the character id - then the name as a string.
/// The id used to be written as u32, which left the name starting four bytes early and unreadable.
/// </summary>
public class SCCharacterDeletedPacket(uint characterId, string characterName)
    : GamePacket(SCOffsets.SCCharacterDeletedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)characterId);   // u64     type
        stream.Write(characterName);        // wstring name
        return stream;
    }
}
