using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCActiveWeaponChangedPacket(uint objId, byte activeWeapon)
    : GamePacket(SCOffsets.SCActiveWeaponChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(activeWeapon);
        return stream;
    }
}
