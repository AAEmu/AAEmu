using System.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// <c>npc_interaction_sets</c> and <c>npc_interactions</c>: the authored interaction menu an NPC offers
/// through <c>npcs.npc_interaction_set_id</c>. A set is an ordered list of interaction skills, and the
/// row order is the authored order the client's interaction bar shows.
/// <para>
/// Service skills (bank, store, auction, repair, …) are template flags and are not part of these
/// tables, so the two sources are combined per NPC instead of one standing in for the other: the
/// service skill is picked from the flags and the authored set adds the NPC-specific actions.
/// </para>
/// </summary>
[GameData]
public class NpcInteractionGameData : Singleton<NpcInteractionGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<uint, uint[]> _skillsBySetId = [];

    /// <summary>Sets declared by <c>npc_interaction_sets</c>, including any that carry no rows.</summary>
    public int SetCount { get; private set; }

    /// <summary>Interaction rows across every set.</summary>
    public int EntryCount => _skillsBySetId.Values.Sum(skills => skills.Length);

    /// <summary>
    /// The authored interaction skills of a set, in row order. A zero or unknown set id answers an
    /// empty list: an NPC with no set has nothing authored to offer and must not be handed a
    /// placeholder skill instead.
    /// </summary>
    public IReadOnlyList<uint> GetSkills(int interactionSetId)
    {
        if (interactionSetId <= 0)
            return [];
        return _skillsBySetId.TryGetValue((uint)interactionSetId, out var skills) ? skills : [];
    }

    public void Load(SqliteConnection connection)
    {
        _skillsBySetId.Clear();

        var setIds = new HashSet<uint>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM npc_interaction_sets ORDER BY id";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var setId = reader.GetUInt32("id");
                if (setId == 0)
                    throw new InvalidDataException("npc_interaction_sets contains a zero id.");
                if (!setIds.Add(setId))
                    throw new InvalidDataException($"npc_interaction_sets repeats id {setId}.");
            }
        }

        var skillsBySetId = new Dictionary<uint, List<uint>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT id, npc_interaction_set_id, skill_id
                FROM npc_interactions
                ORDER BY id
                """;
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var rowId = reader.GetUInt32("id");
                var setId = reader.GetUInt32("npc_interaction_set_id");
                var skillId = reader.GetUInt32("skill_id");
                if (setId == 0 || skillId == 0)
                    throw new InvalidDataException(
                        $"npc_interactions row {rowId} has a zero set or skill id.");
                if (!setIds.Contains(setId))
                    throw new InvalidDataException(
                        $"npc_interactions row {rowId} references missing set {setId}.");
                if (!skillsBySetId.TryGetValue(setId, out var skills))
                    skillsBySetId.Add(setId, skills = []);
                if (skills.Contains(skillId))
                    throw new InvalidDataException(
                        $"npc_interactions set {setId} repeats skill {skillId}.");
                skills.Add(skillId);
            }
        }

        foreach (var (setId, skills) in skillsBySetId)
            _skillsBySetId[setId] = [.. skills];

        SetCount = setIds.Count;
        Logger.Info("Loaded {0} npc interaction set(s); {1} carry {2} authored action(s)",
            SetCount, _skillsBySetId.Count, EntryCount);
    }

    /// <summary>
    /// Every authored skill must exist. This runs after the managers have loaded, so
    /// <see cref="SkillManager"/> can answer for them.
    /// </summary>
    public void PostLoad()
    {
        var missing = _skillsBySetId.Values
            .SelectMany(skills => skills)
            .Distinct()
            .Where(skillId => SkillManager.Instance.GetSkillTemplate(skillId) == null)
            .Order()
            .ToList();

        if (missing.Count > 0)
            throw new InvalidDataException(
                $"npc_interactions references {missing.Count} skill(s) that skills has no row for: " +
                string.Join(", ", missing));
    }

    /// <summary>Replaces the table for tests that cannot open compact.</summary>
    public void SetForTest(IReadOnlyDictionary<uint, uint[]> skillsBySetId)
    {
        _skillsBySetId.Clear();
        if (skillsBySetId != null)
        {
            foreach (var (setId, skills) in skillsBySetId)
                _skillsBySetId[setId] = skills ?? [];
        }

        SetCount = _skillsBySetId.Count;
    }
}
