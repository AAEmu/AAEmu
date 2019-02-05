using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
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

            var type = (MoveTypeEnum)stream.ReadByte();
            var moveType = MoveType.GetType(type);
            stream.Read(moveType);

            if (moveType.VelX != 0 || moveType.VelY != 0 || moveType.VelZ != 0)
            {
                var effects = Connection.ActiveChar.Effects.GetEffectsByType(typeof(BuffTemplate));
                foreach (var effect in effects)
                    if (((BuffTemplate)effect.Template).RemoveOnMove)
                        effect.Exit();
                effects = Connection.ActiveChar.Effects.GetEffectsByType(typeof(BuffEffect));
                foreach (var effect in effects)
                    if (((BuffEffect)effect.Template).Buff.RemoveOnMove)
                        effect.Exit();
            }

            Connection
                .ActiveChar
                .SetPosition(moveType.X, moveType.Y, moveType.Z, moveType.RotationX, moveType.RotationY, moveType.RotationZ);
            Connection.ActiveChar.BroadcastPacket(new SCOneUnitMovementPacket(bc, moveType), false);
        }
    }
}
