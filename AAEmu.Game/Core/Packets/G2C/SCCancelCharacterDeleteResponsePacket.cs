using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Client layout (read at RVA 0xC59D40): u64 "type" - the character id - then u8 deleteStatus.
/// The id used to be written as u32, so cancelling a pending deletion never reached the UI either.
/// </summary>
public class SCCancelCharacterDeleteResponsePacket(uint characterId, byte deleteStatus)
    : GamePacket(SCOffsets.SCCancelCharacterDeleteResponsePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)characterId);   // u64 type
        stream.Write(deleteStatus);         // u8  deleteStatus
        return stream;
    }
}
