using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMySlavePacket(
    uint unitId,
    ushort tl,
    string slaveName,
    uint templateId,
    int hp,
    int maxHp,
    float x,
    float y,
    float z)
    : GamePacket(SCOffsets.SCMySlavePacket, 5)
{
    // TODO slaveId

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(tl);
        stream.Write(slaveName);
        stream.Write(templateId);
        stream.Write(hp);
        stream.Write(maxHp);
        stream.Write(Helpers.ConvertLongX(x));
        stream.Write(Helpers.ConvertLongY(y));
        stream.Write(z);
        return stream;
    }
}
