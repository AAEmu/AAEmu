using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class CraftManager : Singleton<CraftManager>, ICraftManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, Craft> _crafts;

    public void Load()
    {
        _crafts = [];
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
                            VisibleOrder = reader.GetInt32("visible_order", 0)
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

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM craft_pack_crafts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var craftId = reader.GetUInt32("craft_id");
                        if (!_crafts.TryGetValue(craftId, out var craft))
                            continue;
                        craft.IsPack = true;
                    }
                }
            }
        }

        Logger.Info("Loaded crafts", _crafts.Count);
    }

    public Craft GetCraftById(uint craftId)
    {
        return _crafts[craftId];
    }

    public bool HasCraft(uint craftId)
    {
        return _crafts.ContainsKey(craftId);
    }
}
