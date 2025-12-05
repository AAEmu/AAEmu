using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSuspectGoingBotTrial(uint type, uint type2, bool kicked)
    : GamePacket(SCOffsets.SCSuspectGoingBotTrial, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(type);
        stream.Write(type2);
        stream.Write(kicked);
        return stream;
    }
}
