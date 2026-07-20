using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHousePermissionChangedPacket(ushort tl, byte permission)
    : GamePacket(SCOffsets.SCHousePermissionChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(permission);
        return stream;
    }
}
