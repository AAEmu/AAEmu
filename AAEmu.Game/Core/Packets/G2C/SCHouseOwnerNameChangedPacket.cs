using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseOwnerNameChangedPacket(ushort tl, string newName)
    : GamePacket(SCOffsets.SCHouseOwnerNameChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(newName);
        return stream;
    }
}
