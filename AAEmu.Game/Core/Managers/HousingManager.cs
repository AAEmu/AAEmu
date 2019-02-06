using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Utils.DB;
using NLog;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Commons.Network;
using System;
using AAEmu.Game.Models.Game.Char;
using System.Collections.Generic;
using System.Text;

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
                            template.designId = reader.GetUInt32("id");
                            template.name = reader.GetString("name");
                            template.categoryId = reader.GetUInt32("category_id");
                            template.mainmodelId = reader.GetUInt32("main_model_id");
                            //template.doormodelId = reader.GetUInt32("door_model_id");
                            //template.stairmodelId = reader.GetUInt32("stair_model_id");
                            template.autoZ = reader.GetBoolean("auto_z", true);
                            template.gateexists = reader.GetBoolean("gate_exists", true);
                            template.hp = reader.GetUInt32("hp");
                            template.repaircost = reader.GetUInt32("repair_cost");
                            template.gardenraidus = reader.GetFloat("garden_radius");
                            template.family = reader.GetString("family");
                            template.taxationId = reader.GetUInt32("taxation_id");
                            //template.cinemaId = reader.GetUInt32("cinema_id");
                            template.cinemaradius = reader.GetFloat("cinema_radius");
                            template.autoZoffsetX = reader.GetFloat("auto_z_offset_x");
                            template.autoZoffsetY = reader.GetFloat("auto_z_offset_y");
                            template.autoZoffsetZ = reader.GetFloat("auto_z_offset_z");
                            template.alley = reader.GetFloat("alley");
                            template.extrahighetabove = reader.GetFloat("extra_height_above");
                            template.extrahighetbelow = reader.GetFloat("extra_height_below");
                            template.decolimit = reader.GetUInt32("deco_limit");
                            //template.comments = reader.GetString("comments");
                            template.absolutedecolimit = reader.GetUInt32("absolute_deco_limit");
                            //template.housingdecolimitId = reader.GetUInt32("housing_deco_limit_id");
                            template.isSellable = reader.GetBoolean("is_sellable", true);
                            _housing.Add(template.designId, template);

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
                            template.name = reader.GetString("name");
                            template.groupid = reader.GetUInt32("housing_group_id");
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
                            template.housingId = reader.GetUInt32("housing_id");
                            template.steps = reader.GetUInt32("step");
                            template.modelId = reader.GetUInt32("model_id");
                            template.housingskillID = reader.GetUInt32("skill_id");
                            template.numberofActions = reader.GetUInt32("num_actions");

                            if (!_housingSteps.ContainsKey(template.housingId))
                            {
                                _housingSteps.Add(template.housingId, new List<HousingSteps>());
                            }
                            _housingSteps[template.housingId].Add(template);

                        }
                    }

                }
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM taxations";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while(reader.Read())
                        {
                            var template = new HouseTaxes();
                            template.Id = reader.GetUInt32("id");
                            template.tax = reader.GetUInt32("tax");
                            template.desc = reader.GetString("desc");
                            template.show = reader.GetBoolean("show", true);
                            _houseTaxes.Add(template.Id, template);
                        }
                    }
                }
                
            }

            _log.Info("Loaded Housing", _housingAreas.Count);

        }
       public void GetTax(Character builder, uint designId)
        {
            var taxfromdb = _houseTaxes[_housing[designId].taxationId].tax;
            int btax = Convert.ToInt32(taxfromdb);
            int housenum = 0;
            if (housenum == 0)
            {
                int ttax = btax;

                SCConstructHouseTaxPacket houseTaxPacket = new SCConstructHouseTaxPacket(designId, housenum, btax, ttax);
                builder.SendPacket(houseTaxPacket);
            }
        }
  
    }
   

}
