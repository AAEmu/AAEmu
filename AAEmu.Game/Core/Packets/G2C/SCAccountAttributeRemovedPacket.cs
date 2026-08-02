using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// </remarks>
public class SCAccountAttributeRemovedPacket(AccountAttributeKind kind, uint extraKind)
    : GamePacket(SCOffsets.SCAccountAttributeRemovedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)kind);
        stream.Write(extraKind);
        return stream;
    }
}
