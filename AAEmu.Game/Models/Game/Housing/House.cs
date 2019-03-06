using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Housing
{
    public sealed class House : Unit
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private int _currentStep;

        public uint Id { get; set; }
        public uint AccountId { get; set; }
        public uint OwnerId { get; set; }
        public ushort TlId { get; set; }
        public uint TemplateId { get; set; }
        public HousingTemplate Template { get; set; }

        public int CurrentStep
        {
            get => _currentStep;
            set
            {
                _currentStep = value;
                ModelId = _currentStep == -1 ? Template.MainModelId : Template.BuildSteps[_currentStep].ModelId;
            }
        }

        public override int MaxHp { get; set; } = 1500;
        public override UnitCustomModelParams ModelParams { get; set; }
        public byte Permission { get; set; }

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
            data.Account = 1;
            data.Permission = 2;
            data.AllStep = 3;
            data.CurStep = 1;
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

            // TODO spawn doodads...
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

        public void Save(MySqlConnection connection, MySqlTransaction transaction = null)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText =
                    "REPLACE INTO `housings` " +
                    "(`id`,`account_id`,`owner`,`template_id`,`x`,`y`,`z`,`rotation_z`,`current_step`,`permission`) " +
                    "VALUES(@id,@account_id,@owner,@template_id,@x,@y,@z,@rotation_z,@current_step,@permission)";

                command.Parameters.AddWithValue("@id", Id);
                command.Parameters.AddWithValue("@account_id", AccountId);
                command.Parameters.AddWithValue("@owner", Name);
                command.Parameters.AddWithValue("@template_id", TemplateId);
                command.Parameters.AddWithValue("@x", Position.X);
                command.Parameters.AddWithValue("@y", Position.Y);
                command.Parameters.AddWithValue("@z", Position.Z);
                command.Parameters.AddWithValue("@rotation_z", Position.RotationZ);
                command.Parameters.AddWithValue("@current_step", CurrentStep);
                command.Parameters.AddWithValue("@permission", Permission);
                command.ExecuteNonQuery();
            }
        }
    }
}
