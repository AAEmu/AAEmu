using System.Linq;
using System.Text.RegularExpressions;

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
                    if (!fileTypeToId.TryGetValue(id, out var fileType))
                        continue;

                    var data = reader.IsDBNull("ai_param") ? string.Empty : reader.GetString("ai_param");
                    var aiParams = TryParseAiParams(fileType, data, id);
                    if (aiParams != null)
                        _aiParams.TryAdd(id, aiParams);
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
                    var template = new AiCommands { Id = reader.GetUInt32("id") };
                    if (tempListId.Contains(template.Id))
                    {
                        continue; // The table contains duplicates.
                    }

                    tempListId.Add(template.Id);
                    template.CmdSetId = reader.GetUInt32("cmd_set_id");
                    template.CmdId = (AiCommandCategory)reader.GetUInt32("cmd_id");
                    // 10.0.2.13: ai_commands.param1 is a varchar(32) (e.g. "3 sec"/"7.5"); read as string, parse on use.
                    template.Param1 = reader.GetString("param1", "");
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
                    var template = new AiCommandSets
                    {
                        // 10.0.2.13: can_interact is stored as text 't'/'f', so use the
                        // string-aware boolean overload to avoid a parse exception.
                        Id = reader.GetUInt32("id"), Name = reader.GetString("name"), CanInteract = reader.GetBoolean("can_interact", true)
                    };

                    _aiCommandSets.TryAdd(template.Id, template);
                }
            }
        }

        LoadNpcChatBubbles(connection);
        LoadAiEvents(connection);
    }

    private static AiParams TryParseAiParams(AiParamType fileType, string data, uint id)
    {
        try
        {
            return AiParams.CreateByType(fileType, data);
        }
        catch
        {
            // The original Korean AI data has occasional malformed-Lua entries (missing commas, [N,M] index
            // syntax, closing braces lost inside line comments, truncated tables). Retry once with a repaired copy.
            try
            {
                return AiParams.CreateByType(fileType, SanitizeAiParam(data));
            }
            catch (Exception e)
            {
                Logger.Warn("Impossible to parse npc_ai_params {0}\n{1}", id, e.Message);
                return null;
            }
        }
    }

    // Repairs the recurring malformations in the npc_ai_params Lua fragments so the table still parses.
    private static string SanitizeAiParam(string data)
    {
        // Strip Lua line comments (-- to end of line); a trailing Korean name after '--' otherwise eats the
        // closing braces that follow it on the same line.
        data = Regex.Replace(data, @"--[^\r\n]*", "");
        // '[N, M]' index syntax is invalid table content; the data means a {N, M} list.
        data = Regex.Replace(data, @"\[\s*(\d+)\s*,\s*(\d+)\s*\]", "{$1, $2}");
        // Missing comma between a closing brace and the next "key =".
        data = Regex.Replace(data, @"\}(\s*)([A-Za-z_]\w*\s*=)", "},$1$2");
        // Re-close tables truncated in the source data (or whose braces were eaten by a comment).
        var open = data.Count(c => c == '{');
        var close = data.Count(c => c == '}');
        if (open > close)
            data += new string('}', open - close);
        return data;
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
            var bubble = new NpcChatBubble
            {
                Id = reader.GetInt32("id"), AiEventId = reader.GetInt32("ai_event_id"), Bubble = reader.GetString("bubble")
            };
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
            var aiEvent = new AiEvent
            {
                Id = reader.GetInt32("id"), IgnoreCategoryId = reader.GetInt32("ignore_category_id"), Weight = reader.GetFloat("ignore_time", 0f),
                EventName = reader.GetString("name"),
                NpcId = reader.GetInt32("npc_id"),
                OrUnitReqs = reader.GetBoolean("or_unit_reqs", true), // fromString: ai_events.or_unit_reqs is a 't'/'f' text column
                SkillId = reader.IsDBNull("skill_id") ? 0 : reader.GetInt32("skill_id")
            };

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
