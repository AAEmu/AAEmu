using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Utils.DB;
using NLog;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Managers
{
    public class CraftManager : Singleton<CraftManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, Craft> _crafts;

        public void Load()
        {
            _crafts = new Dictionary<uint, Craft>();
            _log.Info("Loading crafts...");

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
                            var template = new Craft();
                            template.Id = reader.GetUInt32("id");
                            template.CastDelay = reader.GetInt32("cast_delay", 0);
                            template.ToolId = reader.GetUInt32("tool_id", 0);
                            template.SkillId = reader.GetUInt32("skill_id", 0);
                            template.WiId = reader.GetUInt32("wi_id", 0);
                            template.MilestoneId = reader.GetUInt32("milestone_id", 0);
                            template.ReqDoodadId = reader.GetUInt32("req_doodad_id", 0);
                            template.NeedBind = reader.GetBoolean("need_bind");
                            template.AcId = reader.GetUInt32("ac_id", 0);
                            template.ActabilityLimit = reader.GetInt32("actability_limit", 0);
                            template.ShowUpperCraft = reader.GetBoolean("show_upper_crafts");
                            template.RecommendLevel = reader.GetInt32("recommend_level", 0);
                            template.VisibleOrder = reader.GetInt32("visible_order", 0);
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

                            var template = new CraftProduct();
                            template.Id = reader.GetUInt32("id");
                            template.CraftId = reader.GetUInt32("craft_id");
                            template.ItemId = reader.GetUInt32("item_id");
                            template.Amount = reader.GetInt32("amount", 1); //We always want to produce at least 1 item ?
                            template.Rate = reader.GetInt32("rate");
                            template.ShowLowerCrafts = reader.GetBoolean("show_lower_crafts");
                            template.UseGrade = reader.GetBoolean("use_grade");
                            template.ItemGradeId = reader.GetUInt32("item_grade_id");

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

                            var template = new CraftMaterial();
                            template.Id = reader.GetUInt32("id");
                            template.CraftId = craftId;
                            template.ItemId = reader.GetUInt32("item_id");
                            template.Amount = reader.GetInt32("amount", 1); //We always want to cost at least 1 item ?
                            template.MainGrade = reader.GetBoolean("main_grade");

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
                            if (!_crafts.ContainsKey(craftId))
                                continue;
                            _crafts[craftId].IsPack = true;
                        }
                    }
                }
            }

            _log.Info("Loaded crafts", _crafts.Count);
        }

        public Craft GetCraftById(uint craftId)
        {
            return _crafts[craftId];
        }
    }
}
