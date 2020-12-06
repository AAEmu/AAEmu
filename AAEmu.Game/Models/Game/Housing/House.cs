using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Housing
{
    public enum HousingPermission : byte
    {
        Private = 0,
        Guild = 1,
        Public = 2,
        Family = 3
    }

    public sealed class House : Unit
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Housing;
        private object _lock = new object();
        private HousingTemplate _template;
        private int _currentStep;
        private int _allAction;
        private uint _id;
        private uint _accountId;
        private uint _coOwnerId;
        private uint _templateId;
        private int _baseAction;
        private bool _isDirty;
        private HousingPermission _permission;
        private int _numAction;
        private DateTime _placeDate;
        private DateTime _protectionEndDate;

        /// <summary>
        /// IsDirty flag for Houses, not all properties are taken into account here as most of the data that needs to be updated will never change
        /// after it's initial addition to the table, like position/rotation. Therefore it's ok to only set the dirty marker on the other properties
        /// </summary>
        public bool IsDirty { get => _isDirty; set => _isDirty = value; }
        public uint Id { get => _id; set { _id = value; _isDirty = true; } }
        public uint AccountId { get => _accountId; set { _accountId = value; _isDirty = true; } }
        public uint CoOwnerId { get => _coOwnerId; set { _coOwnerId = value; _isDirty = true; } }
        //public ushort TlId { get; set; }
        public uint TemplateId { get => _templateId; set { _templateId = value; _isDirty = true; } }
        public HousingTemplate Template
        {
            get => _template;
            set
            {
                _template = value;
                _allAction = _template.BuildSteps.Values.Sum(step => step.NumActions);
            }
        }
        public List<Doodad> AttachedDoodads { get; set; }
        public int AllAction { get => _allAction; set { _allAction = value; _isDirty = true; } }
        private int BaseAction { get => _baseAction; set { _baseAction = value; _isDirty = true; } }
        public int CurrentAction => BaseAction + NumAction;
        public int NumAction { get => _numAction; set { _numAction = value; _isDirty = true; } }
        public int CurrentStep
        {
            get => _currentStep;
            set
            {
                _currentStep = value;
                _isDirty = true;
                ModelId = _currentStep == -1 ? Template.MainModelId : Template.BuildSteps[_currentStep].ModelId;
                if (_currentStep == -1) // TODO ...
                {
                    foreach (var bindingDoodad in Template.HousingBindingDoodad)
                    {
                        var doodad = DoodadManager.Instance.Create(0, bindingDoodad.DoodadId, this);
                        doodad.AttachPoint = (byte)bindingDoodad.AttachPointId;
                        doodad.Position = bindingDoodad.Position.Clone();
                        doodad.ParentObj = this;

                        AttachedDoodads.Add(doodad);
                    }
                }
                else if (AttachedDoodads.Count > 0)
                {
                    foreach (var doodad in AttachedDoodads)
                        if (doodad.ObjId > 0)
                            ObjectIdManager.Instance.ReleaseId(doodad.ObjId);

                    AttachedDoodads.Clear();
                }

                if (_currentStep > 0)
                {
                    BaseAction = 0;
                    for (var i = 0; i < _currentStep; i++)
                        BaseAction += Template.BuildSteps[i].NumActions;
                }
            }
        }
        public override int MaxHp => Template.Hp;
        public override UnitCustomModelParams ModelParams { get; set; }
        public HousingPermission Permission { get => _permission; set { _permission = value; _isDirty = true; } }

        public DateTime PlaceDate { get => _placeDate; set { _placeDate = value; _isDirty = true; } }
        public DateTime ProtectionEndDate { get => _protectionEndDate; set { _protectionEndDate = value; _isDirty = true; } }
        public DateTime TaxDueDate { get => _protectionEndDate.AddDays(-7); }
        public uint SellToPlayerId { get; set; }
        public uint SellPrice { get; set; }


        public House()
        {
            Level = 1;
            ModelParams = new UnitCustomModelParams();
            AttachedDoodads = new List<Doodad>();
            IsDirty = true;
            Events.OnDeath += OnDeath ;
        }

        public void AddBuildAction()
        {
            if (CurrentStep == -1)
                return;

            lock (_lock)
            {
                var nextAction = NumAction + 1;
                if (Template.BuildSteps[CurrentStep].NumActions > nextAction)
                    NumAction = nextAction;
                else
                {
                    NumAction = 0;
                    var nextStep = CurrentStep + 1;
                    if (Template.BuildSteps.Count > nextStep)
                        CurrentStep = nextStep;
                    else
                    {
                        CurrentStep = -1;
                    }
                }
            }
        }

        #region Visible
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

            character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));

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

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            foreach (var character in WorldManager.Instance.GetAround<Character>(this))
                character.SendPacket(packet);
        }

        #endregion

        public bool Save(MySqlConnection connection, MySqlTransaction transaction = null)
        {
            if (!IsDirty)
                return false;
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText =
                    "REPLACE INTO `housings` " +
                    "(`id`,`account_id`,`owner`,`co_owner`,`template_id`,`name`,`x`,`y`,`z`,`rotation_z`,`current_step`,`current_action`,`permission`,`place_date`,`protected_until`,`faction_id`,`sell_to`,`sell_price`) " +
                    "VALUES(@id,@account_id,@owner,@co_owner,@template_id,@name,@x,@y,@z,@rotation_z,@current_step,@current_action,@permission,@placedate,@protecteduntil,@factionid,@sellto,@sellprice)";

                command.Parameters.AddWithValue("@id", Id);
                command.Parameters.AddWithValue("@account_id", AccountId);
                command.Parameters.AddWithValue("@owner", OwnerId);
                command.Parameters.AddWithValue("@co_owner", CoOwnerId);
                command.Parameters.AddWithValue("@template_id", TemplateId);
                command.Parameters.AddWithValue("@name", Name);
                command.Parameters.AddWithValue("@x", Position.X);
                command.Parameters.AddWithValue("@y", Position.Y);
                command.Parameters.AddWithValue("@z", Position.Z);
                command.Parameters.AddWithValue("@rotation_z", Position.RotationZ);
                command.Parameters.AddWithValue("@current_step", CurrentStep);
                command.Parameters.AddWithValue("@current_action", NumAction);
                command.Parameters.AddWithValue("@permission", (byte)Permission);
                command.Parameters.AddWithValue("@placedate", PlaceDate);
                command.Parameters.AddWithValue("@protecteduntil", ProtectionEndDate);
                command.Parameters.AddWithValue("@factionid", Faction.Id);
                command.Parameters.AddWithValue("@sellto", SellToPlayerId);
                command.Parameters.AddWithValue("@sellprice", SellPrice);
                command.ExecuteNonQuery();
            }

            IsDirty = false;
            return true;
        }

        public PacketStream Write(PacketStream stream)
        {
            var ownerName = NameManager.Instance.GetCharacterName(OwnerId);
            var sellToPlayerName = NameManager.Instance.GetCharacterName(SellToPlayerId);

            stream.Write(TlId);
            stream.Write(Id); // dbId
            stream.WriteBc(ObjId);
            stream.Write(TemplateId);
            stream.WritePisc(ModelId, 0);
            //stream.Write(ModelId); // ht
            stream.Write(CoOwnerId); // type(id)
            stream.Write(OwnerId); // type(id)
            stream.Write(ownerName ?? "");
            stream.Write(AccountId);
            stream.Write((byte)Permission);

            if (CurrentStep == -1)
            {
                stream.Write(0);
                stream.Write(0);
            }
            else
            {
                stream.Write(AllAction); // allstep
                stream.Write(CurrentAction); // curstep
            }
            
            stream.Write(Template?.Taxation?.Tax ?? 0); // payMoneyAmount
            stream.Write(Helpers.ConvertLongX(Position.X));
            stream.Write(Helpers.ConvertLongY(Position.Y));
            stream.Write(Position.Z);
            stream.Write(Name); // house // TODO max length 128
            stream.Write(true); // allowRecover
            stream.Write(SellPrice); // Sale moneyAmount
            stream.Write(SellToPlayerId); // type(id)
            stream.Write(sellToPlayerName??""); // sellToName
            return stream;
        }

        public void OnDeath(object sender, EventArgs args)
        {
            _log.Debug("House died ObjId:{0} - TemplateId:{1} - {2}", ObjId, TemplateId, Name);
            HousingManager.Instance.RemoveDeadHouse(this);
        }

    }
}
