using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game.Units.Movements
{
    public class DefaultMoveType : MoveType
    {
        public override void Read(PacketStream stream)
        {
            base.Read(stream);
            (X, Y, Z) = stream.ReadPosition();
            VelX = stream.ReadInt16();
            VelY = stream.ReadInt16();
            VelZ = stream.ReadInt16();
            RotationX = (sbyte) stream.ReadInt16();
            RotationY = (sbyte) stream.ReadInt16();
            RotationZ = (sbyte) stream.ReadInt16();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.WritePosition(X, Y, Z);
            stream.Write(VelX);
            stream.Write(VelY);
            stream.Write(VelZ);
            stream.Write((short) RotationX);
            stream.Write((short) RotationY);
            stream.Write((short) RotationZ);
            return stream;
        }
    }
}
