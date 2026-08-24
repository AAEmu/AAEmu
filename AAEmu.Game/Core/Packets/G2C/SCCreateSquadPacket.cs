using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Squad;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Create ack. u8 ignoreMinGameSize + one SquadBase (mask 0x0F) inside the nested
/// two-u16 blob. See NestedBlobWire.
/// </summary>
public class SCCreateSquadPacket(bool ignoreMinGameSize, SquadListEntry entry)
    : GamePacket(SCOffsets.SCCreateSquadPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ignoreMinGameSize);
        var payload = new PacketStream();
        entry.Write(payload);
        NestedBlobWire.Write(stream, payload);
        return stream;
    }
}
