using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Static;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCEnvDamagePacket(EnvSource source, uint target, uint amount, uint gimmickId = 0)
    : GamePacket(SCOffsets.SCEnvDamagePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)source);
        stream.WriteBc(target);
        stream.Write(amount);
        if (source == EnvSource.Gimmick)
            stream.Write(gimmickId);
        return stream;
    }
}
