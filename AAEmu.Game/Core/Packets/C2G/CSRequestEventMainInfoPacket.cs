using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks for the in-game event board's entries themselves.
/// </summary>
/// <remarks>
/// A board entry is <c>SC 0x2DE</c>: a main order plus three sub-structs whose layouts have not been read
/// yet, so a board with an event in it cannot be answered without handing the client a half-built row. This
/// server runs no board events, which is exactly the case the empty answer exists for — <c>SC 0x2DF</c>,
/// which has no body and clears the board. The request has no body either.
/// </remarks>
public class CSRequestEventMainInfoPacket() : GamePacket(CSOffsets.CSRequestEventMainInfoPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        Connection.ActiveChar?.SendPacket(new SCEventEmptyPacket());
    }
}
