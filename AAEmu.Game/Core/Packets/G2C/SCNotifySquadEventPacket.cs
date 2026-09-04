using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCNotifySquadEventPacket(sbyte eventType, long recruitId)
    : GamePacket(SCOffsets.SCNotifySquadEventPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(eventType);
        stream.Write(recruitId);
        return stream;
    }
}
