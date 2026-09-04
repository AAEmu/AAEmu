using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSyncItemLifespanPacket(bool added, ulong itemId, uint itemTemplateId, DateTime expireTime, byte lifeSpanType = 0)
    : GamePacket(SCOffsets.SCSyncItemLifespanPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(added);
        stream.Write(itemId);
        stream.Write(itemTemplateId);
        stream.Write(expireTime);
        stream.Write(lifeSpanType);

        return stream;
    }
}
