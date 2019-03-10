using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class HousingManager : Singleton<HousingManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, HousingTemplate> _housingTemplates;
        private Dictionary<uint, House> _houses;

        public Dictionary<uint, House> GetByAccountId(Dictionary<uint, House> values, uint accountId)
        {
            foreach (var (id, house) in _houses)
                if (house.AccountId == accountId)
                    values.Add(id, house);
            return values;
        }

        public House Create(uint templateId, uint objectId = 0, ushort tlId = 0)
        {
            if (!_housingTemplates.ContainsKey(templateId))
                return null;

            var template = _housingTemplates[templateId];

            var house = new House();
            house.TlId = tlId > 0 ? tlId : (ushort)TlIdManager.Instance.GetNextId(); // TODO что то придумать...
            house.ObjId = objectId > 0 ? objectId : ObjectIdManager.Instance.GetNextId();
            house.Template = template;
            house.TemplateId = template.Id;
            house.Faction = FactionManager.Instance.GetFaction(1); // TODO frandly
            house.Name = template.Name;
            house.Hp = house.MaxHp;
            house.CurrentStep = -1;

            return house;
        }

        public void Load()
        {
            _housingTemplates = new Dictionary<uint, HousingTemplate>();
            _houses = new Dictionary<uint, House>();

//            var housingAreas = new Dictionary<uint, HousingAreas>();
            var houseTaxes = new Dictionary<uint, HouseTax>();

            using (var connection = SQLite.CreateConnection())
            {
//                using (var command = connection.CreateCommand())
//                {
//                    command.CommandText = "SELECT * FROM housing_areas";
//                    command.Prepare();
//                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
//                    {
//                        while (reader.Read())
//                        {
//                            var template = new HousingAreas();
//                            template.Id = reader.GetUInt32("id");
//                            template.Name = reader.GetString("name");
//                            template.GroupId = reader.GetUInt32("housing_group_id");
//                            housingAreas.Add(template.Id, template);
//                        }
//                    }
//                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM taxations";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new HouseTax();
                            template.Id = reader.GetUInt32("id");
                            template.Tax = reader.GetUInt32("tax");
                            template.Show = reader.GetBoolean("show", true);
                            houseTaxes.Add(template.Id, template);
                        }
                    }
                }

                _log.Info("Loading Housing Templates...");
                
                var contents = FileManager.GetFileContents($"{FileManager.AppPath}Data/housing_bindings.json");
                if (string.IsNullOrWhiteSpace(contents))
                    throw new IOException(
                        $"File {FileManager.AppPath}Data/housing_bindings.json doesn't exists or is empty.");

                List<HousingBindingTemplate> binding;
                if (JsonHelper.TryDeserializeObject(contents, out binding, out _))
                    _log.Info("Housing bindings loaded...");
                else
                    _log.Warn("Housing bindings not loaded...");

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
                            var taxationId = reader.GetUInt32("taxation_id");
                            template.Taxation = houseTaxes.ContainsKey(taxationId) ? houseTaxes[taxationId] : null;
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
                            _housingTemplates.Add(template.Id, template);

                            var templateBindings = binding.Find(x => x.TemplateId.Contains(template.Id));
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
                                        
                                        if (templateBindings != null && 
                                            templateBindings.AttachPointId.ContainsKey(bindingDoodad.AttachPointId))
                                            bindingDoodad.Position = templateBindings
                                                .AttachPointId[bindingDoodad.AttachPointId].Clone();
                                        
                                        if (bindingDoodad.Position == null)
                                            bindingDoodad.Position = new Point(0, 0, 0);
                                        bindingDoodad.Position.WorldId = 1;
                                        
                                        doodads.Add(bindingDoodad);
                                    }

                                    template.HousingBindingDoodad = doodads.ToArray();
                                }
                            }
                        }
                    }
                }

                _log.Info("Loaded Housing Templates {0}", _housingTemplates.Count);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM housing_build_steps";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var housingId = reader.GetUInt32("housing_id");
                            if (!_housingTemplates.ContainsKey(housingId))
                                continue;

                            var template = new HousingBuildStep();
                            template.Id = reader.GetUInt32("id");
                            template.HousingId = housingId;
                            template.Step = reader.GetInt16("step");
                            template.ModelId = reader.GetUInt32("model_id");
                            template.SkillId = reader.GetUInt32("skill_id");
                            template.NumActions = reader.GetInt32("num_actions");

                            _housingTemplates[housingId].BuildSteps.Add(template.Step, template);
                        }
                    }
                }
            }

            _log.Info("Loading Housing...");
            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.CommandText = "SELECT * FROM housings";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var templateId = reader.GetUInt32("template_id");
                            var house = Create(templateId);
                            house.Id = reader.GetUInt32("id");
                            house.AccountId = reader.GetUInt32("account_id");
                            house.OwnerId = reader.GetUInt32("owner");
                            house.Position =
                                new Point(reader.GetFloat("x"), reader.GetFloat("y"), reader.GetFloat("z"));
                            house.Position.RotationX = reader.GetSByte("rotation_z");
                            house.Position.WorldId = 1;
                            house.CurrentStep = reader.GetInt32("current_step");
                            house.Permission = reader.GetByte("permission");
                            _houses.Add(house.Id, house);
                        }
                    }
                }
            }

            _log.Info("Loaded Housing {0}", _houses.Count);
        }

        public void Save()
        {
            using (var connection = MySQL.CreateConnection())
            {
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var house in _houses.Values)
                        house.Save(connection, transaction);

                    try
                    {
                        transaction.Commit();
                    }
                    catch (Exception e)
                    {
                        _log.Error(e);
                        try
                        {
                            transaction.Rollback();
                        }
                        catch (Exception eRollback)
                        {
                            _log.Error(eRollback);
                        }
                    }
                }
            }
        }

        public void SpawnAll()
        {
            foreach (var house in _houses.Values)
                house.Spawn();
        }

        public void ConstructHouseTax(GameConnection connection, uint designId, float x, float y, float z)
        {
            // TODO validation position and some range...

            var tax = _housingTemplates[designId].Taxation?.Tax ?? 0;

            connection.SendPacket(
                new SCConstructHouseTaxPacket(designId,
                    0,
                    0,
                    false,
                    (int)tax,
                    (int)tax,
                    0,
                    0)
            );
        }

        public void Build(GameConnection connection, uint designId, Point position, float zRot,
            ulong itemId, int moneyAmount, int ht, bool autoUseAaPoint)
        {
            // TODO validate house by range...
            // TODO remove itemId
            // TODO minus moneyAmount

            var zoneId = WorldManager.Instance.GetZoneId(1, position.X, position.Y);
            var house = Create(designId);
            house.Id = HousingIdManager.Instance.GetNextId();
            house.Position = position;
            house.Position.RotationZ = MathUtil.ConvertDegreeToDirection(zRot);
            
            house.Position.WorldId = 1;
            house.Position.ZoneId = zoneId;
            house.CurrentStep = 0;
            house.OwnerId = connection.ActiveChar.Id;
            house.AccountId = connection.AccountId;
            house.Permission = 2;
            _houses.Add(house.Id, house);

            connection.ActiveChar.SendPacket(new SCMyHousePacket(house));
            house.Spawn();
        }
    }
}
