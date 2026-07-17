using System.Collections.Concurrent;
using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.NpcGroup;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class NpcGroupGameData : Singleton<NpcGroupGameData>, IGameDataLoader
{
    private readonly ConcurrentDictionary<int, NpcGroup> _npcGroups = new();
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, NpcGroupMember>> _npcGroupMembers = new();

    public NpcGroup GetNpcGroup(int id)
    {
        _npcGroups.TryGetValue(id, out var npcGroup);
        return npcGroup;
    }

    public List<NpcGroupMember> GetNpcGroupMembers(int npcGroupId)
    {
        if (_npcGroupMembers.TryGetValue(npcGroupId, out var members))
        {
            return members.Values.ToList();
        }
        return [];
    }

    public NpcGroupMember GetNpcGroupMember(int npcGroupId, int memberId)
    {
        if (_npcGroupMembers.TryGetValue(npcGroupId, out var members))
        {
            members.TryGetValue(memberId, out var member);
            return member;
        }
        return null;
    }

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        LoadNpcGroups(connection, connection2);
        LoadNpcGroupMembers(connection, connection2);
    }

    private void LoadNpcGroups(SqliteConnection connection, SqliteConnection connection2)
    {
        using var command = connection2.CreateCommand();
        command.CommandText = "SELECT * FROM npc_groups";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var template = new NpcGroup();
            template.Id = reader.GetInt32("id");
            template.Name = reader.GetString("name");
            template.AggroRuleId = reader.GetInt32("aggro_rule_id");
            template.EnableRespawn = reader.GetBoolean("enable_respawn", true);

            _npcGroups.TryAdd(template.Id, template);
        }
    }

    private void LoadNpcGroupMembers(SqliteConnection connection, SqliteConnection connection2)
    {
        using var command = connection2.CreateCommand();
        command.CommandText = "SELECT * FROM npc_group_members";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var template = new NpcGroupMember();
            template.Id = reader.GetInt32("id");
            template.NpcGroupId = reader.GetInt32("npc_group_id");
            template.NpcId = reader.GetInt32("npc_id");
            template.IsLeader = reader.GetBoolean("is_leader", true);
            template.IsMoveLeader = reader.GetBoolean("is_move_leader", true);
            template.FormationOffsetX = reader.GetFloat("formation_offset_x");
            template.FormationOffsetY = reader.GetFloat("formation_offset_y");
            template.FormationOffsetZ = reader.GetFloat("formation_offset_z");
            template.FormationTension = reader.GetFloat("formation_tension");

            if (!_npcGroupMembers.ContainsKey(template.NpcGroupId))
                _npcGroupMembers[template.NpcGroupId] = new ConcurrentDictionary<int, NpcGroupMember>();
            _npcGroupMembers[template.NpcGroupId][template.Id] = template;
        }
    }

    public void PostLoad()
    {
    }
}
