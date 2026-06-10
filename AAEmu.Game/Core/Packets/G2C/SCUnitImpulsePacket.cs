using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitImpulsePacket(
    uint unitObjId,
    float velImpulseX,
    float velImpulseY,
    float velImpulseZ,
    float angvelImpulseX,
    float angvelImpulseY,
    float angvelImpulseZ,
    float impulseX,
    float impulseY,
    float impulseZ,
    float angImpulseX,
    float angImpulseY,
    float angImpulseZ)
    : GamePacket(SCOffsets.SCUnitImpulsePacket, 1)
{
    internal static float EncodeUnitObjIdFloat(uint unitObjId)
    {
        return BitConverter.Int32BitsToSingle(unchecked((int)(unitObjId << 8)));
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitObjId);
        stream.Write(EncodeUnitObjIdFloat(unitObjId));
        stream.Write(velImpulseX);
        stream.Write(velImpulseY);
        stream.Write(velImpulseZ);
        stream.Write(angvelImpulseX);
        stream.Write(angvelImpulseY);
        stream.Write(angvelImpulseZ);
        stream.Write(impulseX);
        stream.Write(impulseY);
        stream.Write(impulseZ);
        stream.Write(angImpulseX);
        stream.Write(angImpulseY);
        stream.Write(angImpulseZ);
        return stream;
    }
}
