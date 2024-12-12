using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Items.Loots;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class LootGameData : Singleton<LootGameData>, IGameDataLoader
{
    // Main data 
    private Dictionary<uint, LootPack> _lootPacks;

    // Data storage
    private Dictionary<uint, Loot> _loots;
    private Dictionary<uint, LootGroups> _lootGroups;
    private Dictionary<uint, LootActabilityGroups> _lootActabilityGroups;

    // Simple refmaps for ease of use
    private Dictionary<uint, List<Loot>> _lootsByPackId;
    private Dictionary<uint, List<LootGroups>> _lootGroupsByPackId;
    private Dictionary<uint, List<LootActabilityGroups>> _lootActabilityGroupsByPackId;

    public void Load(SqliteConnection connection)
    {
        _lootPacks = [];

        _loots = [];
        _lootGroups = [];
        _lootActabilityGroups = [];

        _lootsByPackId = [];
        _lootGroupsByPackId = [];
        _lootActabilityGroupsByPackId = [];

        // table 'loots'
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM loots";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new Loot()
                    {
                        Id = reader.GetUInt32("id"),
                        Group = reader.GetUInt32("group"),
                        ItemId = reader.GetUInt32("item_id"),
                        DropRate = reader.GetUInt32("drop_rate"),
                        MinAmount = reader.GetInt32("min_amount"),
                        MaxAmount = reader.GetInt32("max_amount"),
                        LootPackId = reader.GetUInt32("loot_pack_id"),
                        GradeId = reader.GetByte("grade_id"),
                        AlwaysDrop = reader.GetBoolean("always_drop", true)
                    };

                    _loots.Add(template.Id, template);

                    if (!_lootsByPackId.ContainsKey(template.LootPackId))
                        _lootsByPackId.Add(template.LootPackId, []);

                    _lootsByPackId[template.LootPackId].Add(template);
                }
            }
        }

        // table 'loot_groups'
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM loot_groups";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new LootGroups()
                    {
                        Id = reader.GetUInt32("id"),
                        PackId = reader.GetUInt32("pack_id"),
                        GroupNo = reader.GetUInt32("group_no"),
                        DropRate = reader.GetUInt32("drop_rate"),
                        ItemGradeDistributionId = reader.GetByte("item_grade_distribution_id")
                    };

                    _lootGroups.Add(template.Id, template);

                    if (!_lootGroupsByPackId.ContainsKey(template.PackId))
                        _lootGroupsByPackId.Add(template.PackId, []);

                    _lootGroupsByPackId[template.PackId].Add(template);
                }
            }
        }

        // table 'loot_actability_groups'
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM loot_actability_groups";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new LootActabilityGroups()
                    {
                        Id = reader.GetUInt32("id"),
                        LootPackId = reader.GetUInt32("loot_pack_id"),
                        GroupId = reader.GetUInt32("loot_group_id"),
                        MaxDice = reader.GetUInt32("max_dice"),
                        MinDice = reader.GetUInt32("min_dice")
                    };

                    _lootActabilityGroups.Add(template.Id, template);

                    if (!_lootActabilityGroupsByPackId.ContainsKey(template.LootPackId))
                        _lootActabilityGroupsByPackId.Add(template.LootPackId, []);

                    _lootActabilityGroupsByPackId[template.LootPackId].Add(template);
                }
            }
        }

        // Generate packs

        foreach (var lootPackId in _lootsByPackId.Keys)
        {
            var pack = new LootPack()
            {
                Id = lootPackId,
                Loots = _lootsByPackId[lootPackId],
                Groups = [],
                ActabilityGroups = [],
                LootsByGroupNo = [],
                GroupCount = 0
            };

            if (_lootGroupsByPackId.TryGetValue(lootPackId, out var lootGroupsList))
                foreach (var lootGroup in lootGroupsList)
                    pack.Groups.Add(lootGroup.GroupNo, lootGroup);

            if (_lootActabilityGroupsByPackId.TryGetValue(lootPackId, out var lootActAbilityGroups))
                foreach (var lag in lootActAbilityGroups)
                    pack.ActabilityGroups.Add(lag.GroupId, lag);

            foreach (var loot in _lootsByPackId[lootPackId])
            {
                if (!pack.LootsByGroupNo.ContainsKey(loot.Group))
                    pack.LootsByGroupNo.Add(loot.Group, []);

                pack.LootsByGroupNo[loot.Group].Add(loot);

                if (pack.GroupCount < loot.Group)
                    pack.GroupCount = loot.Group;
            }

            _lootPacks.Add(pack.Id, pack);
        }
    }

    public void PostLoad()
    {
    }

    public LootPack GetPack(uint id)
    {
        if (_lootPacks.TryGetValue(id, out var pack))
            return pack;
        return null;
    }
}
