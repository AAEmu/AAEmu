using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;
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
        private object _lock = new object();
        private HousingTemplate _template;
        private int _currentStep;
        private int _allAction;
        private int _baseAction;

        public uint Id { get; set; }
        public uint AccountId { get; set; }
        public uint CoOwnerId { get; set; }
        public ushort TlId { get; set; }
        public uint TemplateId { get; set; }
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
        public int AllAction => _allAction;
        public int CurrentAction => _baseAction + NumAction;
        public int NumAction { get; set; }
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
                else if (AttachedDoodads.Count > 0)
                {
                    foreach (var doodad in AttachedDoodads)
                        if (doodad.ObjId > 0)
                            ObjectIdManager.Instance.ReleaseId(doodad.ObjId);

                    AttachedDoodads.Clear();
                }

                if (_currentStep > 0)
                {
                    _baseAction = 0;
                    for (var i = 0; i < _currentStep; i++)
                        _baseAction += Template.BuildSteps[i].NumActions;
                }
            }
        }
        public DateTime PlaceDate { get; set; }

        public override int MaxHp => Template.Hp;
        public override UnitCustomModelParams ModelParams { get; set; }
        public HousingPermission Permission { get; set; }

        public House()
        {
            Level = 1;
            ModelParams = new UnitCustomModelParams();
            AttachedDoodads = new List<Doodad>();
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

                        using (var ctx = new GameDBContext())
                            Save(ctx);
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
        #endregion

        public void Save(GameDBContext ctx)
        {
            ctx.Housings.RemoveRange(
                ctx.Housings.Where(h =>
                    h.Id == Id &&
                    h.AccountId == AccountId &&
                    h.Owner == OwnerId));
            ctx.SaveChanges();

            ctx.Housings.Add(this.ToEntity());
            ctx.SaveChanges();
        }

        public PacketStream Write(PacketStream stream)
        {
            var ownerName = NameManager.Instance.GetCharacterName(OwnerId);

            stream.Write(TlId);
            stream.Write(Id); // dbId
            stream.WriteBc(ObjId);
            stream.Write(TemplateId);
            stream.Write(0); // ht
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

            stream.Write(0); // payMoneyAmount
            stream.Write(Helpers.ConvertLongX(Position.X));
            stream.Write(Helpers.ConvertLongY(Position.Y));
            stream.Write(Position.Z);
            stream.Write(Template.Name); // house // TODO max length 128
            stream.Write(true); // allowRecover
            stream.Write(0); // moneyAmount
            stream.Write(0u); // type(id)
            stream.Write(""); // sellToName
            return stream;
        }

        public DB.Game.Housings ToEntity()
            =>
            new DB.Game.Housings()
            {
                Id            =        this.Id                  ,
                AccountId     =        this.AccountId           ,
                Owner         =        this.OwnerId             ,
                CoOwner       =        this.CoOwnerId           ,
                TemplateId    =        this.TemplateId          ,
                Name          =        this.Name                ,
                X             =        this.Position.X          ,
                Y             =        this.Position.Y          ,
                Z             =        this.Position.Z          ,
                RotationZ     = (byte) this.Position.RotationZ  ,
                CurrentStep   = (byte) this.CurrentStep         ,
                CurrentAction =        this.CurrentAction       ,
                Permission    = (byte) this.Permission          ,
            };
    }
}
