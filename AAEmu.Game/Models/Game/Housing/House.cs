using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
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
        public ushort TlId { get; set; }
        public uint TemplateId { get; set; }
        public HousingTemplate Template { get; set; }
        public List<Doodad> AttachedDoodads { get; set; }

        public int CurrentStep
        {
            get => _currentStep;
            set
            {
                _currentStep = value;
                ModelId = _currentStep == -1 ? Template.MainModelId : Template.BuildSteps[_currentStep].ModelId;
                if (_currentStep == -1) // TODO ...
                {
                    foreach (var bindingDoodad in Template.HousingBindingDoodad)
                    {
                        var doodad = DoodadManager.Instance.Create(0, bindingDoodad.DoodadId, this);
                        doodad.AttachPoint = (byte)bindingDoodad.AttachPointId;
                        doodad.Position = bindingDoodad.Position.Clone();
                        doodad.Position.Relative = true;
                        doodad.WorldPosition = Position.Clone();

                        AttachedDoodads.Add(doodad);
                    }
                }
                else
                {
                    foreach (var doodad in AttachedDoodads)
                        if (doodad.ObjId > 0)
                            ObjectIdManager.Instance.ReleaseId(doodad.ObjId);

                    AttachedDoodads.Clear();
                }
            }
        }

        public override int MaxHp => Template.Hp;
        public override UnitCustomModelParams ModelParams { get; set; }
        public byte Permission { get; set; }

        public House()
        {
            Level = 1;
            ModelParams = new UnitCustomModelParams();
            AttachedDoodads = new List<Doodad>();
        }

        public override void Spawn()
        {
            base.Spawn();
            foreach (var doodad in AttachedDoodads)
                doodad.Spawn();
        }

        public override void Delete()
        {
            foreach (var doodad in AttachedDoodads)
                doodad.Delete();
            base.Delete();
        }

        public override void Show()
        {
            base.Show();
            foreach (var doodad in AttachedDoodads)
                doodad.Show();
        }

        public override void Hide()
        {
            foreach (var doodad in AttachedDoodads)
                doodad.Hide();
            base.Hide();
        }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCUnitStatePacket(this));
            character.SendPacket(new SCHouseStatePacket(this));

            var doodads = AttachedDoodads.ToArray();
            for (var i = 0; i < doodads.Length; i += 30)
            {
                var count = doodads.Length - i;
                var temp = new Doodad[count <= 30 ? count : 30];
                Array.Copy(doodads, i, temp, 0, temp.Length);
                character.SendPacket(new SCDoodadsCreatedPacket(temp));
            }
        }

        public override void RemoveVisibleObject(Character character)
        {
            if (character.CurrentTarget != null && character.CurrentTarget == this)
            {
                character.CurrentTarget = null;
                character.SendPacket(new SCTargetChangedPacket(character.ObjId, 0));
            }

            character.SendPacket(new SCUnitsRemovedPacket(new[] {ObjId}));

            var doodadIds = new uint[AttachedDoodads.Count];
            for (var i = 0; i < AttachedDoodads.Count; i++)
                doodadIds[i] = AttachedDoodads[i].ObjId;

            for (var i = 0; i < doodadIds.Length; i += 400)
            {
                var offset = i * 400;
                var length = doodadIds.Length - offset;
                var last = length <= 400;
                var temp = new uint[last ? length : 400];
                Array.Copy(doodadIds, offset, temp, 0, temp.Length);
                character.SendPacket(new SCDoodadsRemovedPacket(last, temp));
            }
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
                command.Parameters.AddWithValue("@owner", OwnerId);
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
            var ownerName = NameManager.Instance.GetCharacterName(OwnerId);
            
            stream.Write(TlId);
            stream.Write(Id); // dbId
            stream.WriteBc(ObjId);
            stream.Write(TemplateId);
            stream.Write(0); // ht
            stream.Write(OwnerId); // type(id)
            stream.Write(OwnerId); // type(id)
            stream.Write(ownerName ?? "");
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
