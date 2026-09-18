using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// A character's equip slot reinforcement progress: one level and one experience bar per slot, plus the
/// artifact effects the slots have obtained. The ladder itself is content
/// (<see cref="EquipSlotReinforceGameData"/>); this is where the character stands on it, and it is what
/// the client's reinforcement window reads — the window is filled by the per-slot update packet and by
/// nothing else, so it is replayed in full at world entry.
/// </summary>
public class CharacterEquipSlotReinforces
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Character _owner;
    private readonly Dictionary<byte, EquipSlotReinforceState> _states = [];

    /// <summary>
    /// The artifact effects the character has obtained, keyed by the pair that identifies one: the slot and
    /// the tier it came from. A tier hands out one row, and it never hands out the same row twice.
    /// </summary>
    private readonly Dictionary<(byte SlotTypeId, uint LevelEffectId), EquipSlotReinforceEffect> _effects = [];

    private readonly Lock _sync = new();

    /// <summary>
    /// The pair a running artifact cast carried, spent when that cast lands.
    /// </summary>
    private (byte SlotTypeId, uint MaterialRowId)? _queuedWindowFeed;

    /// <summary>The slot and tier a running Replace cast carried, spent when that cast lands.</summary>
    private (byte SlotTypeId, ushort TriggerLevel)? _queuedEffectReplace;

    /// <summary>
    /// The skill the artifact window's Replace button casts ("equip slot reinforcement effect replace"). Its
    /// cast carries the slot and the tier to re-roll, which is the only place either arrives.
    /// </summary>
    public const uint ReplaceEffectSkillId = 38664;

    /// <summary>
    /// The skill the artifact window's Confirm button casts ("equip slot reinforcement"). Its tail names the
    /// material row; other skills can carry a 6-byte tail that must not be read as one.
    /// </summary>
    public const uint FeedSkillId = 38363;

    /// <summary>
    /// The <c>content_configs</c> row naming the item Replace mode spends
    /// (<c>enum_content_configs</c> id 236, shipped value 46682, the "Bound Serendipity Stone" the window's
    /// own Replace dialog shows). The cast does not carry the item, so this is where its price comes from,
    /// and a row that is missing refuses the replace loudly rather than spending a guessed item.
    /// </summary>
    public const string ChangeEffectItemConfigName = "equip_slot_reinforce_change_level_effect_item";

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

    /// <summary>Every artifact effect the character has obtained, ordered by slot and then by tier.</summary>
    public List<EquipSlotReinforceEffect> Effects
    {
        get
        {
            lock (_sync)
                return _effects.Values
                    .OrderBy(effect => effect.SlotTypeId)
                    .ThenBy(effect => effect.LevelEffectId)
                    .Select(effect => effect.Clone())
                    .ToList();
        }
    }

    /// <summary>One slot's obtained effect for one tier, or null when that tier never handed it one.</summary>
    public EquipSlotReinforceEffect EffectOf(byte slotTypeId, uint levelEffectId)
    {
        lock (_sync)
            return _effects.TryGetValue((slotTypeId, levelEffectId), out var effect) ? effect.Clone() : null;
    }

    /// <summary>
    /// The modifier rows the character's slots are running, which is what the gear bonus pass adds to the
    /// character. Effects the player switched off are left out.
    /// </summary>
    public List<EquipSlotReinforceUnitModifier> AppliedModifiers
    {
        get
        {
            var data = EquipSlotReinforceGameData.Instance;
            lock (_sync)
                return _effects.Values
                    .Where(effect => effect.Applied)
                    .Select(effect => data.GetUnitModifierById(effect.UnitModifierId))
                    .Where(modifier => modifier != null)
                    .ToList();
        }
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
                    "SELECT `slot_type_id`, `level`, `exp` " +
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
                        Exp = reader.GetInt32("exp")
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

        LoadEffects(connection);

        // Everything a slot is owed is rolled here, before the client is told anything: the unit state carries
        // the result, and an effect announced later would prompt instead of filling the window.
        EnsureAllTierEffects();
    }

    /// <summary>
    /// Rolls every tier every slot has reached, without announcing anything. Runs once the character is loaded,
    /// and again is harmless: a tier that already handed out an effect is left alone.
    /// </summary>
    private void EnsureAllTierEffects()
    {
        foreach (var slotTypeId in EquipSlotReinforceGameData.Instance.SlotTypeIds)
            EnsureTierEffects(slotTypeId, announce: false);
    }

    /// <summary>
    /// Loads the artifact effects the character obtained. Kept apart from the levels so a character whose
    /// rows predate the effects still gets its levels when this read fails.
    /// </summary>
    private void LoadEffects(MySqlConnection connection)
    {
        try
        {
            var loaded = new Dictionary<(byte SlotTypeId, uint LevelEffectId), EquipSlotReinforceEffect>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT `slot_type_id`, `level_effect_id`, `unit_modifier_id`, `applied` " +
                    "FROM character_equip_slot_reinforce_effects WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", _owner.Id);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var effect = new EquipSlotReinforceEffect
                    {
                        SlotTypeId = reader.GetByte("slot_type_id"),
                        LevelEffectId = reader.GetUInt32("level_effect_id"),
                        UnitModifierId = reader.GetUInt32("unit_modifier_id"),
                        Applied = reader.GetBoolean("applied")
                    };

                    loaded[(effect.SlotTypeId, effect.LevelEffectId)] = effect;
                }
            }

            lock (_sync)
            {
                _effects.Clear();
                foreach (var (key, effect) in loaded)
                    _effects[key] = effect;
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to load equip slot reinforce effects for {0}", _owner.Name);
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (_owner == null)
            return;

        List<EquipSlotReinforceState> snapshot;
        List<EquipSlotReinforceEffect> effects;
        lock (_sync)
        {
            snapshot = _states.Values.Select(state => state.Clone()).ToList();
            effects = _effects.Values.Select(effect => effect.Clone()).ToList();
        }

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
                    "(`owner`, `slot_type_id`, `level`, `exp`) " +
                    "VALUES (@owner, @slot, @level, @exp)";
                command.Parameters.AddWithValue("@owner", _owner.Id);
                command.Parameters.AddWithValue("@slot", state.SlotTypeId);
                command.Parameters.AddWithValue("@level", state.Level);
                command.Parameters.AddWithValue("@exp", state.Exp);
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM character_equip_slot_reinforce_effects WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", _owner.Id);
                command.ExecuteNonQuery();
            }

            foreach (var effect in effects)
            {
                using var command = connection.CreateCommand();
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT INTO character_equip_slot_reinforce_effects " +
                    "(`owner`, `slot_type_id`, `level_effect_id`, `unit_modifier_id`, `applied`) " +
                    "VALUES (@owner, @slot, @effect, @modifier, @applied)";
                command.Parameters.AddWithValue("@owner", _owner.Id);
                command.Parameters.AddWithValue("@slot", effect.SlotTypeId);
                command.Parameters.AddWithValue("@effect", effect.LevelEffectId);
                command.Parameters.AddWithValue("@modifier", effect.UnitModifierId);
                command.Parameters.AddWithValue("@applied", effect.Applied);
                command.ExecuteNonQuery();
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to save equip slot reinforces for {0}", _owner.Name);
        }
    }

    /// <summary>
    /// Replays every slot of every ladder: its level and bar, then the artifact effects it has obtained. The
    /// client's window has no other source for these numbers, so a character entering the world without this
    /// sees a window full of zeroes.
    /// </summary>
    /// <remarks>
    /// Nothing is rolled here: whatever a slot is owed was rolled when the character loaded, so that the unit
    /// state — which the client reads this block from, and which it does not re-read on a mid-session push —
    /// already carries it. Announcing an effect instead would open the client's "New Effect" prompt.
    /// </remarks>
    public void SendAll()
    {
        foreach (var slotTypeId in EquipSlotReinforceGameData.Instance.SlotTypeIds)
            Send(slotTypeId);
    }

    /// <summary>
    /// The level the client shows and indexes its material list by. The client's ladder is one-based
    /// (step N is what it costs to leave level N-1), so a character that has reached level L is working
    /// on step L+1 — and a character that has reached the top is clamped back onto the last step. The
    /// stored state stays in "levels reached" terms; this is the wire's view of it, and it is also the number
    /// the tier trigger levels are written against (see <see cref="EnsureTierEffects"/>).
    /// </summary>
    private static sbyte WireLevel(sbyte reachedLevel, byte slotTypeId)
    {
        var ladder = EquipSlotReinforceGameData.Instance.Ladder(slotTypeId);
        var wire = reachedLevel + 1;
        if (ladder.Count == 0)
            return (sbyte)wire;

        var max = ladder[^1].Level;
        return wire > max ? (sbyte)max : (sbyte)wire;
    }

    /// <summary>Publishes one slot's level and bar.</summary>
    public void Send(byte slotTypeId)
    {
        var state = StateOf(slotTypeId);
        var level = WireLevel(state?.Level ?? 0, slotTypeId);
        var exp = state?.Exp ?? 0;
        _owner.SendPacket(new SCEquipSlotReinforceUpdatePacket(_owner.ObjId, slotTypeId, level, exp));
    }

    /// <summary>
    /// Announces one obtained effect to the client. Both numbers are the client's own keys: the level is the
    /// tier's trigger level, because that is the level the window files the effect line under, and the id is the
    /// <c>equip_slot_reinforce_unit_modifiers</c> **row** — the stat itself, which is what the window prints
    /// (attribute and value). It is not the tier id: sending one of those made the client render a different
    /// slot's stat, which is how this was pinned.
    /// </summary>
    /// <remarks>
    /// This is the change path, not the state path: the client answers it with a "New Effect" prompt, so it is
    /// sent for the one effect an action just produced and never as a replay of everything a slot holds.
    /// </remarks>
    private void SendLevelEffect(byte slotTypeId, EquipSlotReinforceEffect effect)
    {
        if (effect == null || !effect.Applied)
            return;

        var tier = EquipSlotReinforceGameData.Instance.GetLevelEffectById(effect.LevelEffectId);
        if (tier == null)
            return;

        _owner.SendPacket(new SCEquipSlotReinforceLevelEffectUpdatePacket(_owner.ObjId, slotTypeId,
            (sbyte)tier.TriggerLevel, effect.UnitModifierId));
    }

    /// <summary>Announces that an effect the slot held is no longer applied — the window's "None".</summary>
    private void SendLevelEffectDeleted(byte slotTypeId, uint levelEffectId)
    {
        var tier = EquipSlotReinforceGameData.Instance.GetLevelEffectById(levelEffectId);
        if (tier == null)
            return;

        _owner.SendPacket(new SCEquipSlotReinforceLevelEffectDeletePacket(_owner.ObjId, slotTypeId,
            (sbyte)tier.TriggerLevel));
    }

    /// <summary>
    /// Rolls the effect for one tier of a slot: one of the tier's modifier rows, picked by weight and never
    /// one the character already obtained. Reports the row it rolled, or null when the tier had nothing left
    /// to hand out.
    /// </summary>
    public EquipSlotReinforceUnitModifier RollTierEffect(byte slotTypeId, uint levelEffectId)
    {
        var tier = EquipSlotReinforceGameData.Instance.GetLevelEffectById(levelEffectId);
        if (tier == null || tier.SlotTypeId != slotTypeId)
        {
            Logger.Warn("Equip slot reinforce {0}: no tier {1} to roll", slotTypeId, levelEffectId);
            return null;
        }

        EquipSlotReinforceUnitModifier rolled;
        lock (_sync)
        {
            rolled = EquipSlotReinforceRules.RollModifier(tier.Modifiers, IsObtained, Random.Shared.Next());
            if (rolled == null)
                return null;

            _effects[(slotTypeId, levelEffectId)] = new EquipSlotReinforceEffect
            {
                SlotTypeId = slotTypeId,
                LevelEffectId = levelEffectId,
                UnitModifierId = rolled.Id,
                Applied = true
            };
        }

        Logger.Info(
            "Equip slot reinforce {0}: {1} rolled artifact effect {2} of tier {3} (level {4} -> attribute {5} {6} {7})",
            slotTypeId, _owner.Name, rolled.Id, tier.Id, tier.TriggerLevel, rolled.UnitAttributeId,
            (UnitModifierType)rolled.UnitModifierTypeId, rolled.Value);

        return rolled;
    }

    /// <summary>
    /// Rolls every tier the slot's level has reached and that never handed the character an effect. Called when
    /// a level is gained, and again at world entry so a slot that reached its tier before this existed still
    /// gets the one effect it is owed.
    /// </summary>
    /// <remarks>
    /// The comparison runs against the client-facing level, not the stored one: a tier's <c>trigger_level</c> is
    /// the same "Artifact Level N" the window prints and the same number the window shows for the slot, so a
    /// ★5 tier lands the moment the slot reads level 5. The support ladders confirm the reading — they are four
    /// steps with tiers at 2, 3 and 4, so one-based they hand out one effect per level, while the stored
    /// (levels-reached) view would put the ★4 tier on the top step with nothing left to earn it at.
    /// </remarks>
    /// <param name="announce">
    /// Whether each roll is announced to the client. A level-up rolls for one tier and says so; an entry that
    /// repairs several slots rolls them quietly, because the client's prompt is a single replace dialog.
    /// </param>
    public int EnsureTierEffects(byte slotTypeId, bool announce)
    {
        var state = StateOf(slotTypeId);
        if (state == null)
            return 0;

        var level = WireLevel(state.Level, slotTypeId);
        var reached = EquipSlotReinforceRules.EligibleLevelEffects(slotTypeId, level,
            EquipSlotReinforceGameData.Instance.LevelEffects);

        var rolled = 0;
        foreach (var tier in reached)
        {
            if (EffectOf(slotTypeId, tier.Id) != null)
                continue;

            if (RollTierEffect(slotTypeId, tier.Id) == null)
                continue;

            rolled++;
            if (announce)
                SendLevelEffect(slotTypeId, EffectOf(slotTypeId, tier.Id));
        }

        return rolled;
    }

    /// <summary>
    /// Re-rolls a tier the slot has already obtained, which is what the window's rotate button asks for: the
    /// old row is dropped and a different one is rolled, because a row can only be obtained once.
    /// </summary>
    public EquipSlotReinforceUnitModifier RerollTierEffect(byte slotTypeId, uint levelEffectId)
    {
        EquipSlotReinforceEffect previous;
        EquipSlotReinforceUnitModifier rolled;
        lock (_sync)
        {
            if (!_effects.TryGetValue((slotTypeId, levelEffectId), out previous))
                return null;

            var tier = EquipSlotReinforceGameData.Instance.GetLevelEffectById(levelEffectId);
            if (tier == null || tier.SlotTypeId != slotTypeId)
                return null;

            // Exclude the row this slot already holds so a paid replace cannot hand it back. IsObtained
            // would allow it once the old row is dropped, which is why the check is named here.
            rolled = EquipSlotReinforceRules.RollModifier(
                tier.Modifiers,
                id => id == previous.UnitModifierId || IsObtained(id),
                Random.Shared.Next());
            if (rolled == null)
                return null;

            _effects[(slotTypeId, levelEffectId)] = new EquipSlotReinforceEffect
            {
                SlotTypeId = slotTypeId,
                LevelEffectId = levelEffectId,
                UnitModifierId = rolled.Id,
                Applied = true
            };
        }

        SendLevelEffect(slotTypeId, EffectOf(slotTypeId, levelEffectId));
        ApplyEffectsToOwner();
        return rolled;
    }

    /// <summary>
    /// Switches an obtained effect on or off, which is the window's radio: picking a line applies it, and
    /// "None" takes it off. Announces the change with the effect packet or its delete, then recomputes the
    /// character's bonuses.
    /// </summary>
    public bool SetEffectApplied(byte slotTypeId, uint levelEffectId, bool applied)
    {
        EquipSlotReinforceEffect effect;
        lock (_sync)
        {
            if (!_effects.TryGetValue((slotTypeId, levelEffectId), out var found))
                return false;

            if (found.Applied == applied)
                return true;

            found.Applied = applied;
            effect = found.Clone();
        }

        var tier = EquipSlotReinforceGameData.Instance.GetLevelEffectById(effect.LevelEffectId);
        if (tier == null)
            return false;

        if (applied)
            SendLevelEffect(slotTypeId, effect);
        else
            SendLevelEffectDeleted(slotTypeId, effect.LevelEffectId);

        ApplyEffectsToOwner();
        return true;
    }

    /// <summary>True when the character already holds this modifier row on any slot.</summary>
    private bool IsObtained(uint unitModifierId)
    {
        foreach (var effect in _effects.Values)
        {
            if (effect.UnitModifierId == unitModifierId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Recomputes the character's gear bonuses after its artifact effects changed, and re-pushes what the
    /// client shows for the character: the unit state carries the attributes a stat panel reads, and the
    /// points packet is what moves a vitals bar's maximum — a bonus that raises or lowers MaxHp/MaxMp lands on
    /// neither without both.
    /// </summary>
    private void ApplyEffectsToOwner()
    {
        if (_owner == null)
            return;

        _owner.UpdateGearBonuses(null, null);
        if (_owner.Connection == null)
            return;

        _owner.SendPacket(new SCUnitStatePacket(_owner));
        _owner.SendPacket(new SCUnitPointsPacket(_owner.ObjId, _owner.Hp, _owner.Mp));
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
        var rolled = EnsureTierEffects(slotTypeId, announce: false);
        if (rolled > 0)
            ApplyEffectsToOwner();
        return true;
    }

    /// <summary>
    /// Feeds a slot from what the artifact window's Confirm button sent: the slot it is on and the
    /// <c>equip_slot_reinforce_materials</c> row the player picked. The row has to belong to that slot — a
    /// mismatch is refused loudly rather than fed somewhere the player did not ask for.
    /// </summary>
    public EquipSlotReinforceChange FeedFromWindow(byte slotTypeId, uint materialRowId)
    {
        var material = EquipSlotReinforceGameData.Instance.GetMaterialById(materialRowId);
        if (material == null)
        {
            Logger.Warn("Equip slot reinforce {0}: material row {1} does not exist", slotTypeId, materialRowId);
            return EquipSlotReinforceChange.Refused;
        }

        if (material.SlotTypeId != slotTypeId)
        {
            Logger.Warn("Equip slot reinforce {0}: material row {1} belongs to slot {2}",
                slotTypeId, materialRowId, material.SlotTypeId);
            return EquipSlotReinforceChange.Refused;
        }

        // The row has to be one the slot's next step actually offers, or it belongs to another level.
        var materials = MaterialsForNextStep(material.SlotTypeId);
        for (var i = 0; i < materials.Count; i++)
        {
            if (materials[i].Id == material.Id)
                return Feed(material.SlotTypeId, i);
        }

        Logger.Warn("Equip slot reinforce {0}: material row {1} is not offered by the next step",
            slotTypeId, materialRowId);
        return EquipSlotReinforceChange.Refused;
    }

    /// <summary>The materials a slot's next step offers, or an empty list when it has no step left.</summary>
    private IReadOnlyList<EquipSlotReinforceMaterial> MaterialsForNextStep(byte slotTypeId)
    {
        var ladder = EquipSlotReinforceGameData.Instance.Ladder(slotTypeId);
        if (ladder.Count == 0)
            return [];

        var next = EquipSlotReinforceRules.NextStep(GetOrCreate(slotTypeId).Level, ladder);
        return next == null ? [] : EquipSlotReinforceGameData.Instance.Materials(slotTypeId, next.Level);
    }

    /// <summary>
    /// Holds what an artifact-window cast carried until that cast finishes. The window sends its choice when the
    /// player presses Confirm, but the skill it belongs to runs for its whole casting time, so the bar and the
    /// material are only touched when the cast lands - by the <c>EquipSlotReinforceAddExp</c> effect.
    /// </summary>
    public void QueueWindowFeed(byte slotTypeId, uint materialRowId)
    {
        lock (_sync)
            _queuedWindowFeed = (slotTypeId, materialRowId);
    }

    /// <summary>
    /// Spends the queued feed. Taken rather than read, so a cast that never lands cannot feed on a later one.
    /// </summary>
    public EquipSlotReinforceChange ConsumeQueuedWindowFeed()
    {
        (byte SlotTypeId, uint MaterialRowId) queued;
        lock (_sync)
        {
            if (_queuedWindowFeed == null)
                return EquipSlotReinforceChange.Refused;
            queued = _queuedWindowFeed.Value;
            _queuedWindowFeed = null;
        }

        return FeedFromWindow(queued.SlotTypeId, queued.MaterialRowId);
    }

    /// <summary>
    /// Holds what a Replace cast carried: the slot and the tier whose effect the player wants re-rolled. Same
    /// reasoning as the feed - the skill runs for its casting time, so the roll happens when it lands.
    /// </summary>
    public void QueueEffectReplace(byte slotTypeId, ushort triggerLevel)
    {
        lock (_sync)
            _queuedEffectReplace = (slotTypeId, triggerLevel);
    }

    /// <summary>
    /// Takes the queued replace request, so a cast that never lands cannot re-roll anything later.
    /// </summary>
    public bool ConsumeQueuedEffectReplace(out byte slotTypeId, out ushort triggerLevel)
    {
        lock (_sync)
        {
            if (_queuedEffectReplace == null)
            {
                slotTypeId = 0;
                triggerLevel = 0;
                return false;
            }

            (slotTypeId, triggerLevel) = _queuedEffectReplace.Value;
            _queuedEffectReplace = null;
            return true;
        }
    }

    /// <summary>
    /// Re-rolls the effect one tier of a slot holds, which is what the window's Replace mode does with a
    /// serendipity stone: the stone is spent, the row it had is dropped and a different one is rolled out of
    /// the same tier — "each artifact effect can only be obtained once" is what makes it a different one.
    /// </summary>
    public EquipSlotReinforceChange ReplaceTierEffect(byte slotTypeId, ushort triggerLevel)
    {
        var tier = EquipSlotReinforceRules.TierAtLevel(slotTypeId, (sbyte)triggerLevel,
            EquipSlotReinforceGameData.Instance.LevelEffects);
        if (tier == null)
        {
            Logger.Warn("Equip slot reinforce {0}: no tier at level {1} to replace", slotTypeId, triggerLevel);
            return EquipSlotReinforceChange.Refused;
        }

        if (EffectOf(slotTypeId, tier.Id) == null)
        {
            Logger.Warn("Equip slot reinforce {0}: tier {1} holds no effect to replace", slotTypeId, tier.Id);
            return EquipSlotReinforceChange.Refused;
        }

        // Check the price before mutating, exactly like a level-up: a refused replace changes nothing.
        if (!ContentConfigGameData.Instance.TryGetInt(ChangeEffectItemConfigName, out var changeItemId) ||
            changeItemId <= 0)
        {
            Logger.Warn("Equip slot reinforce {0}: content config '{1}' is missing, so nothing can be replaced",
                slotTypeId, ChangeEffectItemConfigName);
            return EquipSlotReinforceChange.Refused;
        }

        var itemId = (uint)changeItemId;
        var carried = _owner.Inventory.GetItemsCount(SlotType.Inventory, itemId);
        if (carried < 1)
        {
            Logger.Warn("Equip slot reinforce {0}: {1} carries no item {2} to replace with",
                slotTypeId, _owner.Name, itemId);
            return EquipSlotReinforceChange.Refused;
        }

        var previous = EffectOf(slotTypeId, tier.Id);
        if (previous == null ||
            !EquipSlotReinforceRules.HasRerollCandidate(tier.Modifiers, previous.UnitModifierId, IsObtained))
        {
            Logger.Warn("Equip slot reinforce {0}: tier {1} has no other effect to roll", slotTypeId, tier.Id);
            return EquipSlotReinforceChange.Refused;
        }

        var consumed = _owner.Inventory.ConsumeItem([SlotType.Inventory], ItemTaskType.EquipSlotReinforce,
            itemId, 1, null);
        if (consumed < 1)
        {
            Logger.Warn("Equip slot reinforce {0}: {1} consumed {2} of item {3}",
                slotTypeId, _owner.Name, consumed, itemId);
            return EquipSlotReinforceChange.Refused;
        }

        var rolled = RerollTierEffect(slotTypeId, tier.Id);
        if (rolled == null)
        {
            Logger.Warn("Equip slot reinforce {0}: tier {1} had nothing left to roll", slotTypeId, tier.Id);
            return EquipSlotReinforceChange.Refused;
        }

        Logger.Info("Equip slot reinforce {0}: {1} replaced tier {2} (level {3}) with modifier {4} (attribute {5} {6})",
            slotTypeId, _owner.Name, tier.Id, tier.TriggerLevel, rolled.Id, rolled.UnitAttributeId, rolled.Value);
        return EquipSlotReinforceChange.EffectReplaced;
    }

    /// <summary>
    /// Banks a feed into a slot's bar, spending the material the request names. The material row picks an
    /// item set and the set's members are the alternatives that pay for it, so a character holding any one
    /// of them in full can feed. Every shipped row also charges gold (<c>currency_id</c> 0) at
    /// <c>currency_value</c>; both the gold and the item are taken together, and a full bar is refused
    /// before either is spent.
    /// </summary>
    public EquipSlotReinforceChange Feed(byte slotTypeId, int materialIndex)
    {
        var ladder = EquipSlotReinforceGameData.Instance.Ladder(slotTypeId);
        if (ladder.Count == 0)
            return EquipSlotReinforceChange.Refused;

        EquipSlotReinforceChange change;
        lock (_sync)
        {
            var state = GetOrCreate(slotTypeId);
            var next = EquipSlotReinforceRules.NextStep(state.Level, ladder);
            if (next == null || next.NeedExp <= 0)
                return EquipSlotReinforceChange.Refused;

            var materials = EquipSlotReinforceGameData.Instance.Materials(slotTypeId, next.Level);
            if (materialIndex < 0 || materialIndex >= materials.Count)
            {
                Logger.Warn("Equip slot reinforce {0}: {1} asked for material {2} of {3}",
                    slotTypeId, _owner.Name, materialIndex, materials.Count);
                return EquipSlotReinforceChange.Refused;
            }

            var material = materials[materialIndex];
            if (EquipSlotReinforceRules.ExpAccepted(state.Exp, next.NeedExp, material.GainExp) <= 0)
                return EquipSlotReinforceChange.Refused;

            var itemSet = ItemManager.Instance.GetItemSet(material.NeedMaterialItemSetId);
            if (itemSet == null)
            {
                Logger.Warn("Equip slot reinforce {0}: material {1} wants item set {2}, which is not loaded",
                    slotTypeId, material.Id, material.NeedMaterialItemSetId);
                return EquipSlotReinforceChange.Refused;
            }

            var members = itemSet.Items.Values.Select(item => (item.ItemId, item.Count)).ToList();
            var pick = EquipSlotReinforceRules.PickConsumable(members,
                itemId => _owner.Inventory.GetItemsCount(SlotType.Inventory, itemId));
            if (pick == null)
            {
                Logger.Warn("Equip slot reinforce {0}: {1} holds nothing usable from item set {2}",
                    slotTypeId, _owner.Name, material.NeedMaterialItemSetId);
                return EquipSlotReinforceChange.Refused;
            }

            if (material.CurrencyValue > 0 &&
                !_owner.TryPayCurrency(material.CurrencyId, material.CurrencyValue, false,
                    ItemTaskType.EquipSlotReinforce))
                return EquipSlotReinforceChange.Refused;

            var consumed = _owner.Inventory.ConsumeItem([SlotType.Inventory], ItemTaskType.EquipSlotReinforce,
                pick.Value.ItemId, pick.Value.Count, null);
            if (consumed < pick.Value.Count)
            {
                var refund = EquipSlotReinforceRules.CurrencyToRefund(material.CurrencyValue, pick.Value.Count,
                    consumed);
                if (refund > 0)
                    _owner.TryRefundCurrency(material.CurrencyId, refund, ItemTaskType.EquipSlotReinforce);

                Logger.Warn("Equip slot reinforce {0}: {1} consumed {2} of {3} x item {4}",
                    slotTypeId, _owner.Name, consumed, pick.Value.Count, pick.Value.ItemId);
                return EquipSlotReinforceChange.Refused;
            }

            change = EquipSlotReinforceRules.ApplyExp(state, ladder, material.GainExp);
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
                var carried = _owner.Inventory.GetItemsCount(SlotType.Inventory, next.LevelUpItemId);
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

        // Reaching a tier hands the slot one of that tier's effects. It is rolled here, once, because a row
        // can only be obtained once and the player is meant to get it without asking for it.
        var rolled = EnsureTierEffects(slotTypeId, announce: true);
        if (rolled > 0)
            ApplyEffectsToOwner();
        return change;
    }

    /// <summary>
    /// Writes the reinforcement block of a unit state: the level and bar of every slot that has progress, then
    /// the artifact effect each slot is running. The client's reinforcement window is filled from this block and
    /// from nothing else, so a character carries its progress, and the effects it obtained, in the unit state.
    /// </summary>
    public void WriteInfos(PacketStream stream)
    {
        List<EquipSlotReinforceState> states;
        lock (_sync)
        {
            states = _states.Values
                .Where(state => state.Level > 0 || state.Exp > 0)
                .OrderBy(state => state.SlotTypeId)
                .Select(state =>
                {
                    var wire = state.Clone();
                    wire.Level = WireLevel(state.Level, state.SlotTypeId);
                    return wire;
                })
                .ToList();
        }

        WriteSlotInfos(stream, states);
        WriteEffectInfos(stream, ActiveEffectEntries());
    }

    /// <summary>
    /// The artifact effects to write into a unit state: the applied ones, each as the slot it belongs to, the
    /// trigger level of the tier that handed it out, and the modifier row it rolled.
    /// </summary>
    private List<(byte SlotTypeId, sbyte TriggerLevel, uint UnitModifierId)> ActiveEffectEntries()
    {
        var data = EquipSlotReinforceGameData.Instance;
        lock (_sync)
        {
            return _effects.Values
                .Where(effect => effect.Applied)
                .Select(effect => (Effect: effect, Tier: data.GetLevelEffectById(effect.LevelEffectId)))
                .Where(entry => entry.Tier != null)
                .Select(entry => (entry.Effect.SlotTypeId, (sbyte)entry.Tier.TriggerLevel, entry.Effect.UnitModifierId))
                .ToList();
        }
    }

    /// <summary>
    /// The slot list on its own, so the wire shape can be pinned without a character behind it: a count
    /// of slots, then each slot's id, level and bar. Slots that were never fed are left out. Levels are
    /// written as given — callers hand in the client's view of them (see <c>WireLevel</c>).
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
    }

    /// <summary>
    /// The effect list on its own: a count, then each entry as the pair the client keys it by — the slot and the
    /// tier's trigger level — followed by the modifier row the slot rolled. That row is the stat itself, which is
    /// why the client can print an attribute and a value for a tier it considers unlocked, and why the list is
    /// ordered by (level, slot): it is a composite-key map and the client's own comparator walks it that way.
    /// Effects the player switched off are left out, so their tier reads as not unlocked.
    /// </summary>
    public static void WriteEffectInfos(PacketStream stream,
        IEnumerable<(byte SlotTypeId, sbyte TriggerLevel, uint UnitModifierId)> entries)
    {
        var ordered = entries?
            .OrderBy(entry => entry.TriggerLevel)
            .ThenBy(entry => entry.SlotTypeId)
            .ToList() ?? [];

        stream.Write((uint)ordered.Count);
        foreach (var (slotTypeId, triggerLevel, unitModifierId) in ordered)
        {
            stream.Write(slotTypeId);
            stream.Write(triggerLevel);
            stream.Write(unitModifierId);
        }
    }

    /// <summary>Total level across every slot that belongs to one attribute.</summary>
    public int AttributeTotal(EquipSlotReinforceAttribute attribute)
    {
        return EquipSlotReinforceRules.AttributeTotal(attribute, States,
            slot => EquipSlotReinforceGameData.Instance.AttributeOf(slot),
            slot =>
            {
                var ladder = EquipSlotReinforceGameData.Instance.Ladder(slot);
                return ladder.Count == 0 ? 0 : ladder[^1].Level;
            });
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
