using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCOneUnitMovementPacket : GamePacket // TODO ... SCUnitMovementsPacket
    {
        private readonly uint _id;
        private readonly MoveType _type;

        public SCOneUnitMovementPacket(uint id, MoveType type) : base(SCOffsets.SCOneUnitMovementPacket, 1)
        {
            _id = id;
            _type = type;

            // ---- test Ai ----
            var unit = WorldManager.Instance.GetUnit(id);
            if (!(unit is Npc npc)) { return; }

            var movementAction = new MovementAction(
                new Point(type.X, type.Y, type.Z, type.RotationX, type.RotationY, type.RotationZ),
                new Point(0, 0, 0),
                type.RotationZ,
                3,
                MoveTypeEnum.Unit
            );
            npc.VisibleAi.OwnerMoved(movementAction);
            // ---- test Ai ----
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_id);
            stream.Write((byte) _type.Type);
            stream.Write(_type);
            return stream;
        }
    }
}
