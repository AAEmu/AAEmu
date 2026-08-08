using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Client layout (read at RVA 0xC59C20), which names every field:
///
///   type                u64   the character id - EIGHT bytes, not four
///   deleteStatus        u8
///   deleteRequestedTime u64   unix seconds
///   deleteDelay         u64   unix seconds
///
/// The id was written as u32, which shifted the status and both timestamps by four bytes. Neither the
/// confirmation nor the rejection could be read, so a delete request produced no visible reaction at
/// all - not even an error.
/// </summary>
public class SCDeleteCharacterResponsePacket(
    uint characterId,
    byte status,
    DateTime? deleteRequestedTime = null,
    DateTime? deleteDelay = null)
    : GamePacket(SCOffsets.SCDeleteCharacterResponsePacket, 1)
{
    private readonly DateTime _deleteRequestedTime = deleteRequestedTime ?? DateTime.MinValue;
    private readonly DateTime _deleteDelay = deleteDelay ?? DateTime.MinValue;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)characterId);   // u64 type
        stream.Write(status);               // u8  deleteStatus
        stream.Write(_deleteRequestedTime); // u64 deleteRequestedTime
        stream.Write(_deleteDelay);         // u64 deleteDelay
        return stream;
    }
}
