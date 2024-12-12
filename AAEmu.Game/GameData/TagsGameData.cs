using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class TagsGameData : Singleton<TagsGameData>, IGameDataLoader
{
    public enum TagType
    {
        Buffs,
        Items,
        Npcs,
        Skills
    }
    private Dictionary<TagType, Dictionary<uint, HashSet<uint>>> _tags;

    //Use different type if we need to ICollection/HashSet/Etc
    public IReadOnlySet<uint> GetIdsByTagId(TagType type, uint tagId)
    {
        if (_tags.TryGetValue(type, out var temp))
        {
            if (temp.TryGetValue(tagId, out var result))
            {
                return result;
            }
        }

        return new HashSet<uint>();
    }

    public void Load(SqliteConnection connection)
    {
        _tags = [];
        #region Tag Tables
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM tagged_buffs";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                var taggedBuffsDict = new Dictionary<uint, HashSet<uint>>();
                _tags.Add(TagType.Buffs, taggedBuffsDict);
                while (reader.Read())
                {
                    var tagId = reader.GetUInt32("tag_id");
                    var buffId = reader.GetUInt32("buff_id");

                    if (!taggedBuffsDict.ContainsKey(tagId))
                        taggedBuffsDict.Add(tagId, []);

                    taggedBuffsDict[tagId].Add(buffId);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM tagged_items";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                var taggedItemsDict = new Dictionary<uint, HashSet<uint>>();
                _tags.Add(TagType.Items, taggedItemsDict);
                while (reader.Read())
                {
                    var tagId = reader.GetUInt32("tag_id");
                    var itemId = reader.GetUInt32("item_id");

                    if (!taggedItemsDict.ContainsKey(tagId))
                        taggedItemsDict.Add(tagId, []);

                    taggedItemsDict[tagId].Add(itemId);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM tagged_npcs";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                var taggedNpcsDict = new Dictionary<uint, HashSet<uint>>();
                _tags.Add(TagType.Npcs, taggedNpcsDict);
                while (reader.Read())
                {
                    var tagId = reader.GetUInt32("tag_id");
                    var npcId = reader.GetUInt32("npc_id");

                    if (!taggedNpcsDict.ContainsKey(tagId))
                        taggedNpcsDict.Add(tagId, []);

                    taggedNpcsDict[tagId].Add(npcId);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM tagged_skills";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                var taggedSkillsDict = new Dictionary<uint, HashSet<uint>>();
                _tags.Add(TagType.Skills, taggedSkillsDict);
                while (reader.Read())
                {
                    var tagId = reader.GetUInt32("tag_id");
                    var skillId = reader.GetUInt32("skill_id");

                    if (!taggedSkillsDict.ContainsKey(tagId))
                        taggedSkillsDict.Add(tagId, []);

                    taggedSkillsDict[tagId].Add(skillId);
                }
            }
        }
        #endregion
    }

    public void PostLoad()
    {
    }

    public IReadOnlySet<uint> GetTagsByTargetId(TagType tagType, uint ownerId)
    {
        var res = new HashSet<uint>();
        if (_tags.TryGetValue(tagType, out var tagDictionary))
        {
            foreach (var (tagKey, tagOwners) in tagDictionary)
            {
                if (tagOwners.Contains(ownerId))
                {
                    res.Add(tagKey);
                }
            }
        }

        return res;
    }
}
