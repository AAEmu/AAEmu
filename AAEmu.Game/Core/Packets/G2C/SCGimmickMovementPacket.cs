using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Gimmicks;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCGimmickMovementPacket(Gimmick gimmick) : GamePacket(SCOffsets.SCGimmickMovementPacket, 1)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(gimmick.ObjId);
        stream.Write(gimmick.Time);
        stream.Write(Helpers.ConvertLongX(gimmick.Transform.World.Position.X)); // WorldPosition qx,qx,fz
        stream.Write(Helpers.ConvertLongY(gimmick.Transform.World.Position.Y));
        stream.Write(gimmick.Transform.World.Position.Z);
        var q = gimmick.Transform.World.ToQuaternion();
        stream.Write(q.X); // Quaternion Rotation
        stream.Write(q.Y);
        stream.Write(q.Z);
        stream.Write(q.W);
        stream.Write(gimmick.Vel.X);    // vector3 vel
        stream.Write(gimmick.Vel.Y);
        stream.Write(gimmick.Vel.Z);
        stream.Write(gimmick.AngVel.X); // vector3 angVel
        stream.Write(gimmick.AngVel.Y);
        stream.Write(gimmick.AngVel.Z);
        stream.Write(gimmick.Scale);

        return stream;
    }
}
