using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Housing
{
    public sealed class House : Unit
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public uint Id { get; set; }
        public ushort TlId { get; set; }
        public uint TemplateId { get; set; }
        public HousingTemplate Template { get; set; }
        public short BuildStep { get; set; }
        public override int MaxHp { get; set; } = 1500;
        public override UnitCustomModelParams ModelParams { get; set; }
        
        public House()
        {
            Level = 1;
            ModelParams = new UnitCustomModelParams();
        }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCUnitStatePacket(this));
            character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
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
