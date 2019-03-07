using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
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
            character.SendPacket(new SCMyHousePacket(this));

            character.SendPacket(new SCUnitStatePacket(this));
            // character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));

            character.SendPacket(new SCHouseStatePacket(this));

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

        public PacketStream Write(PacketStream stream)
        {
            stream.Write(TlId);
            stream.Write(Id); // dbId
            stream.WriteBc(ObjId);
            stream.Write(TemplateId);
            stream.Write(0); // ht
            stream.Write(OwnerId); // type(id)
            stream.Write(OwnerId); // type(id)
            stream.Write(Master?.Name ?? "");
            stream.Write(AccountId);
            stream.Write(Permission);
            stream.Write(Template.BuildSteps.Count); // allstep
            stream.Write(CurrentStep == -1 ? Template.BuildSteps.Count : CurrentStep); // curstep
            stream.Write(0); // payMoneyAmount
            stream.Write(Helpers.ConvertLongX(Position.X));
            stream.Write(Helpers.ConvertLongY(Position.Y));
            stream.Write(Position.Z);
            stream.Write(Template.Name); // house // TODO max length 128
            stream.Write(true); // allowRecover
            stream.Write(0); // moneyAmount
            stream.Write(1); // type(id)
            stream.Write(""); // sellToName
            return stream;
        }
    }
}
