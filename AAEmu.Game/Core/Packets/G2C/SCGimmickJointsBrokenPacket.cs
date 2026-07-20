using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Gimmicks;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCGimmickJointsBrokenPacket(Gimmick[] gimmicks) : GamePacket(SCOffsets.SCGimmickJointsBrokenPacket, 5)
{
    private const int JointId = 0;
    private const int Epicenter = 0;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)gimmicks.Length); // TODO max length 200
        foreach (var gimmick in gimmicks)
        {
            stream.Write(gimmick.ObjId); // gimmickId
            stream.Write(JointId);      // jointId
            stream.Write(Epicenter);     // epicenter
        }

        return stream;
    }
}
