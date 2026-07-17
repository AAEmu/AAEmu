using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCWorldQueuePacket(byte worldId, bool isPremium, ushort myTurn, ushort normalLenght, ushort premiumLenght)
    : GamePacket(SCOffsets.SCWorldQueuePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldId);
        stream.Write(isPremium);
        stream.Write(myTurn);
        stream.Write(normalLenght);
        stream.Write(premiumLenght);
        return stream;
    }
}
