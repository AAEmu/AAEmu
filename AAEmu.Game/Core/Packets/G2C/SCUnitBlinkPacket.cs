using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitBlinkPacket : GamePacket
{
    private readonly uint _objId;
    private readonly float _distance;
    private readonly float _degree;
    private readonly float _x;
    private readonly float _y;
    private readonly float _z;

    public SCUnitBlinkPacket(uint objId, float distance, float degree, float x, float y, float z) : base(SCOffsets.SCUnitBlinkPacket, 5)
    {
        _objId = objId;
        _distance = distance;
        _degree = degree;
        _x = x;
        _y = y;
        _z = z;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_objId);  // unitId
        stream.Write(_distance); // distance
        stream.Write(_degree);   // degree
        stream.Write(Helpers.ConvertLongX(_x)); // readPosXYZ
        stream.Write(Helpers.ConvertLongY(_y));
        stream.Write(_z);
        return stream;
    }
}
