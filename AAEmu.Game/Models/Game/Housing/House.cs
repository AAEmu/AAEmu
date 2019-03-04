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
            var data = new HouseData();
            data.Tl = TlId;
            data.DbId = 146502;
            data.ObjId = ObjId;
            data.TemplateId = Template.Id;
            data.Ht = 0;
            data.Unk2Id = character.Id;
            data.Unk3Id = character.Id;
            data.Owner = character.Name;
            data.Account = character.AccountId;
            data.Permission = 2;
            data.AllStep = 3;
            data.CurStep = 0;
            data.X = Position.X;
            data.Y = Position.Y;
            data.Z = Position.Z;
            data.House = Template.Name;
            data.AllowRecover = true;
            data.MoneyAmount = 0;
            data.Unk4Id = 1;
            data.SellToName = "";

            character.SendPacket(new SCMyHousePacket(data));

            character.SendPacket(new SCUnitStatePacket(this));
            // character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));

            character.SendPacket(new SCHouseStatePacket(data));
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
