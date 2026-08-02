using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Heirs;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// Heir progression thresholds and the selectable successor-skill graph.
/// </summary>
/// <remarks>
/// UnitState carries heirLevel, and the client also displays a heirExpPercent alongside it. Levels
/// run 0-70 against a cumulative total, so only the total is stored on the character and the level is
/// resolved here; the top threshold is 178,230,921,286, which is why the total is 64-bit.
/// </remarks>
[GameData]
public class HeirGameData : Singleton<HeirGameData>, IGameDataLoader
{
    private List<HeirLevel> _levels = [];
    private List<HeirSkill> _skills = [];
    private List<HeirSkillDetail> _skillDetails = [];
    private Dictionary<uint, HeirSkill> _skillsById = [];
    private Dictionary<uint, HeirSkillDetail> _successorsBySkillId = [];

    /// <summary>Normal character level at which heir progression becomes available.</summary>
    public byte StartLevel { get; private set; }

    /// <summary>Highest heir level described by <c>heir_levels</c>.</summary>
    public byte MaxLevel => _levels.Count == 0 ? (byte)0 : _levels[^1].Level;

    /// <summary>
    /// Resolves the current heir level using the client's upper-bound lookup: the first row whose
    /// cumulative requirement is greater than <paramref name="totalExp"/> supplies the level.
    /// </summary>
    public byte GetLevelForExp(long totalExp)
    {
        foreach (var level in _levels)
        {
            if (totalExp < level.ReqTotalExp)
                return level.Level;
        }

        return MaxLevel;
    }

    /// <summary>Content row for an heir level, or <c>null</c> when it is not defined.</summary>
    public HeirLevel GetLevel(byte level) => _levels.FirstOrDefault(entry => entry.Level == level);

    /// <summary>
    /// Validates the native level-up boundary and returns the row which supplies its optional material.
    /// The request is accepted only at exactly one experience point below the current level's threshold.
    /// </summary>
    public bool TryGetLevelUpRequirement(byte characterLevel, long totalExp, out HeirLevel requirement)
    {
        requirement = null;
        if (characterLevel < StartLevel)
            return false;

        var heirLevel = GetLevelForExp(totalExp);
        if (heirLevel >= MaxLevel)
            return false;

        requirement = GetLevel(heirLevel);
        return requirement != null && totalExp == requirement.ReqTotalExp - 1;
    }

    /// <summary>
    /// Applies a positive experience notification to heir progression. Native keeps experience one
    /// point below the current level's threshold until the explicit level-up request crosses it.
    /// </summary>
    public long ApplyExpGain(long totalExp, int expDelta)
    {
        if (expDelta <= 0)
            return totalExp;

        var heirLevel = GetLevelForExp(totalExp);
        if (heirLevel >= MaxLevel)
            return totalExp;

        var level = GetLevel(heirLevel);
        if (level == null)
            return totalExp;

        var cap = level.ReqTotalExp - 1;
        if (totalExp >= cap)
            return cap;

        return totalExp + Math.Min((long)expDelta, cap - totalExp);
    }

    /// <summary>Step the given heir level belongs to, or 0 when the level is unknown.</summary>
    public byte GetStepForLevel(byte level) =>
        _levels.FirstOrDefault(l => l.Level == level)?.Step ?? 0;

    /// <summary>
    /// How far the character is through their current level, 0-100, which is what the client shows
    /// as heirExpPercent. Returns 0 once the last level is reached, since there is nothing above it.
    /// </summary>
    public byte GetExpPercent(long totalExp)
    {
        var current = _levels.Where(l => totalExp >= l.ReqTotalExp).OrderByDescending(l => l.Level).FirstOrDefault();
        var next = _levels.Where(l => l.ReqTotalExp > totalExp).OrderBy(l => l.ReqTotalExp).FirstOrDefault();
        if (next == null)
            return 0;

        var floor = current?.ReqTotalExp ?? 0;
        var span = next.ReqTotalExp - floor;
        if (span <= 0)
            return 0;

        return (byte)Math.Clamp((totalExp - floor) * 100 / span, 0, 100);
    }

    /// <summary>Enabled base-skill rows whose successors may be selected at this step.</summary>
    public IEnumerable<HeirSkill> GetSelectableHeirSkillsForStep(byte step) =>
        _skills.Where(skill => skill.Enable && skill.Step <= step);

    /// <summary>
    /// Resolves an enabled Heir base-skill row and verifies that the character has unlocked its step.
    /// </summary>
    public bool TryGetSelectableHeirSkill(uint heirSkillId, byte unlockedStep, out HeirSkill heirSkill)
    {
        heirSkill = null;
        if (!_skillsById.TryGetValue(heirSkillId, out var candidate) ||
            !candidate.Enable || candidate.Step > unlockedStep)
            return false;

        heirSkill = candidate;
        return true;
    }

    /// <summary>
    /// Resolves a successor only when it belongs to the requested enabled Heir row and that row's
    /// step is unlocked. This is the validation boundary for activation requests and persisted rows.
    /// </summary>
    public bool TryGetSelectableSuccessor(
        uint heirSkillId,
        uint successorSkillId,
        byte unlockedStep,
        out HeirSkillDetail successor)
    {
        successor = null;
        if (!TryGetSelectableHeirSkill(heirSkillId, unlockedStep, out var heirSkill) ||
            !_successorsBySkillId.TryGetValue(successorSkillId, out var candidate) ||
            candidate.HeirSkillId != heirSkill.Id)
            return false;

        successor = candidate;
        return true;
    }

    /// <summary>Resolves the content owner and detail for a globally unique successor skill.</summary>
    public bool TryGetHeirSkillForSuccessor(
        uint successorSkillId,
        out HeirSkill heirSkill,
        out HeirSkillDetail successor)
    {
        heirSkill = null;
        successor = null;
        if (!_successorsBySkillId.TryGetValue(successorSkillId, out var candidate) ||
            !_skillsById.TryGetValue(candidate.HeirSkillId, out var owner))
            return false;

        heirSkill = owner;
        successor = candidate;
        return true;
    }

    public void Load(SqliteConnection connection)
    {
        _levels = [];
        _skills = [];
        _skillDetails = [];
        _skillsById = [];
        _successorsBySkillId = [];
        StartLevel = 0;

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT c.value FROM content_configs c " +
                "JOIN enum_content_configs e ON e.id = c.id " +
                "WHERE e.name = 'heir_start_level'";
            command.Prepare();
            var value = command.ExecuteScalar();
            if (value == null || value == DBNull.Value)
                throw new InvalidDataException("Missing required content config 'heir_start_level'.");

            var startLevel = Convert.ToInt32(value);
            if (startLevel is <= 0 or > byte.MaxValue)
                throw new InvalidDataException($"Invalid heir_start_level value {startLevel}.");
            StartLevel = (byte)startLevel;
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM heir_levels";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                _levels.Add(new HeirLevel
                {
                    Id = reader.GetUInt32("id"),
                    Level = (byte)reader.GetUInt32("level", 0),
                    ReqTotalExp = reader.GetInt64("req_total_exp", 0),
                    Step = (byte)reader.GetUInt32("step", 0),
                    ReqItemId = reader.GetUInt32("req_item_id", 0),
                    ReqItemCount = reader.GetInt32("req_item_count", 0)
                });
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM heir_skills";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                _skills.Add(new HeirSkill
                {
                    Id = reader.GetUInt32("id"),
                    SkillId = reader.GetUInt32("skill_id", 0),
                    Step = (byte)reader.GetUInt32("step", 0),
                    Enable = reader.GetBoolean("enable", true)
                });
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM heir_skill_details";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                _skillDetails.Add(new HeirSkillDetail
                {
                    Id = reader.GetUInt32("id"),
                    HeirSkillId = reader.GetUInt32("heir_skill_id"),
                    SkillId = reader.GetUInt32("skill_id", 0),
                    Pos = reader.GetInt32("pos", 0),
                    SkillActiveTypeId = (SkillActiveType)checked((byte)reader.GetUInt32("skill_active_type_id", 0)),
                    Desc = reader.GetString("desc", string.Empty),
                    ActiveItemId = reader.GetUInt32("active_item_id", 0)
                });
            }
        }
    }

    public void PostLoad()
    {
        _levels = _levels.OrderBy(level => level.Level).ToList();
        if (_levels.Count == 0)
            throw new InvalidDataException("heir_levels contains no rows.");

        long previousThreshold = -1;
        for (var index = 0; index < _levels.Count; index++)
        {
            var level = _levels[index];
            if (level.Level != index)
                throw new InvalidDataException($"heir_levels must be contiguous from level 0; found {level.Level} at index {index}.");
            if (level.ReqTotalExp <= previousThreshold)
                throw new InvalidDataException($"heir_levels threshold for level {level.Level} is not strictly increasing.");
            if ((level.ReqItemId == 0 && level.ReqItemCount != 0) ||
                (level.ReqItemId != 0 && level.ReqItemCount <= 0))
                throw new InvalidDataException($"heir_levels level {level.Level} has an incomplete item requirement.");

            previousThreshold = level.ReqTotalExp;
        }

        _skillsById = [];
        var baseSkillIds = new HashSet<uint>();
        foreach (var heirSkill in _skills)
        {
            if (heirSkill.Id == 0 || heirSkill.SkillId == 0 || heirSkill.Step == 0)
                throw new InvalidDataException("heir_skills contains a zero row, base skill, or step id.");
            if (!_skillsById.TryAdd(heirSkill.Id, heirSkill))
                throw new InvalidDataException($"heir_skills contains duplicate id {heirSkill.Id}.");
            if (!baseSkillIds.Add(heirSkill.SkillId))
                throw new InvalidDataException($"heir_skills contains duplicate base skill_id {heirSkill.SkillId}.");
        }

        var detailIds = new HashSet<uint>();
        var positionsByHeirSkill = new Dictionary<uint, HashSet<int>>();
        _successorsBySkillId = [];
        foreach (var detail in _skillDetails)
        {
            if (detail.Id == 0 || detail.SkillId == 0)
                throw new InvalidDataException("heir_skill_details contains a zero row or successor skill id.");
            if (!Enum.IsDefined(detail.SkillActiveTypeId))
                throw new InvalidDataException(
                    $"heir_skill_details row {detail.Id} has invalid skill_active_type_id {(byte)detail.SkillActiveTypeId}.");
            if (!detailIds.Add(detail.Id))
                throw new InvalidDataException($"heir_skill_details contains duplicate id {detail.Id}.");
            if (!_skillsById.ContainsKey(detail.HeirSkillId))
                throw new InvalidDataException(
                    $"heir_skill_details row {detail.Id} references missing heir_skill_id {detail.HeirSkillId}.");
            if (!_successorsBySkillId.TryAdd(detail.SkillId, detail))
                throw new InvalidDataException(
                    $"heir_skill_details contains duplicate successor skill_id {detail.SkillId}.");

            if (!positionsByHeirSkill.TryGetValue(detail.HeirSkillId, out var positions))
            {
                positions = [];
                positionsByHeirSkill.Add(detail.HeirSkillId, positions);
            }

            if (detail.Pos <= 0 || !positions.Add(detail.Pos))
                throw new InvalidDataException(
                    $"heir_skill_details row {detail.Id} has invalid or duplicate position {detail.Pos}.");
        }

        var detailsByHeirSkill = _skillDetails
            .GroupBy(detail => detail.HeirSkillId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<HeirSkillDetail>)group
                    .OrderBy(detail => detail.Pos)
                    .ThenBy(detail => detail.Id)
                    .ToList());

        foreach (var heirSkill in _skills)
        {
            heirSkill.Successors = detailsByHeirSkill.GetValueOrDefault(heirSkill.Id) ?? [];
            if (heirSkill.Enable && heirSkill.Successors.Count == 0)
                throw new InvalidDataException($"Enabled heir_skills row {heirSkill.Id} has no successor details.");
        }
    }
}
