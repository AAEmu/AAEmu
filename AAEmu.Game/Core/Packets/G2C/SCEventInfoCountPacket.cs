using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: u32 count, i64 loadedTime — 12 bytes, the same width the
/// reference server sends at world entry. Without it the player-frame event window dereferences its
/// uninitialised event list on show and crashes.
/// </remarks>
public class SCEventInfoCountPacket() : GamePacket(SCOffsets.SCEventInfoCountPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(0u);  // count — no active events
        stream.Write(0L);  // loadedTime

        return stream;
    }
}
