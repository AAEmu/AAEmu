using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseBuildProgressPacket(ushort tl, uint modelId, int allStep, int curStep)
    : GamePacket(SCOffsets.SCHouseBuildProgressPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(modelId);
        stream.Write(allStep);
        stream.Write(curStep);
        return stream;
    }
}
