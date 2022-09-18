using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Butler
{
    public sealed class Butler : Unit
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public ButlersTemplate Template { get; set; }
        public override UnitCustomModelParams ModelParams { get; set; }

        public override float Scale => 1.0f;

        public Butler()
        {
            ModelParams = new UnitCustomModelParams();
            Name = "";
            Equipment.ContainerSize = 32; // 28 in 1.2, 29 equip slots in 3.5, 31 in 5.7, 32 in 7+
        }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCUnitStatePacket(this));
            //character.SendPacket(new SCShipyardStatePacket(Template));
            //character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
        }

        public override void RemoveVisibleObject(Character character)
        {
            if (character.CurrentTarget != null && character.CurrentTarget == this)
            {
                character.CurrentTarget = null;
                character.SendPacket(new SCTargetChangedPacket(character.ObjId, 0));
            }

            character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));

        }
    }
}
