using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Housing;

public sealed class House : Unit
{
    public override UnitTypeFlag TypeFlag { get => UnitTypeFlag.Housing; }
    public override BaseUnitType BaseUnitType => BaseUnitType.Housing;
    public override ModelPostureType ModelPostureType { get => ModelPostureType.HouseState; }
    private readonly object _lock = new();
    private HousingTemplate _template;
    private int _currentStep;
    private bool _isBeingLoadedFromDb;
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
    private bool _allowRecover;
    private uint _sellToPlayerId;
    private uint _sellPrice;

    /// <summary>
    /// IsDirty flag for Houses, not all properties are taken into account here as most of the data that needs to be updated will never change
    /// after it's initial addition to the table, like position/rotation. Therefore it's ok to only set the dirty marker on the other properties
    /// </summary>
    public bool IsDirty { get => _isDirty; set => _isDirty = value; }
    /// <summary>
    /// When true, suppresses bound doodad spawning in the CurrentStep setter so they can be loaded from the database instead.
    /// </summary>
    public bool IsBeingLoadedFromDb { get => _isBeingLoadedFromDb; set => _isBeingLoadedFromDb = value; }
    public new uint Id { get => _id; set { _id = value; _isDirty = true; } }
    public uint AccountId { get => _accountId; set { _accountId = value; _isDirty = true; } }
    public uint CoOwnerId { get => _coOwnerId; set { _coOwnerId = value; _isDirty = true; } }
    //public ushort TlId { get; set; }
    public new uint TemplateId { get => _templateId; set { _templateId = value; _isDirty = true; } }
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
                if (!_isBeingLoadedFromDb)
                {
                    foreach (var bindingDoodad in Template.HousingBindingDoodad)
                    {
                        var doodad = DoodadManager.Instance.Create(ParentWorld, 0, bindingDoodad.DoodadId, this, true);
                        if (doodad == null)
                        {
                            Logger.Error($"CurrentStep: Failed to create bound doodad templateId={bindingDoodad.DoodadId} for house {Id} — template not found, skipping.");
                            continue;
                        }
                        doodad.AttachPoint = bindingDoodad.AttachPointId;
                        doodad.ParentObj = this;
                        doodad.Transform = this.Transform.CloneDetached(doodad);
                        doodad.Transform.Parent = this.Transform;
                        doodad.Transform.Local.ApplyWorldSpawnPositionWithDeg(bindingDoodad.Position);
                        if (AppConfiguration.Instance.World.UsePersistentHouseDoodads)
                        {
                            doodad.IsPersistent = true;
                            doodad.InitDoodad();
                            doodad.Save();
                        }
                        else
                        {
                            doodad.InitDoodad();
                        }
                        AttachedDoodads.Add(doodad);
                    }
                }
            }
            else if (AttachedDoodads.Count > 0)
            {
                foreach (var doodad in AttachedDoodads)
                {
                    if (doodad.IsPersistent)
                    {
                        if (doodad.ObjId > 0)
                            NonUnitObjectIdManager.Instance.ReleaseId(doodad.ObjId);
                        doodad.Delete();
                    }
                    else if (doodad.ObjId > 0)
                        NonUnitObjectIdManager.Instance.ReleaseId(doodad.ObjId);
                }
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

    public HousingPermission Permission
    {
        get => _permission;
        set { _permission = _template != null && _template.AlwaysPublic ? HousingPermission.Public : value; _isDirty = true; }
    }

    public DateTime PlaceDate { get => _placeDate; set { _placeDate = value; _isDirty = true; } }
    public DateTime ProtectionEndDate { get => _protectionEndDate; set { _protectionEndDate = value; _isDirty = true; } }
    public DateTime TaxDueDate { get => _protectionEndDate.AddDays(-7); }
    public uint SellToPlayerId { get => _sellToPlayerId; set { _sellToPlayerId = value; _isDirty = true; } }
    public uint SellPrice { get => _sellPrice; set { _sellPrice = value; _isDirty = true; } }
    public bool AllowRecover { get => _allowRecover; set { _allowRecover = value; _isDirty = true; } }

    // House always gets its guild from its owner
    public override Expedition Expedition
    {
        get
        {
            var guildId = ExpeditionManager.Instance.GetExpeditionOfCharacter(OwnerId);
            if (guildId == 0)
                return null;
            return ExpeditionManager.Instance.GetExpedition(guildId);
        }
        set
        {
            // Ignored, we always get the guild from its owner
        }
    }

    public House()
    {
        Level = 1;
        AttachedDoodads = [];
        IsDirty = true;
        Events.OnDeath += OnDeath;
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
        {
            if (doodad.IsPersistent)
            {
                if (doodad.ObjId > 0)
                    NonUnitObjectIdManager.Instance.ReleaseId(doodad.ObjId);
                doodad.Delete(); // removes from DB and PlayerDoodads
            }
            else
            {
                if (doodad.AttachPoint == AttachPointKind.None)
                    doodad.Transform.Parent = null; // detach furniture from transform hierarchy
                if (doodad.ObjId > 0)
                    NonUnitObjectIdManager.Instance.ReleaseId(doodad.ObjId);
            }
        }
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

        // UnitState carries a faction only for idType 0, so a house arrives unfactioned and reads as
        // the unit's current faction equals the packet's oldId, and a freshly created unit sits at 0,
        // so this has to be sent as Invalid → real exactly as Npc.AddVisibleObject does.
        if (Faction != null)
            character.SendPacket(new SCUnitFactionChangedPacket(
                ObjId, Name ?? "", FactionsEnum.Invalid, Faction.Id, false));

        // TODO: This should be handled in the base.AddVisibleObject
        var doodads = AttachedDoodads.ToArray();
        for (var i = 0; i < doodads.Length; i += SCDoodadsCreatedPacket.MaxCountPerPacket)
        {
            var count = doodads.Length - i;
            var temp = new Doodad[count <= SCDoodadsCreatedPacket.MaxCountPerPacket ? count : SCDoodadsCreatedPacket.MaxCountPerPacket];
            Array.Copy(doodads, i, temp, 0, temp.Length);
            character.SendPacket(new SCDoodadsCreatedPacket(temp));
        }

        base.AddVisibleObject(character);
    }

    public override void RemoveVisibleObject(Character character)
    {
        base.RemoveVisibleObject(character);

        character.SendPacket(new SCUnitsRemovedPacket([ObjId]));

        // TODO: This should be handled in base.RemoveVisibleObject
        var doodadIds = new uint[AttachedDoodads.Count];
        for (var i = 0; i < AttachedDoodads.Count; i++)
            doodadIds[i] = AttachedDoodads[i].ObjId;

        for (var i = 0; i < doodadIds.Length; i += SCDoodadsRemovedPacket.MaxCountPerPacket)
        {
            var offset = i * SCDoodadsRemovedPacket.MaxCountPerPacket;
            var length = doodadIds.Length - offset;
            var last = length <= SCDoodadsRemovedPacket.MaxCountPerPacket;
            var temp = new uint[last ? length : SCDoodadsRemovedPacket.MaxCountPerPacket];
            Array.Copy(doodadIds, offset, temp, 0, temp.Length);
            character.SendPacket(new SCDoodadsRemovedPacket(last, temp));
        }
    }

    #endregion

    public bool Save(MySqlConnection connection, MySqlTransaction transaction = null)
    {
        if (!IsDirty)
            return false;
        if (AccountId <= 0 || OwnerId <= 0)
            return false; // recently destroyed/expired house
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText =
                "REPLACE INTO `housings` " +
                "(`id`,`account_id`,`owner`,`co_owner`,`template_id`,`name`,`x`,`y`,`z`,`yaw`,`pitch`,`roll`,`current_step`,`current_action`,`permission`,`place_date`," +
                "`protected_until`,`faction_id`,`sell_to`,`sell_price`, `allow_recover`) " +
                "VALUES(@id,@account_id,@owner,@co_owner,@template_id,@name,@x,@y,@z,@yaw,@pitch,@roll,@current_step,@current_action,@permission,@placedate," +
                "@protecteduntil,@factionid,@sellto,@sellprice,@allowrecover)";

            command.Parameters.AddWithValue("@id", Id);
            command.Parameters.AddWithValue("@account_id", AccountId);
            command.Parameters.AddWithValue("@owner", OwnerId);
            command.Parameters.AddWithValue("@co_owner", CoOwnerId);
            command.Parameters.AddWithValue("@template_id", TemplateId);
            command.Parameters.AddWithValue("@name", Name);
            command.Parameters.AddWithValue("@x", Transform.World.Position.X);
            command.Parameters.AddWithValue("@y", Transform.World.Position.Y);
            command.Parameters.AddWithValue("@z", Transform.World.Position.Z);
            command.Parameters.AddWithValue("@roll", Transform.World.Rotation.X);
            command.Parameters.AddWithValue("@pitch", Transform.World.Rotation.Y);
            command.Parameters.AddWithValue("@yaw", Transform.World.Rotation.Z);
            command.Parameters.AddWithValue("@current_step", CurrentStep);
            command.Parameters.AddWithValue("@current_action", NumAction);
            command.Parameters.AddWithValue("@permission", (byte)Permission);
            command.Parameters.AddWithValue("@placedate", PlaceDate);
            command.Parameters.AddWithValue("@protecteduntil", ProtectionEndDate);
            command.Parameters.AddWithValue("@factionid", Faction.Id);
            command.Parameters.AddWithValue("@sellto", SellToPlayerId);
            command.Parameters.AddWithValue("@sellprice", SellPrice);
            command.Parameters.AddWithValue("@allowrecover", AllowRecover);
            command.Prepare();
            command.ExecuteNonQuery();
        }

        IsDirty = false;
        return true;
    }

    private const int UccSlotCount = 5;

    public PacketStream Write(PacketStream stream)
    {
        var ownerName = NameManager.Instance.GetCharacterName(OwnerId);
        var sellToPlayerName = NameManager.Instance.GetCharacterName(SellToPlayerId);

        // single pisc of three values rather than a plain templateId followed by a two-value pisc
        // (the write branch pushes struct +3, +0x2F and +0x30, and the read branch primes the
        // decoder with a count of 3); payMoneyAmount moved ahead of the owner pair and widened,
        // as did the owner ids and accountId; and the tail gained isPublic, isBoundButler, five
        // ucc slots and two positions.
        //
        // passes struct +3 to its housing template lookup, and copies +0x2F and +0x30 into the House
        // object it builds. Those two sit immediately after permission (+0x2E) — the same place v1.2
        // wrote allstep/curstep as plain i32s — so 10.0.2.13 only moved the pair into the pisc group,
        // it did not remove it. A finished house reports 0/0, as it did in v1.2; that is what stops the
        // client treating the building as a construction site and re-enables its interactions.
        var allStep = CurrentStep == -1 ? 0u : (uint)AllAction;
        var curStep = CurrentStep == -1 ? 0u : (uint)CurrentAction;

        stream.Write(TlId);                                     // tl (i16)
        stream.Write(Id);                                       // dbId (i32)
        stream.WriteBc(ObjId);
        stream.WritePisc(TemplateId, allStep, curStep);         // templateId, allstep, curstep
        stream.Write((ulong)(Template?.Taxation?.Tax ?? 0));    // moneyAmount (u64)
        stream.Write(ModelId);                                  // ht (u32)
        stream.Write((ulong)CoOwnerId);                         // original owner who placed it (u64)
        stream.Write((ulong)OwnerId);                           // current owner (u64)
        stream.Write(ownerName ?? "");                          // owner (string, cap 0x80)
        stream.Write((long)AccountId);                          // accountId (i64)
        stream.Write((byte)Permission);                         // permission (i8)
        stream.Write(Helpers.ConvertLongX(Transform.World.Position.X));
        stream.Write(Helpers.ConvertLongY(Transform.World.Position.Y));
        stream.Write(Transform.World.Position.Z);
        stream.Write(Name);                                     // house (string, cap 0x80)
        stream.Write(AllowRecover);                             // allowRecover (bool)
        stream.Write((ulong)SellPrice);                         // sale moneyAmount (u64)
        stream.Write(sellToPlayerName ?? "");                   // sellToName (string, cap 0x80)
        stream.Write(0u);                                       // TODO(v10): expandedDecoLimit — no server-side source yet
        stream.Write(0);                                        // unnamed i32 at struct +0x80
        stream.Write(Permission == HousingPermission.Public);   // isPublic (bool)
        stream.Write(false);                                    // TODO(v10): isBoundButler — butlers are not modelled yet
        stream.Write(0);                                        // unnamed i32 at struct +0x82

        // Five ucc slots, each houseId + u64 + kind + position. Empty until user-created content
        // is modelled; the client reads all five unconditionally.
        for (var i = 0; i < UccSlotCount; i++)
        {
            stream.Write(0);   // houseId (i32)
            stream.Write(0ul); // u64
            stream.Write(0u);  // ucc_kind
            stream.Write(0u);  // ucc_positon
        }

        for (var i = 0; i < 2; i++)
        {
            stream.Write(0ul);
            stream.Write(0ul);
            stream.Write(0f);
        }

        return stream;
    }

    public void OnDeath(object sender, EventArgs args)
    {
        Logger.Debug("House died ObjId:{0} - TemplateId:{1} - {2}", ObjId, TemplateId, Name);
        HousingManager.Instance.RemoveDeadHouse(this);
    }

    public override bool AllowedToInteract(Character player)
    {
        if (Template.AlwaysPublic)
            return base.AllowedToInteract(player);
        if (CurrentStep != -1) // unfinished houses can't be used to private store, so always true
            return base.AllowedToInteract(player);
        switch (Permission)
        {
            case HousingPermission.Private:
                if (player.Id == OwnerId)
                    return base.AllowedToInteract(player);
                var ownerAccount = NameManager.Instance.GetCharacterAccount(OwnerId);
                return player.AccountId == ownerAccount && base.AllowedToInteract(player);
            case HousingPermission.Family when player.Family > 0:
                return FamilyManager.Instance.GetFamily(player.Family).Members.Any(x => x.Id == OwnerId);
            case HousingPermission.Guild when (player.Expedition?.Id > 0):
                return player.Expedition.Members.Any(x => x.CharacterId == OwnerId);
            case HousingPermission.Public:
            default:
                return base.AllowedToInteract(player);
        }
    }

    public override Character GetOwnerCharacter()
    {
        if (OwnerId > 0)
            return WorldManager.Instance.GetCharacterById(OwnerId)?.GetOwnerCharacter();
        return null;
    }
}
