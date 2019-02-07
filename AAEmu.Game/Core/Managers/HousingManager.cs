using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class HousingManager : Singleton<HousingManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, Housing> _housing;
        private Dictionary<uint, HousingAreas> _housingAreas;
        private Dictionary<uint, List<HousingSteps>> _housingSteps;
        private Dictionary<uint, HouseTaxes> _houseTaxes;


        public void Load()
        {
            _housing = new Dictionary<uint, Housing>();
            _housingSteps = new Dictionary<uint, List<HousingSteps>>();
            _housingAreas = new Dictionary<uint, HousingAreas>();
            _houseTaxes = new Dictionary<uint, HouseTaxes>();
            _log.Info("loading Housing Zones...");

            using (var connection = SQLite.CreateConnection())
            {
                //Housing
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM housings";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new Housing();
                            template.DesignId = reader.GetUInt32("id");
                            template.Name = reader.GetString("name");
                            template.CategoryId = reader.GetUInt32("category_id");
                            template.MainModelId = reader.GetUInt32("main_model_id");
                            //template.DoorModelId = reader.GetUInt32("door_model_id");
                            //template.StairModelId = reader.GetUInt32("stair_model_id");
                            template.AutoZ = reader.GetBoolean("auto_z", true);
                            template.GateExists = reader.GetBoolean("gate_exists", true);
                            template.Hp = reader.GetUInt32("hp");
                            template.RepairCost = reader.GetUInt32("repair_cost");
                            template.GardenRadius = reader.GetFloat("garden_radius");
                            template.Family = reader.GetString("family");
                            template.TaxationId = reader.GetUInt32("taxation_id");
                            //template.cinemaId = reader.GetUInt32("cinema_id");
                            template.CinemaRadius = reader.GetFloat("cinema_radius");
                            template.AutoZOffsetX = reader.GetFloat("auto_z_offset_x");
                            template.AutoZOffsetY = reader.GetFloat("auto_z_offset_y");
                            template.AutoZOffsetZ = reader.GetFloat("auto_z_offset_z");
                            template.Alley = reader.GetFloat("alley");
                            template.ExtraHeightAbove = reader.GetFloat("extra_height_above");
                            template.ExtraHeightBelow = reader.GetFloat("extra_height_below");
                            template.DecoLimit = reader.GetUInt32("deco_limit");
                            //template.comments = reader.GetString("comments");
                            template.AbsoluteDecoLimit = reader.GetUInt32("absolute_deco_limit");
                            //template.HousingDecoLimitId = reader.GetUInt32("housing_deco_limit_id");
                            template.IsSellable = reader.GetBoolean("is_sellable", true);
                            _housing.Add(template.DesignId, template);
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
                    command.CommandText = "SELECT * FROM housing_build_steps";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new HousingSteps();
                            template.Id = reader.GetUInt32("id");
                            template.HousingId = reader.GetUInt32("housing_id");
                            template.Steps = reader.GetUInt32("step");
                            template.ModelId = reader.GetUInt32("model_id");
                            template.HousingSkillID = reader.GetUInt32("skill_id");
                            template.NumberOfActions = reader.GetUInt32("num_actions");

                            if (!_housingSteps.ContainsKey(template.HousingId))
                            {
                                _housingSteps.Add(template.HousingId, new List<HousingSteps>());
                            }

                            _housingSteps[template.HousingId].Add(template);
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
            }

            _log.Info("Loaded Housing", _housingAreas.Count);
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
                    new SCConstructHouseTaxPacket(designId, HouseNum, BaseTax, TotalTax);
                builder.SendPacket(houseTaxPacket);
            }
        }
    }
}
