using System.Data;
using System.Data.Common;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mate;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterMates(Character owner)
{
    /*
     * TODO:
     * EQUIPMENT CHANGE
     * FINISH ATTRIBUTES
     * NAME FROM LOCALIZED TABLE
     */

    private Character Owner { get; set; } = owner;

    private readonly Dictionary<ulong, MateDb> _mates = []; // itemId, MountDb
    private readonly object _saveSync = new();

    public MateDb GetMateInfo(ulong itemId)
    {
        lock (_saveSync)
            return _mates.GetValueOrDefault(itemId)?.Clone();
    }

    /// <summary>
    /// Applies a state update under the save lock and returns a detached snapshot. The update is an
    /// in-memory write only; the row reaches the database with the next <see cref="Save"/>, so this
    /// never blocks a gameplay thread on an unrelated character's save transaction.
    /// </summary>
    public MateDb UpdateMateInfo(ulong itemId, Action<MateDb> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_saveSync)
        {
            if (!_mates.TryGetValue(itemId, out var current))
                return null;

            var updated = current.Clone();
            update(updated);
            updated.Id = current.Id;
            updated.ItemId = current.ItemId;
            updated.Owner = current.Owner;
            _mates[itemId] = updated;
            return updated.Clone();
        }
    }

    private MateDb CreateNewMate(ulong itemId, NpcTemplate npcTemplate)
    {
        lock (_saveSync)
        {
            if (_mates.ContainsKey(itemId)) return null;
        }
        var template = new MateDb
        {
            // TODO
            Id = MateIdManager.Instance.GetNextId(),
            ItemId = itemId,
            Level = npcTemplate.Level,
            Name = LocalizationManager.Instance.Get("npcs", "name", npcTemplate.Id, npcTemplate.Name), // npcTemplate.Name,
            Owner = Owner.Id,
            Mileage = 0,
            Xp = ExperienceManager.Instance.GetExpForLevel(npcTemplate.Level, true),
            Hp = 9999,
            Mp = 9999,
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        lock (_saveSync)
        {
            if (!_mates.TryAdd(template.ItemId, template))
                return null;
        }
        return template.Clone();
    }

    public (SummonMateTemplate ItemTemplate, NpcTemplate NpcTemplate, MateRecoveryState RecoveryState)
        ResolveSummonMate(uint itemTemplateId)
    {
        if (ItemManager.Instance.GetTemplate(itemTemplateId) is not SummonMateTemplate itemTemplate)
        {
            throw new InvalidDataException(
                $"Summon mate item {itemTemplateId} has no item_summon_mates template.");
        }

        var npcId = itemTemplate.NpcId;
        var npcTemplate = NpcManager.Instance.GetTemplate(npcId)
            ?? throw new InvalidDataException(
                $"Summon mate item {itemTemplateId} references missing NPC template {npcId}.");
        var recoveryState = MateGameData.Instance.GetRecoveryState(itemTemplate);
        return (itemTemplate, npcTemplate, recoveryState);
    }

    public void SpawnMount(SkillItem skillData)
    {
        var despawnedOldPets = false;
        // Check if we had already spawned something
        foreach(var oldMate in Owner.ParentWorld.MateManager.GetActiveMates(Owner.Id).ToList())
        {
            DespawnMate(oldMate.TlId);
            despawnedOldPets = true;
        }
        if (despawnedOldPets)
            return;

        var item = Owner.Inventory.GetItemById(skillData.ItemId);
        if (item == null) return;

        var (itemTemplate, template, recoveryState) = ResolveSummonMate(item.TemplateId);
        var npcId = itemTemplate.NpcId;
        var tlId = (ushort)TlIdManager.Instance.GetNextId();
        var objId = ObjectIdManager.Instance.GetNextId();
        if (GetMateInfo(skillData.ItemId) == null)
            CreateNewMate(skillData.ItemId, template);
        // The live mate always follows current content; nothing recovery-related is persisted yet.
        var mateDbInfo = GetMateInfo(skillData.ItemId)
            ?? throw new InvalidDataException($"Owned mate {skillData.ItemId} was not created.");

        var mount = new Units.Mate
        {
            ObjId = objId,
            TlId = tlId,
            OwnerId = Owner.Id,
            Name = mateDbInfo.Name,
            TemplateId = template.Id,
            Template = template,
            ModelId = template.ModelId,
            Faction = Owner.Faction,
            Level = (byte)mateDbInfo.Level,
            MateType = MateGameData.Instance.GetMateType((uint)template.MateEquipSlotPackId),
            Hp = mateDbInfo.Hp > 0 ? mateDbInfo.Hp : 100,
            Mp = mateDbInfo.Mp > 0 ? mateDbInfo.Mp : 100,
            OwnerObjId = Owner.ObjId,
            Id = mateDbInfo.Id,
            ItemId = mateDbInfo.ItemId,
            UserState = 1, // TODO
            Experience = mateDbInfo.Xp,
            Mileage = mateDbInfo.Mileage,
            SpawnDelayTime = 0, // TODO
            RecoveryState = recoveryState,
            DbInfo = mateDbInfo
        };

        mount.InitializeSeatTopology(MateGameData.Instance.GetMateSeats(npcId));

        mount.Transform = Owner.Transform.CloneDetached(mount);
        SusManager.Instance.ResetAnalyzeMountDeltaMovement(mount.Id);

        foreach (var skill in MateGameData.Instance.GetMateSkills(npcId))
            mount.Skills.Add(skill);

        foreach (var buffId in template.Buffs)
        {
            var buff = SkillManager.Instance.GetBuffTemplate(buffId);
            if (buff == null)
                continue;

            var obj = new SkillCasterUnit(mount.ObjId);
            buff.Apply(mount, obj, mount, null, null, new EffectSource(), null, DateTime.UtcNow);
        }

        mount.Equipment = ItemManager.Instance.GetItemContainerForCharacter(Owner.Id, SlotType.EquipmentMate, mount, mount.Id);
        mount.UpdateGearBonuses(null, null);

        // CreateNewMate seeds Hp/Mp at 9999 as "full"; after MaxHp is known, treat that sentinel
        // (or any over-cap) as full so the pet frame does not spawn mid-bar waiting on regen.
        if (mateDbInfo.Hp >= 9999 || mount.Hp >= mount.MaxHp)
            mount.Hp = mount.MaxHp;
        else
            mount.Hp = Math.Min(mount.Hp, mount.MaxHp);
        if (mateDbInfo.Mp >= 9999 || mount.Mp >= mount.MaxMp)
            mount.Mp = mount.MaxMp;
        else
            mount.Mp = Math.Min(mount.Mp, mount.MaxMp);

        mount.Transform.Local.AddDistanceToFront(3f);
        //Logger.Warn($"Spawn the pet:{mount.ObjId} X={mount.Transform.World.Position.X} Y={mount.Transform.World.Position.Y}");
        Owner.ParentWorld.MateManager.AddActiveMateAndSpawn(Owner, mount, item);
        mount.PostUpdateCurrentHp(mount, 0, mount.Hp, KillReason.Unknown);

        // UnitState at spawn carries current Hp; gear MaxHealth is already in MaxHp. Re-push state
        // and points so the pet frame denominator matches server MaxHp (SCUnitPoints alone does not).
        UpdateMateInfo(skillData.ItemId, db =>
        {
            db.Hp = mount.Hp;
            db.Mp = mount.Mp;
        });
        Owner.SendPacket(new SCUnitStatePacket(mount));
        Owner.SendPacket(new SCUnitPointsPacket(mount.ObjId, mount.Hp, mount.Mp));
        WorldIntegration.RelayUnitPointsToZone?.Invoke(mount.ObjId, mount.Hp, mount.Mp);

        // remove_by_summoned (110 buffs, the 감정 표현_* poses and the dances among them): summoning a
        // mount ends the summoner's poses, the same way getting on one does through remove_on_mount. The
        // owner is whoever used the summon item, so the raise belongs here rather than in MateManager,
        // which this path reaches through AddActiveMateAndSpawn.
        Owner.Buffs.TriggerRemoveOn(BuffRemoveOn.Summoned);
    }

    /// <summary>
    /// Copies the live mate's persisted fields into its owned row so progress earned while
    /// summoned survives a despawn, a logout or the periodic save. Runs on gameplay threads
    /// (party kill, disconnect), so it must never fail because a save transaction is in flight.
    /// </summary>
    public void CaptureActiveMateState(Units.Mate mateInfo)
    {
        if (mateInfo == null)
            return;

        UpdateMateInfo(mateInfo.ItemId, mateDbInfo =>
        {
            mateDbInfo.Hp = mateInfo.Hp;
            mateDbInfo.Mp = mateInfo.Mp;
            mateDbInfo.Level = mateInfo.Level;
            mateDbInfo.Xp = mateInfo.Experience;
            mateDbInfo.Mileage = mateInfo.Mileage;
            mateDbInfo.Name = mateInfo.Name;
            mateDbInfo.UpdatedAt = DateTime.UtcNow;
        });
    }

    public void DespawnMate(uint tlId)
    {
        var mateInfo = Owner.ParentWorld.MateManager.GetActiveMateByTlId(tlId);
        CaptureActiveMateState(mateInfo);
        Owner.ParentWorld.MateManager.RemoveActiveMateAndDespawn(Owner, tlId);
    }

    /// <summary>
    /// Load pet data of the player
    /// </summary>
    /// <param name="connection"></param>
    public void Load(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM mates WHERE `owner` = @owner";
        AddParameter(command, "@owner", Owner.Id);
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var template = new MateDb
            {
                Id = Convert.ToUInt32(reader.GetValue(reader.GetOrdinal("id"))),
                ItemId = Convert.ToUInt64(reader.GetValue(reader.GetOrdinal("item_id"))),
                Name = reader.GetString("name"),
                Xp = reader.GetInt32("xp"),
                Level = Convert.ToUInt16(reader.GetValue(reader.GetOrdinal("level"))),
                Mileage = reader.GetInt32("mileage"),
                Hp = reader.GetInt32("hp"),
                Mp = reader.GetInt32("mp"),
                Owner = Convert.ToUInt32(reader.GetValue(reader.GetOrdinal("owner"))),
                UpdatedAt = reader.GetDateTime("updated_at"),
                CreatedAt = reader.GetDateTime("created_at")
            };
            lock (_saveSync)
                _mates.Add(template.ItemId, template);
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Writes every owned mate row inside the caller's transaction. The snapshot is taken under
    /// the save lock, so a concurrent <see cref="UpdateMateInfo"/> is either included here or lands
    /// in the in-memory row that the next save writes; it is never rejected and never lost.
    /// </summary>
    public void Save(DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        MateDb[] mateSnapshot;
        lock (_saveSync)
            mateSnapshot = _mates.Values.Select(mate => mate.Clone()).ToArray();

        foreach (var value in mateSnapshot)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "REPLACE INTO mates(`id`,`item_id`,`name`,`xp`,`level`,`mileage`,`hp`,`mp`,`owner`,`updated_at`,`created_at`) " +
                "VALUES (@id, @item_id, @name, @xp, @level, @mileage, @hp, @mp, @owner, @updated_at, @created_at)";
            AddParameter(command, "@id", value.Id);
            AddParameter(command, "@item_id", value.ItemId);
            AddParameter(command, "@name", value.Name);
            AddParameter(command, "@xp", value.Xp);
            AddParameter(command, "@level", value.Level);
            AddParameter(command, "@mileage", value.Mileage);
            AddParameter(command, "@hp", value.Hp);
            AddParameter(command, "@mp", value.Mp);
            AddParameter(command, "@owner", value.Owner);
            AddParameter(command, "@updated_at", value.UpdatedAt);
            AddParameter(command, "@created_at", value.CreatedAt);
            command.ExecuteNonQuery();
        }
    }
}

public class MateDb
{
    public uint Id { get; set; }
    public ulong ItemId { get; set; }
    public string Name { get; set; }
    public int Xp { get; set; }
    public ushort Level { get; set; }
    public int Mileage { get; set; }
    public int Hp { get; set; }
    public int Mp { get; set; }
    public uint Owner { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public MateDb Clone() => (MateDb)MemberwiseClone();
}
