using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Static;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCEnvDamagePacket(
    EnvSource source,
    uint target,
    uint amount,
    uint gimmickId = 0,
    Vector3 position = new Vector3(),
    float collisionImpact = 0,
    byte p = 0)
    : GamePacket(SCOffsets.SCEnvDamagePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)source);
        stream.WriteBc(target);
        stream.Write(amount);
        if (source == EnvSource.Gimmick)
            stream.Write(gimmickId);
        if (source == EnvSource.Collision)
        {
            stream.Write(Helpers.ConvertLongX(position.X));
            stream.Write(Helpers.ConvertLongY(position.Y));
            stream.Write(position.Z);
            stream.Write(collisionImpact);
            stream.Write(p);
        }
        return stream;
    }
}
