using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.AI.v2.Params;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

#pragma warning disable IDE0079 // Remove unnecessary suppression

namespace AAEmu.Game.GameData;

[GameData]
public class AiGameData : Singleton<AiGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, AiParams> _aiParams;
    private Dictionary<uint, List<AiCommands>> _aiCommands;
    private Dictionary<uint, AiCommandSets> _aiCommandSets;
    private readonly Dictionary<int, NpcChatBubble> _npcChatBubbles = new();
    private readonly Dictionary<int, List<AiEvent>> _aiEventsByNpc = new();

    public AiParams GetAiParamsForId(uint id)
    {
        if (_aiParams.TryGetValue(id, out var value))
            return value;
        return null;
    }

    public List<AiCommands> GetAiCommands(uint id)
    {
        if (_aiCommands.TryGetValue(id, out var value))
            return value;
        return null;
    }

    public void Load(SqliteConnection connection)
    {
        _aiParams = [];
        _aiCommands = [];
        _aiCommandSets = [];

        var fileTypeToId = new Dictionary<uint, AiParamType>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, ai_file_id, npc_ai_param_id FROM npcs";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var npcId = reader.GetUInt32("id");
                    var type = (AiParamType)reader.GetUInt32("ai_file_id");
                    var id = reader.GetUInt32("npc_ai_param_id");
                    fileTypeToId.TryAdd(id, type);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM npc_ai_params";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var id = reader.GetUInt32("id");
                    if (!fileTypeToId.ContainsKey(id))
                        continue;

                    var fileType = fileTypeToId[id];
                    try
                    {
                        var data = reader.IsDBNull("ai_param") ? string.Empty : reader.GetString("ai_param");
                        var aiParams = AiParams.CreateByType(fileType, data);
#pragma warning disable CA1508 // Avoid dead conditional code
                        if (aiParams != null)
                            _aiParams.TryAdd(id, aiParams);
#pragma warning restore CA1508 // Avoid dead conditional code
                    }
                    catch (Exception e)
                    {
                        Logger.Warn("Impossible to parse npc_ai_params {0}\n{1}", id, e.Message);
                    }
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM ai_commands";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                var tempListId = new List<uint>();
                while (reader.Read())
                {
                    var template = new AiCommands();
                    template.Id = reader.GetUInt32("id");
                    if (tempListId.Contains(template.Id))
                    {
                        continue; // The table contains duplicates.
                    }

                    tempListId.Add(template.Id);
                    template.CmdSetId = reader.GetUInt32("cmd_set_id");
                    template.CmdId = (AiCommandCategory)reader.GetUInt32("cmd_id");
                    template.Param1 = reader.GetUInt32("param1");
                    template.Param2 = reader.GetString("param2");

                    if (!_aiCommands.TryGetValue(template.CmdSetId, out var value))
                    {
                        value = [];
                        _aiCommands.Add(template.CmdSetId, value);
                    }

                    value.Add(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM ai_command_sets";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new AiCommandSets();
                    template.Id = reader.GetUInt32("id");
                    template.Name = reader.GetString("name");
                    template.CanInteract = reader.GetBoolean("can_interact");

                    _aiCommandSets.TryAdd(template.Id, template);
                }
            }
        }

        LoadNpcChatBubbles(connection);
        LoadAiEvents(connection);
    }

    private void LoadNpcChatBubbles(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM npc_chat_bubbles";
        command.Prepare();

        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);

        while (reader.Read())
        {
            var bubble = new NpcChatBubble();
            bubble.Id = reader.GetInt32("id");
            bubble.AiEventId = reader.GetInt32("ai_event_id");
            bubble.Bubble = reader.GetString("bubble");
            _npcChatBubbles[bubble.AiEventId] = bubble;
        }

        Logger.Info($"Loaded {_npcChatBubbles.Count} entries from npc_chat_bubbles.");
    }

    private void LoadAiEvents(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ai_events";
        command.Prepare();

        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);

        while (reader.Read())
        {
            var aiEvent = new AiEvent();
            aiEvent.Id = reader.GetInt32("id");
            aiEvent.IgnoreCategoryId = reader.GetInt32("ignore_category_id");
            aiEvent.Weight = reader.GetFloat("ignore_time", 0f);
            aiEvent.EventName = reader.GetString("name");
            aiEvent.NpcId = reader.GetInt32("npc_id");
            aiEvent.OrUnitReqs = reader.GetBoolean("or_unit_reqs", false);
            aiEvent.SkillId = reader.IsDBNull("skill_id") ? 0 : reader.GetInt32("skill_id");

            if (!_aiEventsByNpc.ContainsKey(aiEvent.NpcId))
                _aiEventsByNpc[aiEvent.NpcId] = [];
            _aiEventsByNpc[aiEvent.NpcId].Add(aiEvent);
        }

        Logger.Info($"Loaded {_aiEventsByNpc.Count} records from ai_events.");
    }

    public bool TryGet(int id, out NpcChatBubble bubble) => _npcChatBubbles.TryGetValue(id, out bubble);

    /// <summary>
    /// Get all NPC events by event name
    /// </summary>
    /// <param name="npcId"></param>
    /// <param name="eventName"></param>
    /// <returns></returns>
    public List<AiEvent> GetEvents(int npcId, string eventName)
    {
        if (_aiEventsByNpc.TryGetValue(npcId, out var list))
            return list.FindAll(e => e.EventName.Equals(eventName, StringComparison.OrdinalIgnoreCase));
        return [];
    }

    public AiEvent GetEvent(int npcId, string eventName, float weight)
    {
        if (_aiEventsByNpc.TryGetValue(npcId, out var list))
            return list.Find(e => e.EventName.Equals(eventName, StringComparison.OrdinalIgnoreCase) && e.Weight >= weight);

        return null;
    }

    public void PostLoad()
    {
        NpcManager.Instance.LoadAiParams();
    }
}
