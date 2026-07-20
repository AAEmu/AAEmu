using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpChangedPacket(uint objId, int exp, bool shouldAddAbilityExp)
    : GamePacket(SCOffsets.SCExpChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(exp);
        stream.Write(shouldAddAbilityExp);
        return stream;
    }
}
