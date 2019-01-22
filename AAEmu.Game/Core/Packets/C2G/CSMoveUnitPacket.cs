using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSMoveUnitPacket : GamePacket
    {
        public CSMoveUnitPacket() : base(0x088, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var bc = stream.ReadBc();
            if (bc != Connection.ActiveChar.ObjId)
                return;

            var type = (MoveTypeEnum) stream.ReadByte();
            var moveType = MoveType.GetType(type);
            stream.Read(moveType);

            Connection
                .ActiveChar
                .SetPosition(moveType.X, moveType.Y, moveType.Z, moveType.RotationX, moveType.RotationY, moveType.RotationZ);
            Connection.ActiveChar.BroadcastPacket(new SCOneUnitMovementPacket(bc, moveType), false);
        }
    }
}