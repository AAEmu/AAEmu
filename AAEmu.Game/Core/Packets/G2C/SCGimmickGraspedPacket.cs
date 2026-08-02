using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// </remarks>
public class SCGimmickGraspedPacket(int gimmickId, int grasperUnitId, bool grasped) : GamePacket(SCOffsets.SCGimmickGraspedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(gimmickId);
        stream.Write(grasperUnitId);
        stream.Write(grasped);
        return stream;
    }
}
