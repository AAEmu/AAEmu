using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers.UnitManagers
{
    public class DoodadManager : Singleton<DoodadManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, DoodadTemplate> _templates;
        private Dictionary<uint, List<DoodadFunc>> _funcs;
        private Dictionary<uint, List<DoodadFunc>> _phaseFuncs;
        private Dictionary<string, Dictionary<uint, DoodadFuncTemplate>> _funcTemplates;

        public void Load()
        {
            _templates = new Dictionary<uint, DoodadTemplate>();
            _funcs = new Dictionary<uint, List<DoodadFunc>>();
            _phaseFuncs = new Dictionary<uint, List<DoodadFunc>>();
            _funcTemplates = new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>();
            foreach (var type in Helpers.GetTypesInNamespace("AAEmu.Game.Models.Game.DoodadObj.Funcs"))
                if (type.BaseType == typeof(DoodadFuncTemplate))
                    _funcTemplates.Add(type.Name, new Dictionary<uint, DoodadFuncTemplate>());

            using (var connection = SQLite.CreateConnection())
            {
                _log.Info("Loading doodad templates...");
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * from doodad_almighties";
                    command.Prepare();
                    using (var sqliteDataReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                    {
                        while (reader.Read())
                        {
                            var template = new DoodadTemplate();
                            template.Id = reader.GetUInt32("id");
                            template.OnceOneMan = reader.GetBoolean("once_one_man", true);
                            template.OnceOneInteraction = reader.GetBoolean("once_one_interaction", true);
                            template.MgmtSpawn = reader.GetBoolean("mgmt_spawn", true);
                            template.Percent = reader.IsDBNull("percent") ? 0 : reader.GetInt32("percent");
                            template.MinTime = reader.IsDBNull("min_time") ? 0 : reader.GetInt32("min_time");
                            template.MaxTime = reader.IsDBNull("max_time") ? 0 : reader.GetInt32("max_time");
                            template.ModelKindId = reader.GetUInt32("model_kind_id");
                            template.UseCreatorFaction = reader.GetBoolean("use_creator_faction", true);
                            template.ForceTodTopPriority = reader.GetBoolean("force_tod_top_priority", true);
                            template.MilestoneId = reader.IsDBNull("milestone_id") ? 0 : reader.GetUInt32("milestone_id");
                            template.GroupId = reader.GetUInt32("group_id");
                            template.UseTargetDecal = reader.GetBoolean("use_target_decal", true);
                            template.UseTargetSilhouette = reader.GetBoolean("use_target_silhouette", true);
                            template.UseTargetHighlight = reader.GetBoolean("use_target_highlight", true);
                            template.TargetDecalSize = reader.IsDBNull("target_decal_size") ? 0 : reader.GetFloat("target_decal_size");
                            template.SimRadius = reader.IsDBNull("sim_radius") ? 0 : reader.GetInt32("sim_radius");
                            template.CollideShip = reader.GetBoolean("collide_ship", true);
                            template.CollideVehicle = reader.GetBoolean("collide_vehicle", true);
                            template.ClimateId = reader.IsDBNull("climate_id") ? 0 : reader.GetUInt32("climate_id");
                            template.SaveIndun = reader.GetBoolean("save_indun", true);
                            template.ForceUpAction = reader.GetBoolean("force_up_action", true);
                            template.Parentable = reader.GetBoolean("parentable", true);
                            template.Childable = reader.GetBoolean("childable", true);
                            template.FactionId = reader.GetUInt32("faction_id");
                            template.GrowthTime = reader.IsDBNull("growth_time") ? 0 : reader.GetInt32("growth_time");
                            template.DespawnOnCollision = reader.GetBoolean("despawn_on_collision", true);
                            template.NoCollision = reader.GetBoolean("no_collision", true);
                            // TODO 1.2 template.RestrictZoneId = reader.IsDBNull("restrict_zone_id") ? 0 : reader.GetUInt32("restrict_zone_id");

                            using (var commandChild = connection.CreateCommand())
                            {
                                commandChild.CommandText =
                                    "SELECT * FROM doodad_func_groups WHERE doodad_almighty_id = @doodad_almighty_id";
                                commandChild.Prepare();
                                commandChild.Parameters.AddWithValue("doodad_almighty_id", template.Id);
                                using (var sqliteDataReaderChild = commandChild.ExecuteReader())
                                using (var readerChild = new SQLiteWrapperReader(sqliteDataReaderChild))
                                {
                                    while (readerChild.Read())
                                    {
                                        var funcGroups = new DoodadFuncGroups();
                                        funcGroups.Id = readerChild.GetUInt32("id");
                                        funcGroups.GroupKindId = readerChild.GetUInt32("doodad_func_group_kind_id");
                                        template.FuncGroups.Add(funcGroups);
                                    }
                                }
                            }

                            _templates.Add(template.Id, template);
                        }
                    }
                }

                _log.Info("Loaded {0} doodad templates", _templates.Count);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_funcs";
                    command.Prepare();
                    using (var sqliteDataReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFunc();
                            func.GroupId = reader.GetUInt32("doodad_func_group_id");
                            func.FuncId = reader.GetUInt32("actual_func_id");
                            func.FuncType = reader.GetString("actual_func_type");
                            func.NextPhase = reader.IsDBNull("next_phase") ? -1 : reader.GetInt32("next_phase");
                            func.SkillId = reader.IsDBNull("func_skill_id") ? 0 : reader.GetUInt32("func_skill_id");
                            func.PermId = reader.GetUInt32("perm_id");
                            func.Count = reader.IsDBNull("act_count") ? 0 : reader.GetInt32("act_count");
                            List<DoodadFunc> list;
                            if (_funcs.ContainsKey(func.GroupId))
                                list = _funcs[func.GroupId];
                            else
                            {
                                list = new List<DoodadFunc>();
                                _funcs.Add(func.GroupId, list);
                            }

                            list.Add(func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_phase_funcs";
                    command.Prepare();
                    using (var sqliteDataReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFunc();
                            func.GroupId = reader.GetUInt32("doodad_func_group_id");
                            func.FuncId = reader.GetUInt32("actual_func_id");
                            func.FuncType = reader.GetString("actual_func_type");
                            List<DoodadFunc> list;
                            if (_phaseFuncs.ContainsKey(func.GroupId))
                                list = _phaseFuncs[func.GroupId];
                            else
                            {
                                list = new List<DoodadFunc>();
                                _phaseFuncs.Add(func.GroupId, list);
                            }

                            list.Add(func);
                        }
                    }
                }
            }
        }

        public Doodad Create(uint bcId, uint id, Character character)
        {
            if (!_templates.ContainsKey(id))
                return null;
            var template = _templates[id];
            var doodad = new Doodad();
            doodad.BcId = bcId > 0 ? bcId : ObjectIdManager.Instance.GetNextId();
            doodad.TemplateId = template.Id;
            doodad.Template = template;
            doodad.OwnerBcId = character?.BcId ?? 0;
            doodad.OwnerId = character?.Id ?? 0;
            doodad.FuncGroupId = doodad.GetGroupId(); // TODO look, using doodadFuncId
            return doodad;
        }

        public DoodadFunc[] GetPhaseFunc(uint funcGroupId)
        {
            if (_phaseFuncs.ContainsKey(funcGroupId))
                return _phaseFuncs[funcGroupId].ToArray();
            return new DoodadFunc[0];
        }
    }
}