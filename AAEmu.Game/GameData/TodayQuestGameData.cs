using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.TodayAssignment;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

[GameData]
public class TodayQuestGameData : Singleton<TodayQuestGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, TodayQuestStepTemplate> _stepsById = [];
    private Dictionary<uint, TodayQuestStepTemplate> _stepsByRealStep = [];
    private Dictionary<uint, TodayQuestGroupTemplate> _groupsById = [];

    public void Load(SqliteConnection connection)
    {
        _stepsById = [];
        _stepsByRealStep = [];
        _groupsById = [];

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM today_quest_steps";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var step = new TodayQuestStepTemplate
                {
                    Id = reader.GetUInt32("id"),
                    RealStep = reader.GetUInt32("real_step"),
                    ItemId = reader.GetUInt32("item_id", 0),
                    ItemNum = reader.GetInt32("item_num", 0),
                    OrUnitReqs = reader.GetBoolean("or_unit_reqs", true),
                    SortId = reader.GetInt32("sort_id", 0),
                    LevelMin = reader.GetInt32("level_min", 0),
                    LevelMax = reader.GetInt32("level_max", 0)
                };
                _stepsById[step.Id] = step;
                // Prefer the first mapped row when real_step collides (should not in shipped data).
                _stepsByRealStep.TryAdd(step.RealStep, step);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM today_quest_groups";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var group = new TodayQuestGroupTemplate
                {
                    Id = reader.GetUInt32("id"),
                    StepId = reader.GetUInt32("step_id"),
                    OrUnitReqs = reader.GetBoolean("or_unit_reqs", true),
                    AutomaticRestart = reader.GetBoolean("automatic_restart", true)
                };
                _groupsById[group.Id] = group;
                if (_stepsById.TryGetValue(group.StepId, out var step))
                    step.Groups.Add(group);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM today_quest_group_quests ORDER BY id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var groupId = reader.GetUInt32("today_quest_group_id");
                var questId = reader.GetUInt32("quest_context_id");
                if (_groupsById.TryGetValue(groupId, out var group))
                    group.QuestContextIds.Add(questId);
            }
        }

        Logger.Info("Loaded {0} today_quest steps, {1} groups", _stepsById.Count, _groupsById.Count);
    }

    public void PostLoad()
    {
    }

    public IEnumerable<TodayQuestStepTemplate> AllSteps => _stepsById.Values;

    public IEnumerable<TodayQuestGroupTemplate> AllGroups => _groupsById.Values;

    public TodayQuestStepTemplate GetStepByRealStep(uint realStep)
    {
        return _stepsByRealStep.GetValueOrDefault(realStep);
    }

    public TodayQuestStepTemplate GetStepById(uint stepId)
    {
        return _stepsById.GetValueOrDefault(stepId);
    }

    public TodayQuestGroupTemplate GetGroup(uint groupId)
    {
        return _groupsById.GetValueOrDefault(groupId);
    }

    public TodayQuestGroupTemplate FindGroupByQuestId(uint questContextId)
    {
        foreach (var group in _groupsById.Values)
        {
            if (group.QuestContextIds.Contains(questContextId))
                return group;
        }

        return null;
    }
}
