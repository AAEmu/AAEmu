using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Utils.DB;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Skillsaver (ability set) state for one character. Client toast popups are driven by
/// <see cref="SCAbilitySetUpdatedPacket"/> / <see cref="SCAbilitySetSlotCountUpdatedPacket"/>.
/// Activation is finished by special effect <c>ActivateSavedAbilitySet</c> after skill 32189.
/// </summary>
public sealed class CharacterAbilitySets(Character owner)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>Retail client <c>MAX_ABILITY_SET_SLOTS</c> — hard cap after all expands (API dump = 5).</summary>
    public const byte MaxSlots = 5;

    /// <summary>Default unlocked slots for a new character.</summary>
    public const byte DefaultUsableSlots = 1;

    /// <summary>Skill cast when the client confirms a skillsaver apply (<c>갈무리 능력 활성</c>).</summary>
    public const uint ActivateSkillId = 32189;

    /// <summary>
    /// Retail expand seal (<c>갈무리 확장의 인장</c>). Same item as
    /// <c>bless_uthstin_expand_page_item_type</c>; client <c>GetExpandAbilitySetSlotInfo</c> uses it.
    /// </summary>
    public const uint ExpandItemId = 39559;

    /// <summary>
    /// Free activations before formula 42 gold applies. Compact
    /// <c>ability_set_free_activation_count</c> is currently 0 (client always shows gold).
    /// </summary>
    public static byte MaxFreeActivations =>
        (byte)Math.Clamp(AbilityChangeCosts.FreeActivationLimit, 0, byte.MaxValue);

    /// <summary>
    /// Pendant counts from compact <c>ability_set_slot_expand_item_need_cnt1..5</c> (shipped 1/2/4/4/4).
    /// Expanding from usable slot count N consumes entry <c>min(N, 5)</c> — matches client needCount.
    /// </summary>
    private static readonly int[] DefaultExpandNeedCounts = [1, 2, 4, 4, 4];

    private static int[] _expandNeedCounts;

    private readonly object _sync = new();
    private readonly Dictionary<byte, AbilitySetSlot> _slots = [];

    private Character Owner { get; } = owner;

    public byte UsableSlotCount { get; private set; } = DefaultUsableSlots;
    public byte UsedFreeActivationCount { get; private set; }

    /// <summary>Slot index waiting for skill 32189 / <c>ActivateSavedAbilitySet</c> to finish.</summary>
    public int PendingActivationSlot { get; private set; } = -1;

    public void SetPendingActivationSlot(int slotIndex)
    {
        lock (_sync)
            PendingActivationSlot = slotIndex;
    }

    public void ClearPendingActivationSlot()
    {
        lock (_sync)
            PendingActivationSlot = -1;
    }

    public bool TrySave(sbyte slotIndex)
    {
        // Skills UI defaults to the "current_use" combo row (index MAX+1) and sends
        // SaveAbilitySet(MAX_ABILITY_SET_SLOTS). Treat that as "next free usable slot".
        if (!TryResolveSaveSlot(slotIndex, out var slot))
        {
            Logger.Warn(
                "AbilitySet save {0}: no writable slot (requested={1}, usable={2}, occupied={3})",
                Owner.Name, slotIndex, UsableSlotCount, _slots.Count);
            return Fail(slot);
        }

        if (Owner.Ability1 is AbilityType.None or AbilityType.General ||
            Owner.Ability2 is AbilityType.None or AbilityType.General ||
            Owner.Ability3 is AbilityType.None or AbilityType.General)
        {
            Logger.Warn("AbilitySet save {0}: need three active abilities (slot {1})", Owner.Name, slot);
            return Fail(slot);
        }

        var snapshot = CaptureCurrent(slot);
        lock (_sync)
        {
            if (!TryPersistSlot(snapshot))
                return Fail(slot);

            _slots[slot] = snapshot;
        }

        SendUpdated(AbilitySetResponseType.Saved, slot);
        SendAllInfo();
        Logger.Info("AbilitySet save {0}: slot {1} ok (requested={2})", Owner.Name, slot, slotIndex);
        return true;
    }

    public bool TryDelete(sbyte slotIndex)
    {
        if (!TryValidateWritableSlot(slotIndex, out var slot))
            return Fail(slot);

        lock (_sync)
        {
            if (!_slots.ContainsKey(slot))
                return Fail(slot);

            if (!TryDeletePersistedSlot(slot))
                return Fail(slot);

            _slots.Remove(slot);
        }

        SendUpdated(AbilitySetResponseType.Deleted, slot);
        SendAllInfo();
        return true;
    }

    public bool TryExpand()
    {
        byte next;
        int needCount;
        lock (_sync)
        {
            if (UsableSlotCount >= MaxSlots)
                return false;

            needCount = GetExpandNeedCount(UsableSlotCount);
            if (needCount <= 0)
                return false;

            if (!Owner.Inventory.CheckItems(Items.SlotType.Inventory, ExpandItemId, needCount))
            {
                Owner.SendErrorMessage(ErrorMessageType.NotEnoughExpandItem);
                return false;
            }

            var consumed = Owner.Inventory.Bag.ConsumeItem(
                ItemTaskType.AbilityChange,
                ExpandItemId,
                needCount,
                null);
            if (consumed != needCount)
                return false;

            next = (byte)(UsableSlotCount + 1);
            UsableSlotCount = next;
        }

        // Persist immediately so a crash/relog does not roll back a consumed pendant.
        PersistCharacterColumns();
        Owner.SendPacket(new SCAbilitySetSlotCountUpdatedPacket((sbyte)next));
        return true;
    }

    /// <summary>
    /// Pendant cost to expand from <paramref name="currentUsableSlots"/> → +1.
    /// Reads <c>ability_set_slot_expand_item_need_cnt1..5</c>; steps beyond 5 reuse cnt5.
    /// </summary>
    public static int GetExpandNeedCount(byte currentUsableSlots)
    {
        var counts = _expandNeedCounts ??= LoadExpandNeedCounts();
        var step = Math.Clamp((int)currentUsableSlots, 1, counts.Length);
        return counts[step - 1];
    }

    private static int[] LoadExpandNeedCounts()
    {
        var counts = (int[])DefaultExpandNeedCounts.Clone();
        try
        {
            using var connection = SQLite.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT e.name, c.value FROM content_configs c " +
                "JOIN enum_content_configs e ON e.id = c.id " +
                "WHERE e.name LIKE 'ability_set_slot_expand_item_need_cnt%'";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var suffix = name[^1];
                if (suffix is < '1' or > '5')
                    continue;
                var idx = suffix - '1';
                counts[idx] = Convert.ToInt32(reader.GetValue(1));
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "AbilitySet expand need counts: using compact defaults 1/2/4/4/4");
        }

        return counts;
    }

    /// <summary>
    /// Applies the pending skillsaver after skill 32189 fires <c>ActivateSavedAbilitySet</c>.
    /// </summary>
    public bool TryActivatePending()
    {
        int pending;
        lock (_sync)
        {
            pending = PendingActivationSlot;
            PendingActivationSlot = -1;
        }

        if (pending < 0)
        {
            Logger.Warn("AbilitySet activate {0}: no pending slot", Owner.Name);
            return false;
        }

        return TryActivate((byte)pending);
    }

    public bool TryActivate(byte slot)
    {
        AbilitySetSlot saved;
        lock (_sync)
        {
            if (slot >= UsableSlotCount || !_slots.TryGetValue(slot, out saved) || !saved.IsOccupied)
                return Fail(slot);
        }

        AbilityType[] before = [Owner.Ability1, Owner.Ability2, Owner.Ability3];
        AbilityType[] after = [saved.Ability1, saved.Ability2, saved.Ability3];

        // Same triad already equipped — do not wipe/relearn (looks like a refresh) or toast "changed".
        if (before[0] == after[0] && before[1] == after[1] && before[2] == after[2])
        {
            Logger.Info(
                "AbilitySet activate {0}: slot {1} already {2}/{3}/{4} — skip swap",
                Owner.Name, slot, before[0], before[1], before[2]);
            return true;
        }

        if (!TryChargeActivation(slot))
            return false;

        // Server-side wipe first (no SCSkillsReset). SCAbilitySwapped must not be followed by
        // reset spam or the learn-ability banner queue is cancelled.
        foreach (var oldAbility in before.Distinct())
        {
            if (oldAbility is AbilityType.None or AbilityType.General)
                continue;
            Owner.Skills.Reset(oldAbility, notifyClient: false);
            if (Owner.Abilities.Abilities.TryGetValue(oldAbility, out var ability))
                ability.Order = 255;
        }

        Owner.Ability1 = saved.Ability1;
        Owner.Ability2 = saved.Ability2;
        Owner.Ability3 = saved.Ability3;
        Owner.Abilities.SetAbility(saved.Ability1, 0);
        Owner.Abilities.SetAbility(saved.Ability2, 1);
        Owner.Abilities.SetAbility(saved.Ability3, 2);

        ApplySnapshotSkills(saved);

        // Retail skillsaver activate: 0x147 (sheet + client wipe of old trees), then
        // SCAbilitySetUpdated(Changed). The client silently re-learns skills from its
        // savedAbilitySets snapshot (filled by AllInfo / save) — do NOT ResendLearned
        // (each SCSkillLearned posts "Learned …" chat). Full triad 0x147 also skips
        // ABILITY_CHANGED (msg_swap_ability / learn-ability banner); that event only
        // fires when a single leading news[] ability is valid.
        Owner.BroadcastPacket(new SCAbilitySwappedPacket(Owner.ObjId, before, after), true);
        SendUpdated(AbilitySetResponseType.Changed, slot);
        Logger.Info(
            "AbilitySet activate {0}: slot {1} {2}/{3}/{4} → {5}/{6}/{7} (skills={8})",
            Owner.Name, slot,
            before[0], before[1], before[2],
            after[0], after[1], after[2],
            saved.SkillIds.Count);
        return true;
    }

    public void Load(MySqlConnection connection)
    {
        try
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT `usable_abil_set_slot_count`, `used_free_abil_set_activation` FROM characters WHERE `id` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    UsableSlotCount = reader.IsDBNull(0)
                        ? DefaultUsableSlots
                        : Math.Clamp(reader.GetByte(0), (byte)1, MaxSlots);
                    UsedFreeActivationCount = reader.IsDBNull(1) ? (byte)0 : reader.GetByte(1);
                }
            }

            _slots.Clear();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT `slot`, `ability1`, `ability2`, `ability3` FROM ability_sets WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var slot = new AbilitySetSlot
                    {
                        SlotIndex = reader.GetByte("slot"),
                        Ability1 = (AbilityType)reader.GetByte("ability1"),
                        Ability2 = (AbilityType)reader.GetByte("ability2"),
                        Ability3 = (AbilityType)reader.GetByte("ability3")
                    };
                    _slots[slot.SlotIndex] = slot;
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT `slot`, `skill_id`, `is_passive` FROM ability_set_skills WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var slotIndex = reader.GetByte("slot");
                    if (!_slots.TryGetValue(slotIndex, out var slot))
                        continue;

                    var skillId = reader.GetUInt32("skill_id");
                    if (reader.GetBoolean("is_passive"))
                        slot.PassiveBuffIds.Add(skillId);
                    else
                        slot.SkillIds.Add(skillId);
                }
            }
        }
        catch (MySqlException ex)
        {
            // Schema update not applied yet — keep defaults so login still works.
            Logger.Warn(ex, "AbilitySet load skipped for {0}", Owner.Name);
            UsableSlotCount = DefaultUsableSlots;
            UsedFreeActivationCount = 0;
            _slots.Clear();
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;
            command.CommandText =
                "UPDATE characters SET `usable_abil_set_slot_count` = @usable, `used_free_abil_set_activation` = @usedFree WHERE `id` = @owner";
            command.Parameters.AddWithValue("@usable", UsableSlotCount);
            command.Parameters.AddWithValue("@usedFree", UsedFreeActivationCount);
            command.Parameters.AddWithValue("@owner", Owner.Id);
            try
            {
                command.ExecuteNonQuery();
            }
            catch (MySqlException ex)
            {
                // Column may be missing until the SQL update is applied — keep gameplay working.
                Logger.Warn(ex, "AbilitySet character column save skipped for {0}", Owner.Name);
            }
        }

        // Slots are written immediately on save/delete; nothing else to flush here.
    }

    public void SendSlotCount()
    {
        Owner.SendPacket(new SCAbilitySetSlotCountUpdatedPacket((sbyte)UsableSlotCount));
    }

    /// <summary>
    /// Push the full skillsaver list so <c>GetSavedAbilitySets</c> works after login/relog.
    /// Without this the UI looks empty after a World restart even when MySQL still has rows.
    /// </summary>
    public void SendAllInfo()
    {
        AbilitySetSlot[] snapshot;
        byte usedFree;
        lock (_sync)
        {
            snapshot = _slots.Values.OrderBy(s => s.SlotIndex).ToArray();
            usedFree = UsedFreeActivationCount;
        }
        Owner.SendPacket(new SCAbilitySetAllInfoPacket(snapshot, usedFree));
    }

    /// <summary>
    /// Offline catch-up when <see cref="FeaturesManager.Fsets.AbilitySetFreeActivationDailyReset"/> is on.
    /// Uses <c>leave_time</c> like <see cref="CharacterQuests.CheckDailyResetAtLogin"/>.
    /// </summary>
    public void CheckDailyResetAtLogin()
    {
        if (!FeaturesManager.Fsets.AbilitySetFreeActivationDailyReset)
            return;

        var leaveUtc = ServerCalendar.AsUtc(Owner.LeaveTime);
        if (leaveUtc.Date >= ServerCalendar.TodayUtc)
            return;

        ResetFreeActivationCount(syncClient: false);
    }

    /// <summary>
    /// Clears the persisted free-activation counter for a new UTC day.
    /// </summary>
    public void ResetFreeActivationCount(bool syncClient)
    {
        lock (_sync)
        {
            if (UsedFreeActivationCount == 0)
                return;

            UsedFreeActivationCount = 0;
        }

        PersistCharacterColumns();
        if (syncClient)
            SendAllInfo();
    }

    private bool TryChargeActivation(byte slot)
    {
        lock (_sync)
        {
            if (UsedFreeActivationCount < MaxFreeActivations)
            {
                UsedFreeActivationCount++;
                PersistCharacterColumns();
                return true;
            }
        }

        // ChangeMoney already sends NotEnoughMoney on failure.
        if (!AbilityChangeCosts.TryChargeSwapAbilitySet(Owner))
            return Fail(slot);

        return true;
    }

    private AbilitySetSlot CaptureCurrent(byte slot)
    {
        var snapshot = new AbilitySetSlot
        {
            SlotIndex = slot,
            Ability1 = Owner.Ability1,
            Ability2 = Owner.Ability2,
            Ability3 = Owner.Ability3
        };

        foreach (var skill in Owner.Skills.Skills.Values)
        {
            var abilityId = skill.Template?.AbilityId ?? AbilityType.General;
            if (abilityId == snapshot.Ability1 || abilityId == snapshot.Ability2 || abilityId == snapshot.Ability3)
                snapshot.SkillIds.Add(skill.Id);
        }

        foreach (var buff in Owner.Skills.PassiveBuffs.Values)
        {
            var abilityId = buff.Template?.AbilityId ?? AbilityType.General;
            if (abilityId == snapshot.Ability1 || abilityId == snapshot.Ability2 || abilityId == snapshot.Ability3)
                snapshot.PassiveBuffIds.Add(buff.Id);
        }

        return snapshot;
    }

    private void ApplySnapshotSkills(AbilitySetSlot saved)
    {
        foreach (var skillId in saved.SkillIds)
        {
            if (Owner.Skills.Skills.ContainsKey(skillId))
                continue;
            var template = SkillManager.Instance.GetSkillTemplate(skillId);
            if (template != null)
                Owner.Skills.AddSkill(template, 1, false);
        }

        foreach (var buffId in saved.PassiveBuffIds)
        {
            if (Owner.Skills.PassiveBuffs.ContainsKey(buffId))
                continue;
            // Silent: SCBuffLearned → client "Learned …" chat; skillsaver restore uses AbilitySetUpdated.
            Owner.Skills.AddBuff(buffId, notify: false);
        }
    }

    private bool TryValidateWritableSlot(sbyte slotIndex, out byte slot)
    {
        slot = 0;
        if (slotIndex < 0 || slotIndex >= MaxSlots)
            return false;
        slot = (byte)slotIndex;
        return slot < UsableSlotCount;
    }

    /// <summary>
    /// Resolves the slot to write. Explicit indices 0..usable-1 are used as-is (overwrite allowed).
    /// Indices outside that range (notably <see cref="MaxSlots"/> from the "current_use" row) pick
    /// the first empty usable slot.
    /// </summary>
    private bool TryResolveSaveSlot(sbyte slotIndex, out byte slot)
    {
        slot = 0;
        lock (_sync)
        {
            if (slotIndex >= 0 && slotIndex < UsableSlotCount)
            {
                slot = (byte)slotIndex;
                return true;
            }

            // Auto-pick: current_use sentinel, or any out-of-range request when a free slot exists.
            for (byte i = 0; i < UsableSlotCount; i++)
            {
                if (_slots.TryGetValue(i, out var existing) && existing.IsOccupied)
                    continue;
                slot = i;
                return true;
            }
        }

        return false;
    }

    private void PersistCharacterColumns()
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE characters SET `usable_abil_set_slot_count` = @usable, `used_free_abil_set_activation` = @usedFree WHERE `id` = @owner";
            command.Parameters.AddWithValue("@usable", UsableSlotCount);
            command.Parameters.AddWithValue("@usedFree", UsedFreeActivationCount);
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "AbilitySet column persist failed for {0}", Owner.Name);
        }
    }

    private bool Fail(byte slot)
    {
        SendUpdated(AbilitySetResponseType.Failed, slot);
        return false;
    }

    private void SendUpdated(AbilitySetResponseType responseType, byte slot)
    {
        Owner.SendPacket(new SCAbilitySetUpdatedPacket(
            responseType: (sbyte)responseType,
            slotIndex: slot,
            usedFreeActivationCount: (sbyte)UsedFreeActivationCount));
    }

    private bool TryPersistSlot(AbilitySetSlot snapshot)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText =
                    "REPLACE INTO ability_sets(`owner`,`slot`,`ability1`,`ability2`,`ability3`) VALUES (@owner,@slot,@a1,@a2,@a3)";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Parameters.AddWithValue("@slot", snapshot.SlotIndex);
                command.Parameters.AddWithValue("@a1", (byte)snapshot.Ability1);
                command.Parameters.AddWithValue("@a2", (byte)snapshot.Ability2);
                command.Parameters.AddWithValue("@a3", (byte)snapshot.Ability3);
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM ability_set_skills WHERE `owner` = @owner AND `slot` = @slot";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Parameters.AddWithValue("@slot", snapshot.SlotIndex);
                command.ExecuteNonQuery();
            }

            foreach (var skillId in snapshot.SkillIds)
                InsertSkillRow(connection, transaction, snapshot.SlotIndex, skillId, isPassive: false);
            foreach (var buffId in snapshot.PassiveBuffIds)
                InsertSkillRow(connection, transaction, snapshot.SlotIndex, buffId, isPassive: true);

            transaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "AbilitySet persist failed for {0} slot {1}", Owner.Name, snapshot.SlotIndex);
            return false;
        }
    }

    private void InsertSkillRow(MySqlConnection connection, MySqlTransaction transaction, byte slot, uint skillId, bool isPassive)
    {
        using var command = connection.CreateCommand();
        command.Connection = connection;
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO ability_set_skills(`owner`,`slot`,`skill_id`,`is_passive`) VALUES (@owner,@slot,@skill,@passive)";
        command.Parameters.AddWithValue("@owner", Owner.Id);
        command.Parameters.AddWithValue("@slot", slot);
        command.Parameters.AddWithValue("@skill", skillId);
        command.Parameters.AddWithValue("@passive", isPassive);
        command.ExecuteNonQuery();
    }

    private bool TryDeletePersistedSlot(byte slot)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM ability_set_skills WHERE `owner` = @owner AND `slot` = @slot";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Parameters.AddWithValue("@slot", slot);
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM ability_sets WHERE `owner` = @owner AND `slot` = @slot";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Parameters.AddWithValue("@slot", slot);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "AbilitySet delete failed for {0} slot {1}", Owner.Name, slot);
            return false;
        }
    }
}
