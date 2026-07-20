using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCShowCommonFarmPacket(int farmType, int count, Vector3 pos)
    : GamePacket(SCOffsets.SCShowCommonFarmPacket, 5)
{
    private readonly float _x = pos.X;
    private readonly float _y = pos.Y;
    private readonly float _z = pos.Z;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(farmType);
        stream.Write(count);
        stream.Write(Helpers.ConvertLongX(_x));
        stream.Write(Helpers.ConvertLongY(_y));
        stream.Write(_z);
        return stream;
    }

}
