using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The in-game event board's answer when the server is running no board events: the client clears the board
/// instead of leaving the row it asked about unbuilt.
/// </summary>
/// <remarks>10.0.2.13 body: none at all — the packet is its opcode (0x2DF).</remarks>
public class SCEventEmptyPacket() : GamePacket(SCOffsets.SCEventEmptyPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        return stream;
    }
}
