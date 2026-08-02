using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Heirs;
using AAEmu.Game.Models.Game.Skills;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Server-authoritative Heir successor selections for one character. The learned base skill remains
/// in <see cref="CharacterSkills"/>; this map controls which one of its content-defined successors
/// the character may cast.
/// </summary>
public sealed class CharacterHeirSkills(Character owner)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly object _sync = new();
    private readonly Dictionary<uint, uint> _activeSuccessors = [];

    private Character Owner { get; } = owner;

    /// <summary>Validates and applies one client activation/change request.</summary>
    public bool TryActivate(uint heirSkillId, uint successorSkillId, bool isChange)
    {
        var gameData = HeirGameData.Instance;
        if (!gameData.TryGetSelectableHeirSkill(heirSkillId, Owner.HeirStep, out var heirSkill) ||
            !gameData.TryGetSelectableSuccessor(heirSkillId, successorSkillId, Owner.HeirStep, out var successor))
            return false;

        if (!Owner.Skills.Skills.ContainsKey(heirSkill.SkillId))
            return false;

        // active_item_id is acquisition metadata, but the native activation request contains no
        // item or cost field. Do not invent item consumption or a lock rule in this handler.
        var baseTemplate = SkillManager.Instance.GetSkillTemplate(heirSkill.SkillId);
        var successorTemplate = SkillManager.Instance.GetSkillTemplate(successor.SkillId);
        if (baseTemplate == null || successorTemplate == null ||
            baseTemplate.AbilityId != successorTemplate.AbilityId ||
            Owner.SkillActiveTypes.GetHeirState(heirSkillId, successor) != SkillActiveType.Active ||
            Owner.GetAbLevel(successorTemplate.AbilityId) < gameData.StartLevel)
            return false;

        lock (_sync)
        {
            var hasCurrent = _activeSuccessors.TryGetValue(heirSkillId, out var currentSuccessorId);
            if (hasCurrent != isChange || currentSuccessorId == successorSkillId)
                return false;

            if (!TryPersistSelection(heirSkillId, successorSkillId))
                return false;

            _activeSuccessors[heirSkillId] = successorSkillId;
            Owner.SendPacket(new SCActivatedHeirSkillPacket(
                checked((int)heirSkillId),
                checked((int)successorSkillId),
                isChange));
            return true;
        }
    }

    public bool TryReset(HeirSkillResetKind resetKind, sbyte ability, int successorSkillId)
    {
        lock (_sync)
        {
            var removed = resetKind switch
            {
                HeirSkillResetKind.All => RemoveAll(),
                HeirSkillResetKind.Ability => TryGetAbility(ability, out var abilityType) &&
                                              RemoveByAbility(abilityType),
                // The native kind-3 branch selects only by successorSkillId. Ability remains on the
                // wire but is deliberately not part of this branch's validation.
                HeirSkillResetKind.Successor => successorSkillId > 0 && RemoveSuccessor(checked((uint)successorSkillId)),
                _ => false
            };

            if (!removed)
                return false;

            Owner.SendPacket(new SCResetHeirSkillPacket(
                (uint)resetKind,
                successorSkillId,
                ability));
            return true;
        }
    }

    /// <summary>
    /// Removes selections for a skill tree after its learned base skills are reset. The caller can
    /// suppress the Heir reset reply when it is already rebuilding the character state.
    /// </summary>
    public bool RemoveByAbility(AbilityType ability, bool notifyClient = false)
    {
        lock (_sync)
        {
            var removals = _activeSuccessors
                .Where(pair => SkillManager.Instance.GetSkillTemplate(pair.Value)?.AbilityId == ability)
                .Select(pair => pair.Key)
                .ToList();

            if (removals.Count == 0 || !TryDeletePersistedSelections(removals))
                return false;

            foreach (var heirSkillId in removals)
                _activeSuccessors.Remove(heirSkillId);

            if (notifyClient)
                Owner.SendPacket(new SCResetHeirSkillPacket(
                    (uint)HeirSkillResetKind.Ability,
                    0,
                    checked((sbyte)ability)));
            return true;
        }
    }

    /// <summary>True only for the successor currently selected by this character.</summary>
    public bool IsActiveSuccessor(uint skillId)
    {
        uint heirSkillId;
        lock (_sync)
        {
            var match = _activeSuccessors.FirstOrDefault(pair => pair.Value == skillId);
            heirSkillId = match.Key;
        }

        return heirSkillId != 0 &&
               HeirGameData.Instance.TryGetHeirSkillForSuccessor(skillId, out var heirSkill, out var detail) &&
               heirSkill.Id == heirSkillId &&
               Owner.SkillActiveTypes.GetHeirState(heirSkillId, detail) == SkillActiveType.Active;
    }

    /// <summary>Builds the exact fixed-width records consumed by <c>SCHeirSkillListPacket</c>.</summary>
    public IReadOnlyList<HeirSkillListEntry> BuildPacketEntries()
    {
        KeyValuePair<uint, uint>[] snapshot;
        lock (_sync)
            snapshot = _activeSuccessors.OrderBy(pair => pair.Key).ToArray();

        var result = new List<HeirSkillListEntry>(snapshot.Length);
        foreach (var (heirSkillId, successorSkillId) in snapshot)
        {
            if (!TryResolveSelection(heirSkillId, successorSkillId, out var heirSkill, out var successor,
                    out var successorTemplate))
                continue;

            var skillLevel = new Skill(successorTemplate, Owner).Level;
            result.Add(new HeirSkillListEntry(
                checked((int)heirSkill.Id),
                checked((int)heirSkill.SkillId),
                checked((int)successor.SkillId),
                skillLevel,
                checked((sbyte)successorTemplate.AbilityId),
                checked((sbyte)Owner.SkillActiveTypes.GetHeirState(heirSkillId, successor))));
        }

        return result;
    }

    public void Load(MySqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT heir_skill_id, successor_skill_id FROM heir_skill_activations WHERE owner = @owner";
        command.Parameters.AddWithValue("@owner", Owner.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var heirSkillId = reader.GetUInt32("heir_skill_id");
            var successorSkillId = reader.GetUInt32("successor_skill_id");
            if (!TryResolveSelection(heirSkillId, successorSkillId, out _, out _, out _))
            {
                Logger.Warn(
                    "Ignoring invalid persisted Heir selection for {0}: heirSkill={1}, successor={2}",
                    Owner.Name, heirSkillId, successorSkillId);
                continue;
            }

            if (!_activeSuccessors.TryAdd(heirSkillId, successorSkillId))
                Logger.Warn("Ignoring duplicate persisted Heir selection for {0}: heirSkill={1}", Owner.Name, heirSkillId);
        }
    }

    private bool TryResolveSelection(
        uint heirSkillId,
        uint successorSkillId,
        out HeirSkill heirSkill,
        out HeirSkillDetail successor,
        out AAEmu.Game.Models.Game.Skills.Templates.SkillTemplate successorTemplate)
    {
        heirSkill = null;
        successor = null;
        successorTemplate = null;

        var gameData = HeirGameData.Instance;
        if (!gameData.TryGetSelectableHeirSkill(heirSkillId, Owner.HeirStep, out heirSkill) ||
            !gameData.TryGetSelectableSuccessor(heirSkillId, successorSkillId, Owner.HeirStep, out successor) ||
            !Owner.Skills.Skills.ContainsKey(heirSkill.SkillId))
            return false;

        var baseTemplate = SkillManager.Instance.GetSkillTemplate(heirSkill.SkillId);
        successorTemplate = SkillManager.Instance.GetSkillTemplate(successor.SkillId);
        return baseTemplate != null && successorTemplate != null &&
               baseTemplate.AbilityId == successorTemplate.AbilityId &&
               Owner.GetAbLevel(successorTemplate.AbilityId) >= gameData.StartLevel;
    }

    private bool RemoveAll()
    {
        lock (_sync)
        {
            if (_activeSuccessors.Count == 0)
                return false;

            if (!TryDeletePersistedSelections(_activeSuccessors.Keys.ToArray()))
                return false;

            _activeSuccessors.Clear();
            return true;
        }
    }

    private bool RemoveSuccessor(uint successorSkillId)
    {
        lock (_sync)
        {
            var match = _activeSuccessors.FirstOrDefault(pair => pair.Value == successorSkillId);
            if (match.Key == 0)
                return false;

            if (!TryDeletePersistedSelections([match.Key]))
                return false;

            return _activeSuccessors.Remove(match.Key);
        }
    }

    private bool TryPersistSelection(uint heirSkillId, uint successorSkillId)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO heir_skill_activations(owner, heir_skill_id, successor_skill_id) " +
                "VALUES (@owner, @heirSkillId, @successorSkillId) " +
                "ON DUPLICATE KEY UPDATE successor_skill_id = VALUES(successor_skill_id)";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.Parameters.AddWithValue("@heirSkillId", heirSkillId);
            command.Parameters.AddWithValue("@successorSkillId", successorSkillId);
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(
                exception,
                "Failed to persist Heir selection for {0}: heirSkill={1}, successor={2}",
                Owner.Name,
                heirSkillId,
                successorSkillId);
            return false;
        }
    }

    private bool TryDeletePersistedSelections(IReadOnlyCollection<uint> heirSkillIds)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var transaction = connection.BeginTransaction();
            foreach (var heirSkillId in heirSkillIds)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    "DELETE FROM heir_skill_activations WHERE owner = @owner AND heir_skill_id = @heirSkillId";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Parameters.AddWithValue("@heirSkillId", heirSkillId);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to persist Heir reset for {0}", Owner.Name);
            return false;
        }
    }

    private static bool TryGetAbility(sbyte value, out AbilityType ability)
    {
        ability = (AbilityType)value;
        return ability is > AbilityType.General and < AbilityType.None;
    }
}
