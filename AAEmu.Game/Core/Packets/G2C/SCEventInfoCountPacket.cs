using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The in-game event board's head: how many events the server is running and when that list was loaded.
/// The client's event window draws one row per event, so a server running none tells it zero rather than
/// leaving the window waiting for rows.
/// </summary>
/// <remarks>
/// 10.0.2.13 body, named by its own serializer: <c>u32 count</c>, <c>i64 loadedTime</c> — 12 bytes. Without
/// this packet the player-frame event window dereferences its uninitialised event list on show and crashes,
/// which is why it goes out at world entry as well as in answer to <c>CS 0x1B7</c>.
/// </remarks>
public class SCEventInfoCountPacket(uint count, long loadedTime)
    : GamePacket(SCOffsets.SCEventInfoCountPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(count);
        stream.Write(loadedTime);

        return stream;
    }
}
