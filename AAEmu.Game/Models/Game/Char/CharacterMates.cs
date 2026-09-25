using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

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
    private readonly Dictionary<MateDbKey, long> _removedMates = [];
    private readonly HashSet<DbTransaction> _commitGates = [];
    private readonly ConditionalWeakTable<DbTransaction, CharacterMatesSaveToken> _saveTokens = new();
    private readonly object _saveSync = new();
    private long _removalVersion;

    public MateDb GetMateInfo(ulong itemId)
    {
        lock (_saveSync)
            return _mates.GetValueOrDefault(itemId)?.Clone();
    }

    /// <summary>
    /// Applies a state update under the mate save gate and returns a detached snapshot.
    /// </summary>
    public MateDb UpdateMateInfo(ulong itemId, Action<MateDb> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_saveSync)
        {
            ThrowIfCommitGateActive();
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

    /// <summary>
    /// Removes one owned mate from the in-memory collection and stages its exact database row deletion.
    /// The marker is cleared only after the transaction that executed the DELETE commits.
    /// </summary>
    public bool RemoveMate(ulong itemId)
    {
        lock (_saveSync)
        {
            ThrowIfCommitGateActive();
            if (!_mates.Remove(itemId, out var removed))
                return false;

            var key = MateDbKey.From(removed);
            _removedMates[key] = ++_removalVersion;
            return true;
        }
    }

    /// <summary>
    /// Restores an owned mate, cancelling only a staged deletion for the same full database key.
    /// This is also used when a summon is recreated while a save transaction is still open.
    /// </summary>
    public void RestoreMate(MateDb mate)
    {
        ArgumentNullException.ThrowIfNull(mate);
        if (mate.Owner != Owner.Id)
            throw new InvalidDataException("A mate cannot be restored under a different owner.");

        lock (_saveSync)
        {
            ThrowIfCommitGateActive();
            _removedMates.Remove(MateDbKey.From(mate));
            _mates[mate.ItemId] = mate.Clone();
        }
    }

    private MateDb CreateNewMate(
        ulong itemId,
        NpcTemplate npcTemplate,
        MateRecoveryState recoveryState)
    {
        lock (_saveSync)
        {
            ThrowIfCommitGateActive();
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
            RecoveryState = recoveryState,
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        lock (_saveSync)
        {
            ThrowIfCommitGateActive();
            _removedMates.Remove(MateDbKey.From(template));
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
        var mateDbInfo = GetMateInfo(skillData.ItemId);
        if (mateDbInfo == null)
            CreateNewMate(skillData.ItemId, template, recoveryState);
        // The persisted snapshot follows current content on every summon. Legacy/null rows and
        // content revisions are reconciled here without treating an old value as authoritative.
        mateDbInfo = UpdateMateInfo(skillData.ItemId, db => db.RecoveryState = recoveryState)
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
            mateDbInfo.RecoveryState = mateInfo.RecoveryState;
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
            var reviveDelay = NullableInt32(reader, "mate_revive_delay");
            var reviveHpPercent = NullableInt32(reader, "mate_revive_hp_percent");
            var reviveMpPercent = NullableInt32(reader, "mate_revive_mp_percent");
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
                RecoveryState = MateRecoveryState.FromPersisted(
                    reviveDelay,
                    reviveHpPercent,
                    reviveMpPercent),
                UpdatedAt = reader.GetDateTime("updated_at"),
                CreatedAt = reader.GetDateTime("created_at")
            };
            lock (_saveSync)
                _mates.Add(template.ItemId, template);
        }
    }

    private static int? NullableInt32(DbDataReader reader, string columnName)
    {
        return reader.IsDBNull(reader.GetOrdinal(columnName))
            ? null
            : reader.GetInt32(columnName);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public void Save(DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        MateDb[] mateSnapshot;
        Dictionary<MateDbKey, long> removalSnapshot;
        lock (_saveSync)
        {
            ThrowIfCommitGateActive();
            mateSnapshot = _mates.Values.ToArray();
            removalSnapshot = _removedMates.ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        var token = new CharacterMatesSaveToken(removalSnapshot);
        _saveTokens.Remove(transaction);
        _saveTokens.Add(transaction, token);

        try
        {
            PersistSnapshot(connection, transaction, mateSnapshot, removalSnapshot.Keys);
        }
        catch
        {
            _saveTokens.Remove(transaction);
            throw;
        }
    }

    /// <summary>
    /// Flushes state changes made after <see cref="Save"/> but before the outer transaction commits.
    /// The short commit gate prevents further mate mutations between this final snapshot and commit.
    /// </summary>
    public void PrepareSaveCommit(DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!_saveTokens.TryGetValue(transaction, out var token))
            return;

        MateDb[] mateSnapshot;
        Dictionary<MateDbKey, long> removalSnapshot;
        lock (_saveSync)
        {
            ThrowIfCommitGateActive();
            mateSnapshot = _mates.Values.ToArray();
            removalSnapshot = _removedMates.ToDictionary(pair => pair.Key, pair => pair.Value);
            token.RemovedVersions = removalSnapshot;
            _commitGates.Add(transaction);
        }

        try
        {
            PersistSnapshot(connection, transaction, mateSnapshot, removalSnapshot.Keys);
        }
        catch
        {
            lock (_saveSync)
                _commitGates.Remove(transaction);
            throw;
        }
    }

    public void ConfirmSave(DbTransaction transaction)
    {
        if (transaction == null)
            return;

        _saveTokens.TryGetValue(transaction, out var token);
        _saveTokens.Remove(transaction);
        lock (_saveSync)
        {
            _commitGates.Remove(transaction);
            if (token == null)
                return;

            foreach (var removal in token.RemovedVersions)
            {
                if (_removedMates.TryGetValue(removal.Key, out var currentVersion) &&
                    currentVersion == removal.Value)
                {
                    _removedMates.Remove(removal.Key);
                }
            }
        }
    }

    public void DiscardSave(DbTransaction transaction)
    {
        if (transaction == null)
            return;

        _saveTokens.Remove(transaction);
        lock (_saveSync)
            _commitGates.Remove(transaction);
    }

    private void PersistSnapshot(
        DbConnection connection,
        DbTransaction transaction,
        IEnumerable<MateDb> mateSnapshot,
        IEnumerable<MateDbKey> removalSnapshot)
    {
        foreach (var removedMate in removalSnapshot)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "DELETE FROM mates WHERE id = @id AND item_id = @item_id AND owner = @owner";
            AddParameter(command, "@id", removedMate.Id);
            AddParameter(command, "@item_id", removedMate.ItemId);
            AddParameter(command, "@owner", removedMate.Owner);
            command.Prepare();
            command.ExecuteNonQuery();
        }

        foreach (var value in mateSnapshot)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText =
                "REPLACE INTO mates(`id`,`item_id`,`name`,`xp`,`level`,`mileage`,`hp`,`mp`,`owner`," +
                "`mate_revive_delay`,`mate_revive_hp_percent`,`mate_revive_mp_percent`,`updated_at`,`created_at`) " +
                "VALUES (@id, @item_id, @name, @xp, @level, @mileage, @hp, @mp, @owner," +
                "@mate_revive_delay,@mate_revive_hp_percent,@mate_revive_mp_percent,@updated_at,@created_at)";
            AddParameter(command, "@id", value.Id);
            AddParameter(command, "@item_id", value.ItemId);
            AddParameter(command, "@name", value.Name);
            AddParameter(command, "@xp", value.Xp);
            AddParameter(command, "@level", value.Level);
            AddParameter(command, "@mileage", value.Mileage);
            AddParameter(command, "@hp", value.Hp);
            AddParameter(command, "@mp", value.Mp);
            AddParameter(command, "@owner", value.Owner);
            AddParameter(command, "@mate_revive_delay",
                value.RecoveryState?.MateReviveDelay ?? (object)DBNull.Value);
            AddParameter(command, "@mate_revive_hp_percent",
                value.RecoveryState?.MateReviveHpPercent ?? (object)DBNull.Value);
            AddParameter(command, "@mate_revive_mp_percent",
                value.RecoveryState?.MateReviveMpPercent ?? (object)DBNull.Value);
            AddParameter(command, "@updated_at", value.UpdatedAt);
            AddParameter(command, "@created_at", value.CreatedAt);
            command.ExecuteNonQuery();
        }
    }

    private void ThrowIfCommitGateActive()
    {
        if (_commitGates.Count > 0)
        {
            throw new InvalidOperationException(
                "Owned mate state cannot change while its save transaction is being committed.");
        }
    }

    private sealed class CharacterMatesSaveToken(IReadOnlyDictionary<MateDbKey, long> removedVersions)
    {
        public IReadOnlyDictionary<MateDbKey, long> RemovedVersions { get; set; } = removedVersions;
    }

    private readonly record struct MateDbKey(uint Id, ulong ItemId, uint Owner)
    {
        public static MateDbKey From(MateDb mate) => new(mate.Id, mate.ItemId, mate.Owner);
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
    public MateRecoveryState? RecoveryState { get; set; }
    public uint Owner { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public MateDb Clone() => (MateDb)MemberwiseClone();
}
