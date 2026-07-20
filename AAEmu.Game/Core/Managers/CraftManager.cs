using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Менеджер загрузки рецептов крафта из таблиц <c>crafts</c>, <c>craft_products</c>,
/// <c>craft_materials</c> и <c>craft_pack_crafts</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class CraftManager : Singleton<CraftManager>, ICraftManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, Craft> _crafts;

    /// <summary>
    /// Загружает рецепты крафта из таблиц <c>crafts</c>, <c>craft_products</c>,
    /// <c>craft_materials</c> и <c>craft_pack_crafts</c> БД <c>compact.sqlite3</c>.
    /// </summary>
    /// <remarks>
    /// Схемы таблиц (проверены по compact.sqlite3):
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <c>crafts</c>: id (PK), cast_delay, tool_id, skill_id, wi_id, req_doodad_id,
    ///       need_bind, ac_id, actability_limit, show_upper_crafts, recommend_level,
    ///       visible_order, desc, products_pack_id, title, use_only_actability
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <c>craft_products</c>: id (PK), craft_id, item_id, amount, rate,
    ///       show_lower_crafts, use_grade, item_grade_id
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <c>craft_materials</c>: id (PK), craft_id, item_id, amount, main_grade,
    ///       require_grade, upper_grade
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <c>craft_pack_crafts</c>: id (PK), craft_pack_id, craft_id
    ///     </description>
    ///   </item>
    /// </list>
    /// Контейнер: <c>_crafts</c> (id → Craft).
    /// </remarks>
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
                            Id = reader.GetUInt32("id"),
                            CastDelay = reader.GetInt32("cast_delay"),
                            ToolId = reader.GetUInt32("tool_id", 0),
                            SkillId = reader.GetUInt32("skill_id", 0),
                            WiId = reader.GetUInt32("wi_id"),
                            ReqDoodadId = reader.GetUInt32("req_doodad_id", 0),
                            NeedBind = reader.GetBoolean("need_bind"),
                            AcId = reader.GetUInt32("ac_id", 0),
                            ActabilityLimit = reader.GetInt32("actability_limit"),
                            ShowUpperCraft = reader.GetBoolean("show_upper_crafts"),
                            RecommendLevel = reader.GetInt32("recommend_level"),
                            VisibleOrder = reader.GetInt32("visible_order"),
                            Desc = reader.GetString("desc"),
                            ProductsPackId = reader.GetUInt32("products_pack_id", 0),
                            Title = reader.GetString("title"),
                            UseOnlyActability = reader.GetBoolean("use_only_actability")
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
                            Id = reader.GetUInt32("id"),
                            CraftId = craftId,
                            ItemId = reader.GetUInt32("item_id"),
                            Amount = reader.GetInt32("amount", 1), //We always want to produce at least 1 item ?
                            Rate = reader.GetInt32("rate"),
                            ShowLowerCrafts = reader.GetBoolean("show_lower_crafts"),
                            UseGrade = reader.GetBoolean("use_grade"),
                            ItemGradeId = reader.GetUInt32("item_grade_id")
                        };

                        _crafts[template.CraftId].CraftProducts.Add(template);
                    }
                }
            }

            /* Craft materials */
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
                            Id = reader.GetUInt32("id"),
                            CraftId = craftId,
                            ItemId = reader.GetUInt32("item_id"),
                            Amount = reader.GetInt32("amount", 1), //We always want to cost at least 1 item ?
                            MainGrade = reader.GetBoolean("main_grade"),
                            RequireGrade = reader.GetInt32("require_grade"),
                            UpperGrade = reader.GetBoolean("upper_grade")
                        };

                        _crafts[craftId].CraftMaterials.Add(template);
                    }
                }
            }

            /* Craft pack crafts */
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

        Logger.Info("Loaded crafts {0}", _crafts.Count);
    }

    public Craft GetCraftById(uint craftId)
    {
        return _crafts[craftId];
    }
}
