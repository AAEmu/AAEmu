using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCEventInfoCountPacket() : GamePacket(SCOffsets.SCEventInfoCountPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Three event-category counts; the client's player-frame event window reads them on show. The reference
        // server sends all zeros (no active events) at world entry — a 12-byte body. Without it the window's
        // OnShow dereferences its uninitialized event list and crashes.
        stream.Write(0u); // normalEventCount
        stream.Write(0u); // hotTimeEventCount
        stream.Write(0u); // noticeEventCount

        return stream;
    }
}
