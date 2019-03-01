using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class HousingManager : Singleton<HousingManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, HousingTemplate> _housing;
        private Dictionary<uint, HousingAreas> _housingAreas;
        private Dictionary<uint, HouseTaxes> _houseTaxes;
        private Dictionary<uint, HousingBuildStep> _buildStep;

        public House Create(uint id, uint objectId = 0, ushort tlId = 0)
        {
            if (!_housing.ContainsKey(id))
                return null;

            var template = _housing[id];
            var house = new House();
            house.TlId = tlId > 0 ? tlId : (ushort)TlIdManager.Instance.GetNextId(); // TODO что то придумать...
            house.ObjId = objectId > 0 ? objectId : ObjectIdManager.Instance.GetNextId();
            house.TemplateId = id;
            house.Template = template;
            house.ModelId = _housing[id].BuildSteps[0].ModelId; //always starts at 0
            house.Faction = FactionManager.Instance.GetFaction(1); // TODO frandly
            house.Name = template.Name;
            house.Level = 1;
            house.MaxHp = house.Hp = template.Hp;
            house.BuildStep = -3; //need to count the number of steps according to housing_id

            return house;
        }

        public void Load()
        {
            _housing = new Dictionary<uint, HousingTemplate>();
            _housingAreas = new Dictionary<uint, HousingAreas>();
            _houseTaxes = new Dictionary<uint, HouseTaxes>();
            _buildStep = new Dictionary<uint, HousingBuildStep>();

            _log.Info("Loading Housing...");

            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM housings";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new HousingTemplate();
                            template.Id = reader.GetUInt32("id");
                            template.Name = reader.GetString("name");
                            template.CategoryId = reader.GetUInt32("category_id");
                            template.MainModelId = reader.GetUInt32("main_model_id");
                            template.DoorModelId = reader.GetUInt32("door_model_id", 0);
                            template.StairModelId = reader.GetUInt32("stair_model_id", 0);
                            template.AutoZ = reader.GetBoolean("auto_z", true);
                            template.GateExists = reader.GetBoolean("gate_exists", true);
                            template.Hp = reader.GetInt32("hp");
                            template.RepairCost = reader.GetUInt32("repair_cost");
                            template.GardenRadius = reader.GetFloat("garden_radius");
                            template.Family = reader.GetString("family");
                            template.TaxationId = reader.GetUInt32("taxation_id");
                            template.GuardTowerSettingId = reader.GetUInt32("guard_tower_setting_id", 0);
                            template.CinemaRadius = reader.GetFloat("cinema_radius");
                            template.AutoZOffsetX = reader.GetFloat("auto_z_offset_x");
                            template.AutoZOffsetY = reader.GetFloat("auto_z_offset_y");
                            template.AutoZOffsetZ = reader.GetFloat("auto_z_offset_z");
                            template.Alley = reader.GetFloat("alley");
                            template.ExtraHeightAbove = reader.GetFloat("extra_height_above");
                            template.ExtraHeightBelow = reader.GetFloat("extra_height_below");
                            template.DecoLimit = reader.GetUInt32("deco_limit");
                            template.AbsoluteDecoLimit = reader.GetUInt32("absolute_deco_limit");
                            template.HousingDecoLimitId = reader.GetUInt32("housing_deco_limit_id", 0);
                            template.IsSellable = reader.GetBoolean("is_sellable", true);
                            _housing.Add(template.Id, template);

                            using (var command2 = connection.CreateCommand())
                            {
                                command2.CommandText =
                                    "SELECT * FROM housing_binding_doodads WHERE owner_id=@owner_id AND owner_type='Housing'";
                                command2.Prepare();
                                command2.Parameters.AddWithValue("owner_id", template.Id);
                                using (var reader2 = new SQLiteWrapperReader(command2.ExecuteReader()))
                                {
                                    var doodads = new List<HousingBindingDoodad>();
                                    while (reader2.Read())
                                    {
                                        var bindingDoodad = new HousingBindingDoodad();
                                        bindingDoodad.AttachPointId = reader2.GetUInt32("attach_point_id");
                                        bindingDoodad.DoodadId = reader2.GetUInt32("doodad_id");

                                        doodads.Add(bindingDoodad);
                                    }

                                    template.HousingBindingDoodad = doodads.ToArray();
                                }
                            }
                          

                        }
                    }
                }
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM housing_build_steps";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var housingId = reader.GetUInt32("housing_id");
                            if (!_housing.ContainsKey(housingId))
                            continue;

                            var template = new HousingBuildStep();
                            template.Id = reader.GetUInt32("id");
                            template.HousingId = housingId;
                            template.Step = reader.GetInt16("step");
                            template.ModelId = reader.GetUInt32("model_id");
                            template.SkillId = reader.GetUInt32("skill_id");
                            template.NumActions = reader.GetInt32("num_actions");

                            _housing[housingId].BuildSteps.Add(template.Step, template);
                        }
                    }
                }


                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM housing_areas";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new HousingAreas();
                            template.Id = reader.GetUInt32("id");
                            template.Name = reader.GetString("name");
                            template.GroupId = reader.GetUInt32("housing_group_id");
                            _housingAreas.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM taxations";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new HouseTaxes();
                            template.Id = reader.GetUInt32("id");
                            template.Tax = reader.GetUInt32("tax");
                            template.Desc = reader.GetString("desc");
                            template.Show = reader.GetBoolean("show", true);
                            _houseTaxes.Add(template.Id, template);
                        }
                    }
                }
                _log.Info("Loaded Housing", _housing.Count);
            }
        }

        public void GetTax(Character builder, uint designId)
        {
            var TaxFromDB = _houseTaxes[_housing[designId].TaxationId].Tax;
            int BaseTax = Convert.ToInt32(TaxFromDB);
            int HouseNum = 0;
            if (HouseNum == 0)
            {
                int TotalTax = BaseTax;

                SCConstructHouseTaxPacket houseTaxPacket =
                    new SCConstructHouseTaxPacket(designId, 0, HouseNum, false, BaseTax, TotalTax, 111, 222);
                builder.SendPacket(houseTaxPacket);
            }
        }
        public HousingBuildStep GetHousingBuildStep(uint HousingId)
        {
            return _buildStep[HousingId];
        }
    }
}
