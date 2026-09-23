using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The server confirms a cross-server departure request.
/// </summary>
/// <remarks>
/// Wire: 10.0.2.13 <c>SCDepartureServerGrantedPacket</c>, opcode 0x006, zero fields
/// (protocol-10.0.2.13/catalog_SC.md row 16; the shipped client's packet structs list the class
/// with <c>fields: []</c>). Nothing is
/// written because the client's reader reads nothing.
/// </remarks>
public class SCDepartureServerGrantedPacket() : GamePacket(SCOffsets.SCDepartureServerGrantedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        return stream;
    }
}
