using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// A character's equip slot reinforcement progress: one level and one experience bar per slot, plus the
/// level effect each slot runs. The ladder itself is content
/// (<see cref="EquipSlotReinforceGameData"/>); this is where the character stands on it, and it is what
/// the client's reinforcement window reads — the window is filled by the per-slot update packet and by
/// nothing else, so it is replayed in full at world entry.
/// </summary>
public class CharacterEquipSlotReinforces
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Character _owner;
    private readonly Dictionary<byte, EquipSlotReinforceState> _states = [];
    private readonly Lock _sync = new();

    public CharacterEquipSlotReinforces(Character owner)
    {
        _owner = owner;
    }

    /// <summary>Every slot the character has progress on. Slots never fed are absent and read as zero.</summary>
    public List<EquipSlotReinforceState> States
    {
        get
        {
            lock (_sync)
                return _states.Values.Select(state => state.Clone()).ToList();
        }
    }

    public EquipSlotReinforceState StateOf(byte slotTypeId)
    {
        lock (_sync)
            return _states.TryGetValue(slotTypeId, out var state) ? state : null;
    }

    /// <summary>
    /// The slot's progress record, created on first use. Slots that have never been fed are level 0
    /// with an empty bar, which is exactly what the client shows for them.
    /// </summary>
    private EquipSlotReinforceState GetOrCreate(byte slotTypeId)
    {
        lock (_sync)
        {
            if (_states.TryGetValue(slotTypeId, out var state))
                return state;

            state = new EquipSlotReinforceState { SlotTypeId = slotTypeId };
            _states[slotTypeId] = state;
            return state;
        }
    }

    public void Load(MySqlConnection connection)
    {
        if (_owner == null)
            return;

        try
        {
            var loaded = new Dictionary<byte, EquipSlotReinforceState>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT `slot_type_id`, `level`, `exp`, `level_effect_index` " +
                    "FROM character_equip_slot_reinforces WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", _owner.Id);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var slotTypeId = reader.GetByte("slot_type_id");
                    loaded[slotTypeId] = new EquipSlotReinforceState
                    {
                        SlotTypeId = slotTypeId,
                        Level = reader.GetSByte("level"),
                        Exp = reader.GetInt32("exp"),
                        LevelEffectIndex = reader.GetInt32("level_effect_index")
                    };
                }
            }

            lock (_sync)
            {
                _states.Clear();
                foreach (var (slotTypeId, state) in loaded)
                    _states[slotTypeId] = state;
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to load equip slot reinforces for {0}", _owner.Name);
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (_owner == null)
            return;

        List<EquipSlotReinforceState> snapshot;
        lock (_sync)
            snapshot = _states.Values.Select(state => state.Clone()).ToList();

        try
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM character_equip_slot_reinforces WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", _owner.Id);
                command.ExecuteNonQuery();
            }

            foreach (var state in snapshot)
            {
                using var command = connection.CreateCommand();
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT INTO character_equip_slot_reinforces " +
                    "(`owner`, `slot_type_id`, `level`, `exp`, `level_effect_index`) " +
                    "VALUES (@owner, @slot, @level, @exp, @effect)";
                command.Parameters.AddWithValue("@owner", _owner.Id);
                command.Parameters.AddWithValue("@slot", state.SlotTypeId);
                command.Parameters.AddWithValue("@level", state.Level);
                command.Parameters.AddWithValue("@exp", state.Exp);
                command.Parameters.AddWithValue("@effect", state.LevelEffectIndex);
                command.ExecuteNonQuery();
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to save equip slot reinforces for {0}", _owner.Name);
        }
    }

    /// <summary>
    /// Replays every slot of every ladder. The client's window has no other source for these numbers,
    /// so a character entering the world without this sees a window full of zeroes.
    /// </summary>
    public void SendAll()
    {
        foreach (var slotTypeId in EquipSlotReinforceGameData.Instance.SlotTypeIds)
        {
            Send(slotTypeId);
            SendLevelEffect(slotTypeId);
        }
    }

    /// <summary>Publishes one slot's level and bar.</summary>
    public void Send(byte slotTypeId)
    {
        var state = StateOf(slotTypeId);
        var level = state?.Level ?? 0;
        var exp = state?.Exp ?? 0;
        _owner.SendPacket(new SCEquipSlotReinforceUpdatePacket(_owner.ObjId, slotTypeId, level, exp));
    }

    /// <summary>
    /// Publishes the level effect the slot is running, when it has one. A slot that has not reached a
    /// trigger level yet has nothing to announce, and its stored choice stays untouched.
    /// </summary>
    public void SendLevelEffect(byte slotTypeId)
    {
        var state = StateOf(slotTypeId);
        if (state == null)
            return;

        var eligible = EquipSlotReinforceRules.EligibleLevelEffects(slotTypeId, state.Level,
            EquipSlotReinforceGameData.Instance.LevelEffects);
        if (eligible.Count == 0)
            return;

        var index = EquipSlotReinforceRules.NormalizeLevelEffectIndex(state.LevelEffectIndex, eligible.Count);
        state.LevelEffectIndex = index;
        _owner.SendPacket(new SCEquipSlotReinforceLevelEffectUpdatePacket(_owner.ObjId, slotTypeId, state.Level,
            eligible[index].Id));
    }

    /// <summary>
    /// Overwrites one slot's level and bar. The bar is capped at what the level being worked towards
    /// can hold, so a set can never leave a slot able to level twice on one bar.
    /// </summary>
    public bool SetProgress(byte slotTypeId, sbyte level, int exp, out string error)
    {
        error = null;
        var ladder = EquipSlotReinforceGameData.Instance.Ladder(slotTypeId);
        if (ladder.Count == 0)
        {
            error = $"slot {slotTypeId} has no reinforcement ladder";
            return false;
        }

        if (level < 0 || level > ladder[^1].Level)
        {
            error = $"slot {slotTypeId} has no level {level} (0..{ladder[^1].Level})";
            return false;
        }

        var next = EquipSlotReinforceRules.NextStep(level, ladder);
        var capped = next == null ? 0 : Math.Clamp(exp, 0, next.NeedExp);

        lock (_sync)
        {
            var state = GetOrCreate(slotTypeId);
            state.Level = level;
            state.Exp = capped;
        }

        Send(slotTypeId);
        SendLevelEffect(slotTypeId);
        return true;
    }

    /// <summary>
    /// Banks a feed into a slot's bar. The material itself — which item it draws from and what it costs
    /// — belongs to the request that carries it; this only records what the feed was worth.
    /// </summary>
    public EquipSlotReinforceChange Feed(byte slotTypeId, int gainExp)
    {
        var ladder = EquipSlotReinforceGameData.Instance.Ladder(slotTypeId);
        if (ladder.Count == 0)
            return EquipSlotReinforceChange.Refused;

        EquipSlotReinforceChange change;
        EquipSlotReinforceState state;
        lock (_sync)
        {
            state = GetOrCreate(slotTypeId);
            change = EquipSlotReinforceRules.ApplyExp(state, ladder, gainExp);
        }

        if (change is EquipSlotReinforceChange.ExpGained or EquipSlotReinforceChange.ExpCapped)
            Send(slotTypeId);

        return change;
    }

    /// <summary>
    /// Takes the next level when the slot's bar is full, spending the item that step costs. Reports
    /// <see cref="EquipSlotReinforceChange.Refused"/> when the bar is not full, the ladder ends there,
    /// or the character does not hold the item.
    /// </summary>
    public EquipSlotReinforceChange LevelUp(byte slotTypeId)
    {
        var ladder = EquipSlotReinforceGameData.Instance.Ladder(slotTypeId);
        if (ladder.Count == 0)
            return EquipSlotReinforceChange.Refused;

        EquipSlotReinforceStep reached;
        EquipSlotReinforceChange change;
        lock (_sync)
        {
            var state = GetOrCreate(slotTypeId);

            // Check the price before mutating: a refused level-up must leave the bar exactly as it was.
            var next = EquipSlotReinforceRules.NextStep(state.Level, ladder);
            if (!EquipSlotReinforceRules.CanLevelUp(state.Level, state.Exp, next))
                return EquipSlotReinforceChange.Refused;

            if (next.LevelUpItemCount > 0)
            {
                var carried = _owner.Inventory.GetItemsCount(next.LevelUpItemId);
                if (carried < next.LevelUpItemCount)
                {
                    Logger.Warn("Equip slot reinforce {0}: {1} needs {2} x item {3} but carries {4}",
                        slotTypeId, _owner.Name, next.LevelUpItemCount, next.LevelUpItemId, carried);
                    return EquipSlotReinforceChange.Refused;
                }

                var consumed = _owner.Inventory.ConsumeItem([SlotType.Inventory], ItemTaskType.EquipSlotReinforce,
                    next.LevelUpItemId, next.LevelUpItemCount, null);
                if (consumed < next.LevelUpItemCount)
                {
                    Logger.Warn("Equip slot reinforce {0}: {1} consumed {2} of {3} x item {4}",
                        slotTypeId, _owner.Name, consumed, next.LevelUpItemCount, next.LevelUpItemId);
                    return EquipSlotReinforceChange.Refused;
                }
            }

            change = EquipSlotReinforceRules.TryLevelUp(state, ladder, out reached);
        }

        if (change != EquipSlotReinforceChange.LeveledUp)
            return change;

        Logger.Info("Equip slot reinforce {0}: {1} reached level {2} (item level +{3})",
            slotTypeId, _owner.Name, reached.Level, reached.GainItemLevel);

        Send(slotTypeId);
        SendLevelEffect(slotTypeId);
        return change;
    }

    /// <summary>
    /// Switches a slot to another of the level effects its level has unlocked. The index is a position
    /// in that eligible list, which is the ordering the client shows.
    /// </summary>
    public bool SetLevelEffect(byte slotTypeId, int levelEffectIndex)
    {
        var state = StateOf(slotTypeId);
        if (state == null)
            return false;

        var eligible = EquipSlotReinforceRules.EligibleLevelEffects(slotTypeId, state.Level,
            EquipSlotReinforceGameData.Instance.LevelEffects);
        if (levelEffectIndex < 0 || levelEffectIndex >= eligible.Count)
            return false;

        state.LevelEffectIndex = levelEffectIndex;
        _owner.SendPacket(new SCEquipSlotReinforceLevelEffectUpdatePacket(_owner.ObjId, slotTypeId, state.Level,
            eligible[levelEffectIndex].Id));
        return true;
    }

    /// <summary>
    /// Writes the reinforcement block of a unit state: the level and bar of every slot that has
    /// progress, then the level-effect choices. The client's reinforcement window is filled from this
    /// block, so a character carries its progress in the unit state itself.
    /// </summary>
    public void WriteInfos(PacketStream stream)
    {
        List<EquipSlotReinforceState> states;
        lock (_sync)
        {
            states = _states.Values
                .Where(state => state.Level > 0 || state.Exp > 0)
                .OrderBy(state => state.SlotTypeId)
                .Select(state => state.Clone())
                .ToList();
        }

        WriteSlotInfos(stream, states);
    }

    /// <summary>
    /// The slot list on its own, so the wire shape can be pinned without a character behind it: a count
    /// of slots, then each slot's id, level and bar. Slots that were never fed are left out.
    /// </summary>
    public static void WriteSlotInfos(PacketStream stream, IEnumerable<EquipSlotReinforceState> states)
    {
        var ordered = states?.Where(state => state is { Level: > 0 } || state is { Exp: > 0 })
            .OrderBy(state => state.SlotTypeId)
            .ToList() ?? [];

        stream.Write((uint)ordered.Count);
        foreach (var state in ordered)
        {
            stream.Write((int)state.SlotTypeId);
            stream.Write((byte)state.Level);
            stream.Write(state.Exp);
        }

        // The effect choices are keyed per (slot, level) on the wire, and the request that sets one is
        // not implemented yet, so the list goes out empty rather than invented.
        stream.Write(0u);
    }

    /// <summary>Total level across every slot that belongs to one attribute.</summary>
    public int AttributeTotal(EquipSlotReinforceAttribute attribute)    {
        return EquipSlotReinforceRules.AttributeTotal(attribute, States,
            slot => EquipSlotReinforceGameData.Instance.AttributeOf(slot));
    }

    /// <summary>Highest set effect level the attribute totals have unlocked.</summary>
    public byte SetEffectLevel(EquipSlotReinforceAttribute attribute)
    {
        return EquipSlotReinforceRules.SetEffectLevel(attribute, AttributeTotal(attribute),
            EquipSlotReinforceGameData.Instance.SetEffects);
    }

    /// <summary>Highest bundle effect level the three attribute totals have unlocked.</summary>
    public byte BundleEffectLevel()
    {
        return EquipSlotReinforceRules.BundleEffectLevel(
            AttributeTotal(EquipSlotReinforceAttribute.Offence),
            AttributeTotal(EquipSlotReinforceAttribute.Defence),
            AttributeTotal(EquipSlotReinforceAttribute.Support),
            EquipSlotReinforceGameData.Instance.BundleEffects);
    }
}
