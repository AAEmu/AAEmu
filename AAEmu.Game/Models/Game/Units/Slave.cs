using System.Collections.Generic;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.DoodadObj;
using NLog;

namespace AAEmu.Game.Models.Game.Units
{
    public class Slave : Unit
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Slave;
        public uint Id { get; set; }
        public uint TemplateId { get; set; }
        public uint BondingObjId { get; set; } = 0;

        public SlaveTemplate Template { get; set; }
        public Character Bounded { get; set; }
        public Character Summoner { get; set; }
        public List<Doodad> AttachedDoodads { get; set; }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCUnitStatePacket(this));
            character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
            character.SendPacket(new SCSlaveStatePacket(ObjId, TlId, Summoner.Name, Summoner.ObjId, Template.Id));
        }

        public override void RemoveVisibleObject(Character character)
        {
            if (character.CurrentTarget != null && character.CurrentTarget == this)
            {
                character.CurrentTarget = null;
                character.SendPacket(new SCTargetChangedPacket(character.ObjId, 0));
            }

            character.SendPacket(new SCUnitsRemovedPacket(new[] {ObjId}));
        }
    }
}
