using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class CraftManager : Singleton<CraftManager>, ICraftManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, Craft> _crafts;
    private Dictionary<uint, HashSet<uint>> _craftsByPack;

    /// <summary>Orderable crafts by the item they produce, for the craft order board.</summary>
    private Dictionary<uint, uint> _orderableCraftsByProduct;

    public void Load()
    {
        _crafts = [];
        _craftsByPack = [];
        Logger.Info("Loading crafts...");

        using (var connection = SQLite.CreateConnection())
        {
            /* Crafts */
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM crafts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new Craft
                        {
                            Id = reader.GetUInt32("id"), CastDelay = reader.GetInt32("cast_delay", 0),
                            // 10.0.2.13: tool_id removed
                            SkillId = reader.GetUInt32("skill_id", 0),
                            WiId = reader.GetUInt32("wi_id", 0),
                            MilestoneId = reader.GetUInt32("milestone_id", 0),
                            ReqDoodadId = reader.GetUInt32("req_doodad_id", 0),
                            // 10.0.2.13: need_bind, ac_id removed
                            ActabilityLimit = reader.GetInt32("actability_limit", 0),
                            // 10.0.2.13: show_upper_crafts removed
                            RecommendLevel = reader.GetInt32("recommend_level", 0),
                            VisibleOrder = reader.GetInt32("visible_order", 0),
                            Orderable = reader.GetBoolean("orderable"),
                            Cost = reader.GetInt32("cost", 0)
                        };
                        _crafts.Add(template.Id, template);
                    }
                }
            }

            /* Craft products (item you get at the end) */
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM craft_products";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var craftId = reader.GetUInt32("craft_id");
                        if (!_crafts.ContainsKey(craftId))
                            continue;

                        var template = new CraftProduct
                        {
                            Id = reader.GetUInt32("id"), CraftId = reader.GetUInt32("craft_id"), ItemId = reader.GetUInt32("item_id"),
                            Amount = reader.GetInt32("amount", 1), //We always want to produce at least 1 item ?
                            Rate = reader.GetInt32("rate"),
                            // 10.0.2.13: show_lower_crafts removed
                            UseGrade = reader.GetBoolean("use_grade"),
                            ItemGradeId = reader.GetUInt32("item_grade_id")
                        };

                        _crafts[template.CraftId].CraftProducts.Add(template);
                    }
                }
            }

            /* Craft products (item you get at the end) */
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM craft_materials";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var craftId = reader.GetUInt32("craft_id");
                        if (!_crafts.ContainsKey(craftId))
                            continue;

                        var template = new CraftMaterial
                        {
                            Id = reader.GetUInt32("id"), CraftId = craftId, ItemId = reader.GetUInt32("item_id"), Amount = reader.GetInt32("amount", 1), //We always want to cost at least 1 item ?
                            MainGrade = reader.GetBoolean("main_grade")
                        };

                        _crafts[craftId].CraftMaterials.Add(template);
                    }
                }
            }

            LoadCraftPackMembership(connection);
            LoadActabilityGroups(connection);
            LoadOrderableProducts();
        }

        Logger.Info("Loaded crafts", _crafts.Count);
    }

    /// <summary>
    /// Fills in the actability group of every craft that has one, which is what the craft order
    /// board filters a search by. The group lives on the craft's skill, not on the craft row.
    /// </summary>
    internal void LoadActabilityGroups(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT c.id AS craft_id, s.actability_group_id AS group_id FROM crafts c " +
            "JOIN skills s ON s.id = c.skill_id WHERE s.actability_group_id IS NOT NULL";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var craftId = reader.GetUInt32("craft_id");
            if (_crafts.TryGetValue(craftId, out var craft))
                craft.ActabilityGroupId = reader.GetUInt32("group_id", 0);
        }
    }

    /// <summary>
    /// Indexes the orderable crafts by the item they produce, so a board request that names an item
    /// resolves to its craft without a scan. When several orderable crafts make the same item the
    /// lowest craft id wins, which keeps the answer stable.
    /// </summary>
    internal void LoadOrderableProducts()
    {
        _orderableCraftsByProduct = [];
        foreach (var craft in _crafts.Values.Where(craft => craft.Orderable).OrderBy(craft => craft.Id))
        {
            foreach (var product in craft.CraftProducts)
            {
                if (!_orderableCraftsByProduct.ContainsKey(product.ItemId))
                    _orderableCraftsByProduct.Add(product.ItemId, craft.Id);
            }
        }
    }

    /// <summary>Resolves the orderable craft that produces an item, if the content has one.</summary>
    public bool TryFindOrderableCraftByProduct(uint itemId, out Craft craft)
    {
        craft = null;
        return _orderableCraftsByProduct != null &&
               _orderableCraftsByProduct.TryGetValue(itemId, out var craftId) &&
               _crafts.TryGetValue(craftId, out craft);
    }

    public Craft GetCraftById(uint craftId)
    {
        return _crafts[craftId];
    }

    public bool HasCraft(uint craftId)
    {
        return _crafts.ContainsKey(craftId);
    }

    public bool TryGetCraft(uint craftId, out Craft craft)
    {
        craft = null;
        return _crafts != null && _crafts.TryGetValue(craftId, out craft);
    }

    public bool IsCraftInPack(uint craftPackId, uint craftId)
    {
        return _craftsByPack != null &&
               _craftsByPack.TryGetValue(craftPackId, out var craftIds) &&
               craftIds.Contains(craftId);
    }

    public IReadOnlyCollection<uint> GetCraftIdsForPack(uint craftPackId)
    {
        return _craftsByPack != null && _craftsByPack.TryGetValue(craftPackId, out var craftIds)
            ? craftIds
            : Array.Empty<uint>();
    }

    internal void LoadCraftPackMembership(SqliteConnection connection)
    {
        _craftsByPack = [];
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT craft_pack_id, craft_id FROM craft_pack_crafts ORDER BY craft_pack_id, craft_id";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var craftPackId = reader.GetUInt32("craft_pack_id");
            var craftId = reader.GetUInt32("craft_id");
            if (craftPackId == 0)
                throw new InvalidDataException($"craft_pack_crafts craft {craftId} has a zero craft_pack_id.");
            if (!_crafts.TryGetValue(craftId, out var craft))
            {
                Logger.Warn("Skipping craft_pack_crafts pack {0}: missing crafts row {1}.", craftPackId, craftId);
                continue;
            }

            if (!_craftsByPack.TryGetValue(craftPackId, out var craftIds))
            {
                craftIds = [];
                _craftsByPack.Add(craftPackId, craftIds);
            }
            craftIds.Add(craftId);
            craft.IsPack = true;
        }
    }
}
