using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Housing;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class HousingManager : Singleton<HousingManager>
    {
        private const uint ForSaleMarkerDoodadId = 6760;
        
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, HousingTemplate> _housingTemplates;
        private Dictionary<uint, House> _houses;
        private Dictionary<ushort, House> _housesTl; // TODO or so mb tlId is id in the active zone? or type of house
        private List<uint> _removedHousings;
        private List<HousingItemHousings> _housingItemHousings;
        private Dictionary<uint, HousingDecoration> _housingDecorations;
        private List<ItemHousingDecoration> _housingItemHousingDecorations;
        private static int MAX_HEAVY_TAX_COUNTED = 10; // Maximum number of heavy tax buildings to take into account for tax calculation
        private bool isCheckingTaxTiming = false;

        /// <summary>
        /// Gets all houses for a given Account
        /// </summary>
        /// <param name="values"></param>
        /// <param name="accountId"></param>
        /// <returns></returns>
        public int GetByAccountId(Dictionary<uint, House> values, uint accountId)
        {
            foreach (var (id, house) in _houses)
                if (house.AccountId == accountId)
                    values.Add(id, house);
            return values.Count;
        }

        /// <summary>
        /// Gets all houses owned by Character
        /// </summary>
        /// <param name="values"></param>
        /// <param name="characterId"></param>
        /// <returns></returns>
        public int GetByCharacterId(Dictionary<uint, House> values, uint characterId)
        {
            foreach (var (id, house) in _houses)
                if (house.OwnerId == characterId)
                    values.Add(id, house);
            return values.Count;
        }

        public House Create(uint templateId, uint factionId, uint objectId = 0, ushort tlId = 0)
        {
            if (!_housingTemplates.ContainsKey(templateId))
                return null;

            var template = _housingTemplates[templateId];

            var house = new House();
            house.TlId = tlId > 0 ? tlId : (ushort)HousingTldManager.Instance.GetNextId();
            house.ObjId = objectId > 0 ? objectId : ObjectIdManager.Instance.GetNextId();
            house.Template = template;
            house.TemplateId = template.Id;
            house.Faction = FactionManager.Instance.GetFaction(factionId); // TODO: Inherit from owner
            house.Name = LocalizationManager.Instance.Get("housings", "name", template.Id);
            house.Hp = house.MaxHp;
            // Force public on always public properties on create
            if (template.AlwaysPublic)
                house.Permission = HousingPermission.Public;

            SetUntouchable(house, true);

            return house;
        }

        public void Load()
        {
            _housingTemplates = new Dictionary<uint, HousingTemplate>();
            _houses = new Dictionary<uint, House>();
            _housesTl = new Dictionary<ushort, House>();
            _removedHousings = new List<uint>();
            _housingItemHousings = new List<HousingItemHousings>();
            _housingDecorations = new Dictionary<uint, HousingDecoration>();
            _housingItemHousingDecorations = new List<ItemHousingDecoration>();

            // var housingAreas = new Dictionary<uint, HousingAreas>();
            // var houseTaxes = new Dictionary<uint, HouseTax>();

            using (var connection = SQLite.CreateConnection())
            {
                _log.Info("Loading Housing Information ...");

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_housings";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new HousingItemHousings();
                            template.Id = reader.GetUInt32("id");
                            template.Item_Id = reader.GetUInt32("item_id");
                            template.Design_Id = reader.GetUInt32("design_id");
                            _housingItemHousings.Add(template);
                        }
                    }
                }

                _log.Info("Loading Housing Templates...");

                var filePath = Path.Combine(FileManager.AppPath, "Data", "housing_bindings.json");
                var contents = FileManager.GetFileContents(filePath);
                if (string.IsNullOrWhiteSpace(contents))
                    throw new IOException(
                        $"File {filePath} doesn't exists or is empty.");

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
                            template.Name = LocalizationManager.Instance.Get("housings", "name", template.Id, reader.GetString("name"));
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
                            template.Taxation = TaxationsManager.Instance.taxations.ContainsKey(taxationId) ? TaxationsManager.Instance.taxations[taxationId] : null; 
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
                            template.HeavyTax = reader.GetBoolean("heavy_tax", true);
                            template.AlwaysPublic = reader.GetBoolean("always_public", true);
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
                                        bindingDoodad.AttachPointId = (AttachPointKind)reader2.GetInt16("attach_point_id");
                                        bindingDoodad.DoodadId = reader2.GetUInt32("doodad_id");

                                        if (templateBindings != null &&
                                            templateBindings.AttachPointId.ContainsKey(bindingDoodad.AttachPointId))
                                            bindingDoodad.Position = templateBindings
                                                .AttachPointId[bindingDoodad.AttachPointId].Clone();

                                        if (bindingDoodad.Position == null)
                                            bindingDoodad.Position = new WorldSpawnPosition();

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

                _log.Info("Loaded Decoration Templates...");

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM housing_decorations";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new HousingDecoration();
                            template.Id = reader.GetUInt32("id");
                            template.Name = reader.GetString("name");
                            template.AllowOnFloor = reader.GetBoolean("allow_on_floor", true);
                            template.AllowOnWall = reader.GetBoolean("allow_on_wall", true);
                            template.AllowOnCeiling = reader.GetBoolean("allow_on_ceiling", true);
                            template.DoodadId = reader.GetUInt32("doodad_id");
                            template.AllowPivotOnGarden = reader.GetBoolean("allow_pivot_on_garden", true);
                            template.ActabilityGroupId = !reader.IsDBNull("actability_group_id") ? reader.GetUInt32("actability_group_id") : 0;
                            template.ActabilityUp = !reader.IsDBNull("actability_up") ? reader.GetUInt32("actability_up") : 0;
                            template.DecoActAbilityGroupId = !reader.IsDBNull("deco_actability_group_id") ? reader.GetUInt32("deco_actability_group_id") : 0;
                            template.AllowMeshOnGarden = reader.GetBoolean("allow_mesh_on_garden", true);

                            _housingDecorations.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_housing_decorations";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new ItemHousingDecoration();
                            template.Id = reader.GetUInt32("id");
                            template.ItemId = reader.GetUInt32("item_id");
                            template.DesignId = reader.GetUInt32("design_id");
                            template.Restore = reader.GetBoolean("restore", true);

                            _housingItemHousingDecorations.Add(template);
                        }
                    }
                }
            }

            _log.Info("Loading Player Buildings ...");
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
                            var factionId = reader.GetUInt32("faction_id");
                            var house = Create(templateId, factionId);
                            house.Id = reader.GetUInt32("id");
                            house.AccountId = reader.GetUInt32("account_id");
                            house.OwnerId = reader.GetUInt32("owner");
                            house.CoOwnerId = reader.GetUInt32("co_owner");
                            house.Name = reader.GetString("name");
                            house.Transform = new Transform(house, null,
                                new Vector3(reader.GetFloat("x"), reader.GetFloat("y"), reader.GetFloat("z")),
                                new Vector3(reader.GetFloat("roll"), reader.GetFloat("pitch"), reader.GetFloat("yaw"))
                            );
                            house.Transform.ZoneId = WorldManager.Instance.GetZoneId(house.Transform.WorldId, house.Transform.World.Position.X, house.Transform.World.Position.Y);
                            house.CurrentStep = reader.GetInt32("current_step");
                            house.NumAction = reader.GetInt32("current_action");
                            house.Permission = (HousingPermission)reader.GetByte("permission");
                            house.PlaceDate = reader.GetDateTime("place_date");
                            house.ProtectionEndDate = reader.GetDateTime("protected_until");
                            house.SellToPlayerId = reader.GetUInt32("sell_to");
                            house.SellPrice = reader.GetUInt32("sell_price");
                            house.AllowRecover = reader.GetBoolean("allow_recover");
                            _houses.Add(house.Id, house);
                            _housesTl.Add(house.TlId, house);

                            // Manually placed houses (or after upgrading MySQL), will get 2 weeks for free as to not immediately trigger them into demolition
                            if (house.PlaceDate == house.ProtectionEndDate)
                                house.ProtectionEndDate = house.PlaceDate.AddDays(14);

                            UpdateTaxInfo(house);
                            house.IsDirty = false;
                        }
                    }
                }
            }

            _log.Info("Loaded {0} Player Buildings", _houses.Count);

            var houseCheckTask = new HousingTaxTask();
            TaskManager.Instance.Schedule(houseCheckTask, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));

            _log.Info("Started Housing Tax Timer");
        }

        public (int, int) Save(MySqlConnection connection, MySqlTransaction transaction)
        {
            var deleteCount = 0;
            lock (_removedHousings)
            {
                if (_removedHousings.Count > 0)
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText =
                            $"DELETE FROM housings WHERE id IN({string.Join(",", _removedHousings)})";
                        command.Prepare();
                        command.ExecuteNonQuery();
                        deleteCount++;
                    }

                    _removedHousings.Clear();
                }
            }

            var updateCount = 0;
            foreach (var house in _houses.Values)
                if (house.Save(connection, transaction))
                    updateCount++;

            return (updateCount, deleteCount);
        }

        public void SpawnAll()
        {
            foreach (var house in _houses.Values)
                house.Spawn();
        }

        /// <summary>
        /// Sets or removes the untouchable buff for the house
        /// </summary>
        /// <param name="house"></param>
        /// <param name="isUntouchable"></param>
        public void SetUntouchable(House house, bool isUntouchable)
        {
            if (isUntouchable)
            {
                if (house.Buffs.CheckBuff((uint)BuffConstants.Untouchable))
                    return;

                // Permanent Untouchable buff, should only be removed when failed tax payment, or demolishing by hand
                var protectionBuffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Untouchable);
                if (protectionBuffTemplate != null)
                {
                    var casterObj = new Models.Game.Skills.SkillCasterUnit(house.ObjId);
                    house.Buffs.AddBuff(new Models.Game.Skills.Buff(house, house, casterObj,
                        protectionBuffTemplate, null, System.DateTime.UtcNow));
                }
                else
                {
                    _log.Error("Unable to find Untouchable buff template");
                }
            }
            else
            {
                // Remove Untouchable if it's enabled
                if (house.Buffs.CheckBuff((uint)BuffConstants.Untouchable))
                    house.Buffs.RemoveBuff((uint)BuffConstants.Untouchable);
            }
        }

        /// <summary>
        /// Sets or removes the removal debuff for demolishing houses
        /// </summary>
        /// <param name="house"></param>
        /// <param name="isDeteriorating"></param>
        public void SetRemovalDebuff(House house, bool isDeteriorating)
        {
            if (isDeteriorating)
            {
                if (!house.Buffs.CheckBuff((uint)BuffConstants.RemovalDebuff))
                {
                    // Permanent Untouchable buff, should only be removed when failed tax payment, or demolishing by hand
                    var protectionBuffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.RemovalDebuff);
                    if (protectionBuffTemplate != null)
                    {
                        var casterObj = new Models.Game.Skills.SkillCasterUnit(house.ObjId);
                        house.Buffs.AddBuff(new Models.Game.Skills.Buff(house, house, casterObj,
                            protectionBuffTemplate, null, System.DateTime.UtcNow));
                    }
                    else
                    {
                        _log.Error("Unable to find Removal Debuff template");
                    }
                }
            }
            else
            {
                // Remove Untouchable if it's enabled
                if (house.Buffs.CheckBuff((uint)BuffConstants.RemovalDebuff))
                    house.Buffs.RemoveBuff((uint)BuffConstants.RemovalDebuff);
            }
        }

        public void ConstructHouseTax(GameConnection connection, uint designId, float x, float y, float z)
        {
            // TODO validation position and some range...

            var houseTemplate = _housingTemplates[designId];

            CalculateBuildingTaxInfo(connection.ActiveChar.AccountId, houseTemplate, true, out var totalTaxAmountDue, out var heavyTaxHouseCount, out var normalTaxHouseCount, out var hostileTaxRate, out _);

            var baseTax = (int)(houseTemplate.Taxation?.Tax ?? 0);
            var depositTax = baseTax * 2;

            connection.SendPacket(
                new SCConstructHouseTaxPacket(designId,
                    heavyTaxHouseCount,
                    normalTaxHouseCount,
                    houseTemplate.HeavyTax,
                    baseTax,
                    depositTax,
                    totalTaxAmountDue
                )
            );
        }

        /// <summary>
        /// Request house tax information (using name plaque of a house)
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="tlId"></param>
        public void HouseTaxInfo(GameConnection connection, ushort tlId)
        {
            if (!_housesTl.ContainsKey(tlId))
                return;

            var house = _housesTl[tlId];

            CalculateBuildingTaxInfo(house.AccountId, house.Template, false, out var totalTaxAmountDue, out var heavyTaxHouseCount, out var normalTaxHouseCount, out var hostileTaxRate, out _);

            var baseTax = (int)(house.Template.Taxation?.Tax ?? 0);
            var depositTax = baseTax * 2;

            // Note: I'm sure this can be done better, but it works and displays correctly
            var requiresPayment = false;
            var weeksWithoutPay = -1;
            if (house.TaxDueDate <= DateTime.UtcNow)
            {
                requiresPayment = true;
                weeksWithoutPay = 0;
            }
            else
            if (house.TaxDueDate <= DateTime.UtcNow)
            {
                requiresPayment = true;
                weeksWithoutPay = 1;
            }

            /*
            _log.Debug(
                "SCHouseTaxInfoPacket; tlId:{0}, domTaxRate:{1}, deposit: {2}, taxdue:{3}, protectEnd:{4}, isPaid:{5}, weeksWithoutPay:{6}, isHeavy:{7}",
                house.TlId, 0, depositTax, totalTaxAmountDue, house.ProtectionEndDate, requiresPayment, weeksWithoutPay, house.Template.HeavyTax);
            */
            connection.SendPacket(
                new SCHouseTaxInfoPacket(
                    house.TlId,
                    0,  // TODO: implement when castles are added
                    depositTax, // this is used in the help text on (?) when you hover your mouse over it to display deposit tax for this building
                    totalTaxAmountDue, // Amount Due
                    house.ProtectionEndDate,
                    requiresPayment,
                    weeksWithoutPay,  // TODO: do proper calculation ?
                    house.Template.HeavyTax
                )
            );
        }

        /// <summary>
        /// Start building a house at target location using design
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="designId"></param>
        /// <param name="posX"></param>
        /// <param name="posY"></param>
        /// <param name="posZ"></param>
        /// <param name="zRot"></param>
        /// <param name="itemId"></param>
        /// <param name="moneyAmount"></param>
        /// <param name="ht"></param>
        /// <param name="autoUseAaPoint"></param>
        public void Build(GameConnection connection, uint designId, float posX, float posY, float posZ, float zRot,
            ulong itemId, int moneyAmount, int ht, bool autoUseAaPoint)
        {
            // TODO validate house by range...
            // TODO remove itemId
            // TODO minus moneyAmount

            var sourceDesignItem = connection.ActiveChar.Inventory.GetItemById(itemId);
            if ((sourceDesignItem == null) && (sourceDesignItem.OwnerId != connection.ActiveChar.Id))
            {
                // Invalid itemId supplied or the id is not owned by the user
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.BagInvalidItem);
                return;
            }


            var zoneId = WorldManager.Instance.GetZoneId(connection.ActiveChar.Transform.WorldId, posX, posY);

            var houseTemplate = _housingTemplates[designId];
            CalculateBuildingTaxInfo(connection.ActiveChar.AccountId, houseTemplate, true, out var totalTaxAmountDue, out var heavyTaxHouseCount, out var normalTaxHouseCount, out var hostileTaxRate, out _);

            if (FeaturesManager.Fsets.Check(Models.Game.Features.Feature.taxItem))
            {
                // Pay in Tax Certificate

                var userTaxCount = connection.ActiveChar.Inventory.GetItemsCount(SlotType.Inventory, Item.TaxCertificate);
                var userBoundTaxCount = connection.ActiveChar.Inventory.GetItemsCount(SlotType.Inventory, Item.BoundTaxCertificate);
                var totalUserTaxCount = userTaxCount + userBoundTaxCount;
                var totalCertsCost = (int)Math.Ceiling(totalTaxAmountDue / 10000f);

                // Alloyingly complex item consumption, maybe we need a seperate function in inventory to handle this kind of thing
                var consumedCerts = totalCertsCost;
                if (totalCertsCost > totalUserTaxCount)
                {
                    connection.ActiveChar.SendErrorMessage(ErrorMessageType.MailNotEnoughMoneyToPayTaxes);
                    return;
                }
                else
                {
                    var c = consumedCerts;
                    // Use Bound First
                    if ((userBoundTaxCount > 0) && (c > 0))
                    {
                        if (c > userBoundTaxCount)
                            c = userBoundTaxCount;
                        connection.ActiveChar.Inventory.Bag.ConsumeItem(Models.Game.Items.Actions.ItemTaskType.HouseCreation, Item.BoundTaxCertificate, c, null);
                        consumedCerts -= c;
                    }
                    c = consumedCerts;
                    if ((userTaxCount > 0) && (c > 0))
                    {
                        if (c > userTaxCount)
                            c = userTaxCount;
                        connection.ActiveChar.Inventory.Bag.ConsumeItem(Models.Game.Items.Actions.ItemTaskType.HouseCreation, Item.TaxCertificate, c, null);
                        consumedCerts -= c;
                    }

                    if (consumedCerts != 0)
                        _log.Error("Something went wrong when paying tax for new building for player {0}", connection.ActiveChar.Name);
                }

            }
            else
            {
                // Pay in Gold
                // TODO: test house with actual gold tax
                if (totalTaxAmountDue > connection.ActiveChar.Money)
                {
                    connection.ActiveChar.SendErrorMessage(ErrorMessageType.MailNotEnoughMoneyToPayTaxes);
                    return;
                }
                connection.ActiveChar.SubtractMoney(SlotType.Inventory, totalTaxAmountDue, Models.Game.Items.Actions.ItemTaskType.HouseCreation);
            }


            if (connection.ActiveChar.Inventory.Bag.ConsumeItem(Models.Game.Items.Actions.ItemTaskType.HouseBuilding, sourceDesignItem.TemplateId, 1, sourceDesignItem) <= 0)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.BagInvalidItem);
                return;
            }

            // Spawn the actual house
            var house = Create(designId, connection.ActiveChar.Faction.Id);

            // Fallback for un-translated buildings (en_us)
            if (house.Name == string.Empty)
            {
                var fakeLocalizedName = LocalizationManager.Instance.Get("items", "name", sourceDesignItem.Template.Id, houseTemplate.Name);
                if (fakeLocalizedName.EndsWith(" Design"))
                    fakeLocalizedName = fakeLocalizedName.Replace(" Design", "");
                house.Name = fakeLocalizedName;
            }

            house.Id = HousingIdManager.Instance.GetNextId();
            house.Transform.Local.SetPosition(posX, posY, posZ);
            house.Transform.Local.SetZRotation(zRot);

            if (house.Template.BuildSteps.Count > 0)
                house.CurrentStep = 0;
            else
                house.CurrentStep = -1;
            house.OwnerId = connection.ActiveChar.Id;
            house.CoOwnerId = connection.ActiveChar.Id;
            house.AccountId = connection.AccountId;
            house.Permission = HousingPermission.Private;
            house.PlaceDate = DateTime.UtcNow;
            house.ProtectionEndDate = DateTime.UtcNow.AddDays(7);
            _houses.Add(house.Id, house);
            _housesTl.Add(house.TlId, house);
            connection.ActiveChar.SendPacket(new SCMyHousePacket(house));
            house.Spawn();
            UpdateTaxInfo(house);
        }

        /// <summary>
        /// Update house permission settings
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="tlId"></param>
        /// <param name="permission"></param>
        public void ChangeHousePermission(GameConnection connection, ushort tlId, HousingPermission permission)
        {
            if (!_housesTl.ContainsKey(tlId))
                return;
            var house = _housesTl[tlId];
            if (house.OwnerId != connection.ActiveChar.Id)
                return;

            switch (permission)
            {
                case HousingPermission.Guild when connection.ActiveChar.Expedition == null:
                case HousingPermission.Family when connection.ActiveChar.Family == 0:
                    return;
                case HousingPermission.Guild:
                    house.CoOwnerId = connection.ActiveChar.Expedition.Id;
                    break;
                case HousingPermission.Family:
                    house.CoOwnerId = connection.ActiveChar.Family;
                    break;
                default:
                    house.CoOwnerId = connection.ActiveChar.Id;
                    break;
            }

            house.Permission = permission;
            house.BroadcastPacket(new SCHousePermissionChangedPacket(tlId, (byte)permission), false);
            // connection.SendPacket(new SCHousePermissionChangedPacket(tlId, (byte)permission));
        }

        /// <summary>
        /// Rename house
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="tlId"></param>
        /// <param name="name"></param>
        public void ChangeHouseName(GameConnection connection, ushort tlId, string name)
        {
            if (!_housesTl.ContainsKey(tlId))
                return;
            var house = _housesTl[tlId];
            if (house.OwnerId != connection.ActiveChar.Id)
                return;

            house.Name = name.Substring(0, 1).ToUpper() + name.Substring(1);
            house.IsDirty = true; // Manually set the IsDirty on House level
            connection.SendPacket(new SCUnitNameChangedPacket(house.ObjId, house.Name));
        }

        /// <summary>
        /// Start demolishing of a house
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="house"></param>
        /// <param name="failedToPayTax"></param>
        /// <param name="forceRestoreAllDecor"></param>
        public void Demolish(GameConnection connection, House house, bool failedToPayTax, bool forceRestoreAllDecor)
        {
            if (!_houses.ContainsKey(house.Id))
            {
                connection?.ActiveChar?.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
                return;
            }
            // Check if owner
            if ((connection == null) || (house.OwnerId == connection.ActiveChar.Id))
            {
                // VERIFY: check if tax payed, cannot manually demolish or sell a house with unpaid taxes ?
                // Note - ZeromusXYZ: I'm disabling this "feature", as it would prevent you from demolishing freshly placed buildings that you want to move 
                /*
                if (house.TaxDueDate <= DateTime.UtcNow)
                {
                    connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotDemolishUnpaidTax);
                    return;
                }
                */
                var ownerChar = WorldManager.Instance.GetCharacterById(house.OwnerId);

                // Mark it as expired protection
                house.ProtectionEndDate = DateTime.UtcNow.AddSeconds(-1);
                // Make sure to call UpdateTaxInfo first to remove tax-rated mails of this house
                UpdateTaxInfo(house);
                // Return items to player by mail
                ReturnHouseItemsToOwner(house, failedToPayTax, forceRestoreAllDecor, null);

                // Remove owner
                house.OwnerId = 0;
                house.CoOwnerId = 0;
                house.AccountId = 0;
                house.SellPrice = 0;
                house.SellToPlayerId = 0;
                house.Permission = HousingPermission.Public;
                house.BroadcastPacket(new SCHouseDemolishedPacket(house.TlId), false);

                ownerChar?.SendPacket(new SCMyHouseRemovedPacket(house.TlId));
                // Make killable
                UpdateHouseFaction(house, (uint)FactionsEnum.Monstrosity);
                
                SetForSaleMarkers(house, false);
                
                house.IsDirty = true;

                // TODO: better house killing handling
                _removedHousings.Add(house.Id);
            }
            else
            {
                // Non-owner should not be able to press demolish
                connection?.ActiveChar?.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
                return;
            }
        }

        public void RemoveDeadHouse(House house)
        {
            // Remove house from housing tables
            _removedHousings.Add(house.Id);
            _houses.Remove(house.Id);
            _housesTl.Remove(house.TlId);
            HousingTldManager.Instance.ReleaseId(house.TlId);
            HousingIdManager.Instance.ReleaseId(house.Id);
            // TODO: not sure how to handle this, just insta-delete it for now
            house.Delete();
            // TODO: Add to despawn handler
            //house.Despawn = DateTime.UtcNow.AddSeconds(20);
            //SpawnManager.Instance.AddDespawn(house);
        }

        /// <summary>
        /// Helper function to calculate due tax
        /// </summary>
        /// <param name="AccountId"></param>
        /// <param name="newHouseTemplate"></param>
        /// <param name="buildingNewHouse"></param>
        /// <param name="totalTaxToPay"></param>
        /// <param name="heavyHouseCount"></param>
        /// <param name="normalHouseCount"></param>
        /// <param name="hostileTaxRate"></param>
        /// <param name="oneWeekTaxCount"></param>
        /// <returns></returns>
        public bool CalculateBuildingTaxInfo(uint AccountId, HousingTemplate newHouseTemplate, bool buildingNewHouse, out int totalTaxToPay, out int heavyHouseCount, out int normalHouseCount, out int hostileTaxRate, out int oneWeekTaxCount)
        {
            totalTaxToPay = 0;
            heavyHouseCount = 0;
            normalHouseCount = 0;
            hostileTaxRate = 0; // NOTE: When castles are added, this needs to be updated depending on ruling guild's settings
            oneWeekTaxCount = 0;

            Dictionary<uint, House> userHouses = new Dictionary<uint, House>();
            if (GetByAccountId(userHouses, AccountId) <= 0)
                return false;

            // Count the houses on this account
            foreach (var h in userHouses)
            {
                if (h.Value.Template.HeavyTax)
                    heavyHouseCount++;
                else
                    normalHouseCount++;
            }

            // If this is for a new building, add 1 to count
            if (buildingNewHouse)
            {
                if (newHouseTemplate.HeavyTax)
                    heavyHouseCount++;
                else
                    normalHouseCount++;
            }

            // Default Heavy Tax formula for 1.2
            var taxMultiplier = (heavyHouseCount < MAX_HEAVY_TAX_COUNTED ? heavyHouseCount : MAX_HEAVY_TAX_COUNTED) * 0.5f;
            // If less than 3 properties, or not a heavy tax property, no extra multiplier needed
            if ((heavyHouseCount < 3) || (newHouseTemplate.HeavyTax == false))
                taxMultiplier = 1f;

            totalTaxToPay = oneWeekTaxCount = (int)Math.Ceiling(newHouseTemplate.Taxation.Tax * taxMultiplier);

            // If this is a new house, add the deposit (base tax * 2)
            if (buildingNewHouse)
                totalTaxToPay += (int)(newHouseTemplate.Taxation.Tax * 2);

            return true;
        }

        /// <summary>
        /// This function updates related tax mails of a house (if needed)
        /// </summary>
        /// <param name="house"></param>
        public void UpdateTaxInfo(House house)
        {
            var isDemolished = (house.ProtectionEndDate <= DateTime.UtcNow);
            var isTaxDue = (house.TaxDueDate <= DateTime.UtcNow);

            // Update Buffs (if needed)
            SetUntouchable(house, !isDemolished);
            SetRemovalDebuff(house, isDemolished);

            if (house.OwnerId <= 0)
                return;

            // If expired, start demolition debuffs
            if (isDemolished)
            {
                MailManager.Instance.DeleteHouseMails(house.Id);
            }
            else
            if (isTaxDue)
            {
                // TODO: update corresponding mails if needed (like update weeks unpaid etc)
                var allMails = MailManager.Instance.GetMyHouseMails(house.Id);

                if (allMails.Count <= 0)
                {
                    // Create new tax mail
                    var newMail = new MailForTax(house);
                    newMail.FinalizeMail();
                    newMail.Send();
                    _log.Trace("New Tax Mail sent for {0} owned by {1}", house.Name, house.OwnerId);
                }
                else
                {
                    foreach (var mail in allMails)
                    {
                        MailForTax.UpdateTaxInfo(mail, house);
                        _log.Trace("Tax Mail {0} updated for {1} ({2}) owned by {3}", mail.Id, house.Name, house.Id,
                            house.OwnerId);
                    }
                }

            }
        }

        /// <summary>
        /// Adds a week to the protection end date (pay 1 week's tax)
        /// </summary>
        /// <param name="house"></param>
        /// <returns></returns>
        public bool PayWeeklyTax(House house)
        {
            house.ProtectionEndDate = house.ProtectionEndDate.AddDays(7);
            return true;
        }

        public House GetHouseById(uint houseId)
        {
            return _houses.TryGetValue(houseId, out var house) ? house : null;
        }

        public House GetHouseByTlId(ushort houseTlId)
        {
            return _housesTl.TryGetValue(houseTlId, out var house) ? house : null;
        }

        /// <summary>
        /// Changes the faction of the house
        /// </summary>
        /// <param name="house"></param>
        /// <param name="factionId"></param>
        public void UpdateHouseFaction(House house, uint factionId)
        {
            house.BroadcastPacket(new SCUnitFactionChangedPacket(house.ObjId, house.Name, house.Faction?.Id ?? 0, factionId, false), true);
            house.Faction = FactionManager.Instance.GetFaction(factionId);
        }

        /// <summary>
        /// Helper function for when the owning character changes faction
        /// </summary>
        /// <param name="characterId"></param>
        /// <param name="factionId"></param>
        public void UpdateOwnedHousingFaction(uint characterId, uint factionId)
        {
            var myHouses = new Dictionary<uint, House>();
            GetByCharacterId(myHouses, characterId);
            foreach (var h in myHouses)
                if ((h.Value.Faction == null) || (h.Value.Faction.Id != factionId))
                    UpdateHouseFaction(h.Value, factionId);
        }

        /// <summary>
        /// Returns furniture of a house that's being demolished or sold
        /// </summary>
        /// <param name="house"></param>
        /// <param name="failedToPayTax">Set true if demilishing due to failed tax, this adds a delay to the mail</param>
        /// <param name="forceRestoreAllDecor">For GM commands or server merges. Will try to send ALL placed furniture if set to true, even those that normally don't get returned.</param>
        /// <param name="newOwner">New owner Character if buying, otherwise leave null</param>
        public void ReturnHouseItemsToOwner(House house, bool failedToPayTax, bool forceRestoreAllDecor, Character newOwner)
        {
            if (house.OwnerId <= 0)
                return;

            var returnedItems = new List<Item>();
            var returnedMoney = 0;

            // If returning items because of a new House Owner, then don't include the design
            if (newOwner == null)
            {
                // TODO: proper grades for design
                // TODO for future versions: Support Full-Kit demolition
                var designItemId = GetItemIdByDesign(house.Template.Id);
                var designItem = ItemManager.Instance.Create(designItemId, 1, 0);
                var designTemplate = ItemManager.Instance.GetTemplate(designItemId);
                designItem.Grade = (designTemplate.FixedGrade >= 0) ? (byte)designTemplate.FixedGrade : (byte)0 ;
                designItem.OwnerId = house.OwnerId;
                designItem.SlotType = SlotType.Mail;
                returnedItems.Add(designItem);

                // Return taxes
                if (!failedToPayTax)
                {
                    if (FeaturesManager.Fsets.Check(Models.Game.Features.Feature.taxItem))
                    {
                        var taxItem = ItemManager.Instance.Create(Item.BoundTaxCertificate, (int)(house.Template.Taxation.Tax / 5000), 0);
                        taxItem.OwnerId = house.OwnerId;
                        taxItem.SlotType = SlotType.Mail;
                        returnedItems.Add(taxItem);
                    }
                    else
                    {
                        returnedMoney = (int)(house.Template.Taxation.Tax * 2);
                    }
                }
            }

            var furniture = WorldManager.Instance.GetDoodadByHouseDbId(house.Id);
            foreach (var f in furniture)
            {
                // Ignore attached objects (those are doors/windows etc)
                if (f.AttachPoint != AttachPointKind.None)
                    continue;
                
                // Ignore for sale signs
                if (f.TemplateId == ForSaleMarkerDoodadId)
                    continue;

                var decoDesign = GetDecorationDesignFromDoodadId(f.TemplateId);
                if (decoDesign == null)
                {
                    // Is not furniture, probably plants or backpacks
                    f.Transform.DetachAll();
                    f.ParentObjId = 0;
                    f.ParentObj = null;
                    f.DbHouseId = 0;
                    // TODO: probably needs to send a packet as well here
                    continue;
                }

                var decoInfo = _housingItemHousingDecorations.FirstOrDefault(x => x.DesignId == decoDesign.Id);
                if (decoInfo == null)
                {
                    // No design info for this item ? Just detach it for now
                    f.Transform.DetachAll();
                    f.ParentObjId = 0;
                    f.ParentObj = null;
                    f.DbHouseId = 0;
                    _log.Warn("ReturnHouseItemsToOwner - Furniture doesn't have design info for Doodad Id:{0} Template:{1}", f.ObjId, f.TemplateId);
                    continue;
                }

                var thisDoodadsItem = ItemManager.Instance.GetItemByItemId(f.ItemId);
                var returnedThisItem = false;

                var wantReturned = (newOwner == null) && decoInfo.Restore;
                if (forceRestoreAllDecor)
                    wantReturned = true;
                
                // If item is bound, always return it owner
                if (f.ItemId > 0)
                {
                    var item = ItemManager.Instance.GetItemByItemId(f.ItemId);
                    if (item.ItemFlags.HasFlag(ItemFlag.SoulBound))
                        wantReturned = true;
                }

                // If the decoration item isn't marked as Restore, then just delete it (and it's possibly attached item)
                if (!wantReturned)
                {
                    // Non-restore-able item
                    if (newOwner == null)
                    {
                        // Just delete the doodad and attached item if no new owner
                        // Delete the attached item
                        if (f.ItemId != 0)
                            thisDoodadsItem._holdingContainer?.ConsumeItem(ItemTaskType.Invalid,
                                thisDoodadsItem.TemplateId, thisDoodadsItem.Count, thisDoodadsItem);

                        // Is furniture, but doesn't restore, destroy it
                        f.Transform.DetachAll();
                        f.Delete();
                    }
                    else
                    {
                        // Move the doodad and item to the new owner
                        if (f.ItemId != 0)
                        {
                            // If a single item is attached, change it's owner and location
                            var item = ItemManager.Instance.GetItemByItemId(f.ItemId);
                            newOwner.Inventory.SystemContainer.AddOrMoveExistingItem(ItemTaskType.Invalid, item);
                        }
                        // Change doodad owner
                        f.OwnerId = newOwner.Id;
                    }

                    continue;
                }
                
                // Item needs to be actually returned, so let's do that
                if (f.ItemId > 0)
                {
                    // Ignore if it's not in a System container for whatever reason
                    if ((thisDoodadsItem != null) && (thisDoodadsItem.SlotType == SlotType.System))
                    {
                        returnedItems.Add(thisDoodadsItem);
                        returnedThisItem = true;
                    }
                }
                else
                if (f.ItemTemplateId > 0)
                {
                    // try to stack stackable items
                    var oldItem = returnedItems.FirstOrDefault(x => (x.TemplateId == f.ItemTemplateId) && (x.Count < x.Template.MaxCount));

                    if (oldItem != null)
                    {
                        oldItem.Count++;
                    }
                    else
                    {
                        // It's a new one, add a item slot
                        var furnitureItem = ItemManager.Instance.Create(f.ItemTemplateId, 1, 0);
                        var furnitureTemplate = ItemManager.Instance.GetTemplate(f.ItemTemplateId);
                        furnitureItem.Grade = (furnitureTemplate.FixedGrade >= 0) ? (byte)furnitureTemplate.FixedGrade : (byte)0 ;
                        furnitureItem.OwnerId = house.OwnerId;
                        furnitureItem.SlotType = SlotType.Mail;
                        returnedItems.Add(furnitureItem);
                    }
                    returnedThisItem = true;
                }
                else
                {
                    // Not sure what happened here, just ignore it
                    continue;
                }

                // Set new doodad owner if needed
                if (newOwner != null)
                    f.OwnerId = newOwner.Id;

                if ((newOwner == null) || returnedThisItem)
                {
                    f.Transform.DetachAll();
                    f.Delete();
                }
            }

            // TODO: Grab a list of items in chests

            // TODO: Proper Mail handler
            BaseMail newMail = null;
            for (var i = 0; i < returnedItems.Count; i++)
            {
                // Split items into mails of maximum 10 attachemnts
                if ((i % 10) == 0)
                {
                    // TODO: proper mail handler
                    newMail = new BaseMail();
                    newMail.MailType = MailType.Demolish;
                    newMail.ReceiverName = NameManager.Instance.GetCharacterName(house.OwnerId); // Doesn't seem like this needs to be set
                    newMail.Header.ReceiverId = house.OwnerId;
                    newMail.Header.SenderId = 0;
                    newMail.Header.SenderName = ".houseDemolish";
                    newMail.Title = "title";
                    newMail.Body.Text = "body"; // Yes, that's indeed what it needs to be set to
                    newMail.Body.SendDate = DateTime.UtcNow;
                    if (failedToPayTax)
                        newMail.Body.RecvDate = DateTime.UtcNow.AddHours(22);
                    else
                        newMail.Body.RecvDate = DateTime.UtcNow;
                    newMail.Header.Extra = house.Id;
                }
                // Only attach money to first mail
                if ((returnedMoney > 0) && (i == 0))
                    newMail.AttachMoney(returnedMoney);

                // If player is loaded in at the moment (which he/she should be anyway), directly manipulate the inventory
                // If not, only change the container
                var onlineOwner = WorldManager.Instance.GetCharacterById((uint)returnedItems[i].OwnerId);
                if (onlineOwner != null)
                    onlineOwner.Inventory.MailAttachments.AddOrMoveExistingItem(ItemTaskType.Invalid, returnedItems[i]);
                else
                    returnedItems[i].SlotType = SlotType.Mail;

                // Attach item
                newMail.Body.Attachments.Add(returnedItems[i]);

                // Send on last or 10th item of the mail
                if (((i % 10) == 9) || (i == returnedItems.Count - 1))
                    newMail.Send();
            }

            if (newMail != null)
            {
                _log.Trace("Demolition mail sent to {0}", newMail.ReceiverName);
            }
        }

        public uint GetDesignByItemId(uint itemId)
        {
            var design = _housingItemHousings.First(h => h.Item_Id == itemId);
            return design?.Design_Id ?? 0;
        }

        public uint GetItemIdByDesign(uint designId)
        {
            var design = _housingItemHousings.First(h => h.Design_Id == designId);
            return design?.Item_Id ?? 0;
        }

        /// <summary>
        /// Helper function to calculate how many Appraisal Certificates are needed to sell a house at a given price
        /// </summary>
        /// <param name="house"></param>
        /// <param name="salePrice"></param>
        /// <returns></returns>
        public int CalculateSaleCertifcates(House house, uint salePrice)
        {
            // NOTE: In earlier AA, you need 1 appraisal certificate for every 100 gold of sales price
            // TODO: In later versions, this depends on the building-type/size
            var certAmount = (int)Math.Ceiling(salePrice / 1000000f);
            if (certAmount < 1)
                certAmount = 1;
            return certAmount;
        }

        /// <summary>
        /// Sets or removes For Sale Signs on the property
        /// </summary>
        /// <param name="house"></param>
        /// <param name="isForSale"></param>
        private void SetForSaleMarkers(House house, bool isForSale)
        {
            if (isForSale)
            {
                for (int postId = 0; postId < 4; postId++)
                {
                    var xMultiplier = (postId % 2) == 0 ? -1 : 1f;
                    var yMultiplier = (postId / 2) == 0 ? -1 : 1f;
                    var zRot = ((135f + (90f * postId) % 360)).DegToRad(); 
                    
                    var doodad = DoodadManager.Instance.Create(0, ForSaleMarkerDoodadId);
                    // location
                    doodad.Transform.Local.SetPosition(
                        (house.Template.GardenRadius * xMultiplier) + house.Transform.World.Position.X,
                        (house.Template.GardenRadius * yMultiplier) + house.Transform.World.Position.Y,
                        +house.Transform.World.Position.Z);
                    // adjust height to the floor
                    doodad.Transform.Local.SetHeight(WorldManager.Instance.GetHeight(doodad.Transform));
                    doodad.Transform.Local.SetZRotation(zRot);
                    doodad.ItemTemplateId = 0; // designId;
                    doodad.ItemId = 0;
                    doodad.OwnerId = 0;
                    doodad.ParentObjId = 0;
                    doodad.ParentObj = null;
                    doodad.UccId = 0;
                    doodad.AttachPoint = AttachPointKind.None;
                    doodad.OwnerType = DoodadOwnerType.Housing;
                    doodad.DbHouseId = house.Id;

                    doodad.Spawn();
                }
                
            }
            else
            {
                // Get all doodads related to this house
                var thisHouseSalePosts = WorldManager.Instance.GetDoodadByHouseDbId(house.Id);
                for(var c = thisHouseSalePosts.Count-1 ; c >= 0; c--)
                {
                    var doodad = thisHouseSalePosts[c];
                    // If it's a for sale sign, remove it
                    if (doodad.TemplateId == ForSaleMarkerDoodadId)
                    {
                        house.AttachedDoodads.Remove(doodad);
                        doodad.Delete();
                    }
                }
            }
        }
        
        public bool SetForSale(ushort houseTlId, uint price, uint buyerId, Character seller) =>
            SetForSale(GetHouseByTlId(houseTlId), price, buyerId, seller);

        /// <summary>
        /// Puts up a house for sale
        /// </summary>
        /// <param name="house"></param>
        /// <param name="price"></param>
        /// <param name="buyerId">Use CharacterId for selling to a specific person</param>
        /// <param name="seller">Current owner of the property (needed to manipulate inventory)</param>
        /// <returns></returns>
        public bool SetForSale(House house, uint price, uint buyerId, Character seller)
        {
            if (house == null)
                return false;

            if (!house.Template.IsSellable)
                return false;

            // Check if buyer exists (we just check if the name exists)
            var buyerName = NameManager.Instance.GetCharacterName(buyerId);
            if ((buyerId != 0) && (buyerName == null))
                return false;

            if (buyerName == null)
                buyerName = "";

            // Using the GM command does not send the seller (uses null), and thus will not require certificates 
            if (seller != null)
            {
                var certAmount = CalculateSaleCertifcates(house, price);
                if (seller.Inventory.Bag.ConsumeItem(ItemTaskType.BuyHouse, Item.AppraisalCertificate, certAmount, null) != certAmount)
                {
                    seller.SendErrorMessage(ErrorMessageType.HouseCannotSellAsNotEnoughSeal);
                    return false;
                }
            }

            house.SellPrice = price;
            house.SellToPlayerId = buyerId;

            house.BroadcastPacket(new SCHouseSetForSalePacket(house.TlId, price, house.SellToPlayerId, buyerName, house.Name), false);
            SetForSaleMarkers(house, true);
            
            return true;
        }
        
        public bool CancelForSale(ushort houseTlId, bool returnCertificates = true) =>
            CancelForSale(GetHouseByTlId(houseTlId),returnCertificates);

        /// <summary>
        /// Cancels a sale
        /// </summary>
        /// <param name="house"></param>
        /// <param name="returnCertificates"></param>
        /// <returns></returns>
        public bool CancelForSale(House house, bool returnCertificates = true)
        {
            if (house.SellPrice <= 0)
                return true;
            var certAmount = CalculateSaleCertifcates(house, house.SellPrice);
            var owner = WorldManager.Instance.GetCharacterById(house.OwnerId);
            
            house.SellPrice = 0;
            house.SellToPlayerId = 0;
            // Can only return certificates if owner is online and is the one resetting the sale
            if ((certAmount > 0) && (returnCertificates) && (owner != null))
            {
                if (owner.Inventory.MailAttachments.AcquireDefaultItemEx(ItemTaskType.Invalid,
                    Item.AppraisalCertificate, certAmount, -1, out var addedItems, out _, 0))
                {
                    // Mail container is set up to never update existing items, so we can discard that result
                    var mail = new BaseMail();
                    mail.MailType = MailType.HousingSale;
                    mail.Header.ReceiverId = house.OwnerId;
                    mail.ReceiverName = NameManager.Instance.GetCharacterName(house.OwnerId);
                    mail.Title = "title(" + ZoneManager.Instance.GetZoneByKey(house.Transform.ZoneId)?.GroupId.ToString() + ",'" + house.Name + "')";
                    mail.Header.SenderName = ".houseSellCancel";
                    mail.Body.Text = "body('" + house.Name + "', " + Item.AppraisalCertificate.ToString() + ", " + certAmount.ToString() + ")";
                    mail.Body.Attachments.AddRange(addedItems);
                    mail.Body.SendDate = DateTime.UtcNow;
                    mail.Body.RecvDate = DateTime.UtcNow.AddMilliseconds(1);
                    mail.Send();
                }
                else
                {
                    // Failed to create Appraisal certificate ?
                    _log.Warn("CancelForSale - Failed to create Appraisal Certificates for mail");
                    return false;
                }
            }

            house.BroadcastPacket(new SCHouseResetForSalePacket(house.TlId, house.Name), false);
            SetForSaleMarkers(house, false);

            return true;
        }

        /// <summary>
        /// Updates all furniture on the house to a new owner and broadcasts packets for it
        /// </summary>
        /// <param name="house"></param>
        /// <param name="characterId"></param>
        /// <returns>The number of items that have their owner information updated</returns>
        public uint UpdateFurnitureOwner(House house, uint characterId)
        {
            uint res = 0;
            var furnitures = WorldManager.Instance.GetDoodadByHouseDbId(house.Id);
            foreach (var furniture in furnitures)
            {
                if (furniture.AttachPoint != AttachPointKind.None)
                    continue;
                furniture.OwnerId = characterId;
                furniture.BroadcastPacket(new SCDoodadOriginatorPacket(furniture.ObjId, characterId),true);
                res++;
            }
            return res;
        }

        /// <summary>
        /// Buys the house using money amount
        /// </summary>
        /// <param name="houseId"></param>
        /// <param name="money"></param>
        /// <param name="character"></param>
        /// <returns>Returns true if successful</returns>
        public bool BuyHouse(uint houseId, uint money, Character character)
        {
            var house = GetHouseById(houseId);

            if (house == null)
            {
                // Invalid house
                character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
                return false;
            }

            if (house.SellPrice <= 0)
            {
                // House wasn't for sale
                character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsNotForSale);
                return false;
            }

            if (house.SellPrice != money)
            {
                // House price changed
                character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsSaleInfoChanged);
                return false;
            }

            if ((house.SellToPlayerId != 0) && (house.SellToPlayerId != character.Id))
            {
                // Not a valid buyer
                character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsNotDesignatedBuyer);
                return false;
            }

            if (house.SellToPlayerId == character.Id)
            {
                // Cannot buy own building
                character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsOwner);
                return false;
            }

            // NOTE: check tax due maybe ?

            if (!character.SubtractMoney(SlotType.Inventory, (int)house.SellPrice, ItemTaskType.BuyHouse))
            {
                // Not enough money
                character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsNotEnoughMoney);
                return false;
            }

            var previousOwner = house.OwnerId;
            var previousOwnerName = NameManager.Instance.GetCharacterName(previousOwner);

            // Mail confirmation mail to new owner
            var newOwnerMail = new BaseMail();
            newOwnerMail.MailType = MailType.HousingSale;
            newOwnerMail.Header.ReceiverId = character.Id;
            newOwnerMail.ReceiverName = character.Name;
            newOwnerMail.Title = "title(" + ZoneManager.Instance.GetZoneByKey(house.Transform.ZoneId)?.GroupId.ToString() + ",'" + house.Name + "')";
            newOwnerMail.Header.SenderName = ".houseBought";
            newOwnerMail.Body.Text = "body('" + previousOwnerName + "', '" + house.Name + "', " + house.SellPrice.ToString() + ")";
            newOwnerMail.Body.SendDate = DateTime.UtcNow;
            newOwnerMail.Body.RecvDate = DateTime.UtcNow.AddMilliseconds(1);
            newOwnerMail.Send();
            
            // Send sales money to previous owner
            var profitMail = new BaseMail();
            profitMail.MailType = MailType.HousingSale;
            profitMail.Header.ReceiverId = previousOwner;
            profitMail.ReceiverName = previousOwnerName;
            profitMail.Title = "title('" + character.Name + "','" + house.Name + "')";
            profitMail.Header.SenderName = ".houseSold";
            profitMail.Body.Text = "body('" + character.Name + "', '" + house.Name + "', " + house.SellPrice.ToString() + ")";
            profitMail.Body.CopperCoins = (int)house.SellPrice; // add the money
            profitMail.Body.SendDate = DateTime.UtcNow;
            profitMail.Body.RecvDate = DateTime.UtcNow.AddMilliseconds(1);
            profitMail.Send();
            
            ReturnHouseItemsToOwner(house, false, false, character);
            
            // Set new owner info
            house.SellPrice = 0;
            house.SellToPlayerId = 0;
            house.AccountId = character.AccountId;
            house.OwnerId = character.Id;
            house.CoOwnerId = character.Id;
            house.Permission = house.Template.AlwaysPublic ? HousingPermission.Public : HousingPermission.Private;
            UpdateHouseFaction(house, character.Faction.Id);
            UpdateTaxInfo(house); // send tax due mails etc if needed ...

            // TODO: broadcast changes
            house.BroadcastPacket(
                new SCHouseSoldPacket(house.TlId, previousOwner, character.Id, character.AccountId, character.Name,
                    house.Name), false);

            SetForSaleMarkers(house, false);
            
            character.SendPacket(new SCMyHousePacket(house));
            var oldOwner = WorldManager.Instance.GetCharacterById(previousOwner);
            if ((oldOwner != null) && (oldOwner.IsOnline))
                oldOwner.SendPacket(new SCMyHouseRemovedPacket(house.TlId));

            UpdateFurnitureOwner(house, character.Id);
            
            house.IsDirty = true;

            return true;
        }

        /// <summary>
        /// Ticker function for checking all houses if they need tax mails sent
        /// </summary>
        public void CheckHousingTaxes()
        {
            if (isCheckingTaxTiming)
                return;
            isCheckingTaxTiming = true;
            try
            {
                // _log.Trace("CheckHousingTaxes");
                var expiredHouseList = new List<House>();
                foreach (var house in _houses)
                {
                    if ((house.Value?.ProtectionEndDate <= DateTime.UtcNow) && (house.Value?.OwnerId > 0))
                        expiredHouseList.Add(house.Value);
                    UpdateTaxInfo(house.Value);
                }
                foreach (var house in expiredHouseList)
                {
                    Demolish(null, house, true, false);
                }
            }
            catch (Exception e)
            {
                _log.Error(e);
            }

            isCheckingTaxTiming = false;
        }

        public HousingDecoration GetDecorationDesignFromId(uint designId)
        {
            if (_housingDecorations.TryGetValue(designId, out var deco))
            {
                return deco;
            }

            return null;
        }

        public HousingDecoration GetDecorationDesignFromDoodadId(uint doodadId)
        {
            var deco = _housingDecorations.FirstOrDefault(x => x.Value.DoodadId == doodadId).Value;
            return default ? null : deco;
        }

        /// <summary>
        /// Places a piece of furniture at a given location, using item and design
        /// </summary>
        /// <param name="player"></param>
        /// <param name="houseId"></param>
        /// <param name="designId"></param>
        /// <param name="pos"></param>
        /// <param name="quat"></param>
        /// <param name="parentObjId"></param>
        /// <param name="itemId"></param>
        /// <returns></returns>
        public bool DecorateHouse(Character player, ushort houseId, uint designId, Vector3 pos, Quaternion quat, uint parentObjId, ulong itemId)
        {
            // Check Player
            if (player == null)
                return false;

            // Check Item
            var item = ItemManager.Instance.GetItemByItemId(itemId);
            if ((item == null) || (item.OwnerId != player.Id))
            {
                // Invalid Item
                return false;
            }

            // Check House
            var house = GetHouseById(houseId);
            if ((house == null) || (house.Id != houseId))
            {
                // Invalid House
                player.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
                return false;
            }

            var itemUcc = UccManager.Instance.GetUccFromItem(item);


            // Create decoration doodad
            var decorationDesign = GetDecorationDesignFromId(designId);

            // TODO: Validate if designId is correct for the given item
            /*
            if (item.TemplateId != decorationDesign.ItemTemplateId)
            {
                player.SendErrorMessage(ErrorMessageType.FailedToUseItem);
                return false;
            }
            */

            var doodad = DoodadManager.Instance.Create(0, decorationDesign.DoodadId);
            doodad.Transform.Parent = house.Transform;
            doodad.Transform.Local.SetPosition(pos.X, pos.Y, pos.Z);
            doodad.Transform.Local.ApplyFromQuaternion(quat);
            doodad.ItemTemplateId = item.TemplateId; // designId;
            doodad.ItemId = (item.Template.MaxCount <= 1) ? itemId : 0;
            doodad.DbHouseId = house.Id;
            doodad.OwnerId = player.Id;
            doodad.ParentObjId = house.ObjId;
            doodad.ParentObj = house;
            doodad.AttachPoint = AttachPointKind.None;
            doodad.OwnerType = DoodadOwnerType.Housing;
            doodad.UccId = itemUcc?.Id ?? 0;
            doodad.IsPersistent = true;

            doodad.Spawn();
            doodad.Save();

            var res = false;
            if (item.Template.MaxCount > 1)
            {
                // Stackable items are simply consumed
                res = (player.Inventory.Bag.ConsumeItem(ItemTaskType.DoodadCreate, item.TemplateId, 1, item) == 1);
            }
            else
            {
                // Non-stackables are stored in the owner's system container as to retain crafter information and such 
                res = player.Inventory.SystemContainer.AddOrMoveExistingItem(ItemTaskType.DoodadCreate, item);
            }
            
            // _log.Debug("DecorateHouse => DoodadTemplate: {0} , DoodadId {1}, Pos: {2}", doodad.TemplateId, doodad.ObjId, doodad.Transform.ToString());

            return res;
        }

        public void HousingToggleAllowRecover(Character character, ushort houseTl)
        {
            var house = GetHouseByTlId(houseTl);
            if (house == null)
                return;
            if (character.Id != house.OwnerId)
                return;
            house.AllowRecover = !house.AllowRecover;
            house.BroadcastPacket(new SCHousingRecoverTogglePacket(house.TlId, house.AllowRecover), false);
        }

        /// <summary>
        /// Returns a house where the given position falls within boundaries of the house 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns>Target House or Null</returns>
        public House GetHouseAtLocation(float x, float y)
        {
            // TODO: Check if all houses actually use a square shape aligned to grid
            // TODO: Add world and/or instance checks
            foreach (var h in _houses)
            {
                var house = h.Value;
                var r = house.Template.GardenRadius;
                var bounds = new RectangleF(house.Transform.World.Position.X - r, house.Transform.World.Position.Y - r,
                    r * 2f, r * 2f);
                if (bounds.Contains(x, y))
                    return house;
            }
            return null;
        }

    }
}
