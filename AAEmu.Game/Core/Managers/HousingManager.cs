using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;
using AAEmu.Game.Models.Game.Error;
using AAEmu.Game.Models.Game.Items.Actions;
using MySql.Data.MySqlClient;
using NLog;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Tasks.Housing;
using Microsoft.CodeAnalysis.Text;

namespace AAEmu.Game.Core.Managers
{
    public class HousingManager : Singleton<HousingManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, HousingTemplate> _housingTemplates;
        private Dictionary<uint, House> _houses;
        private Dictionary<ushort, House> _housesTl; // TODO or so mb tlId is id in the active zone? or type of house
        private List<uint> _removedHousings;
        private List<HousingItemHousings> _housingItemHousings;
        private static uint BUFF_UNTOUCHABLE = 545;
        private static uint REMOVAL_DEBUFF = 2250;
        private static int MAX_HEAVY_TAX_COUNTED = 10; // Maximum number of heavy tax buildings to take into account for tax calculation
        private bool isCheckingTaxTiming = false;

        public int GetByAccountId(Dictionary<uint, House> values, uint accountId)
        {
            foreach (var (id, house) in _houses)
                if (house.AccountId == accountId)
                    values.Add(id, house);
            return values.Count;
        }

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

            //            var housingAreas = new Dictionary<uint, HousingAreas>();
            var houseTaxes = new Dictionary<uint, HouseTax>();

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
                            house.Position = new Point(reader.GetFloat("x"), reader.GetFloat("y"), reader.GetFloat("z"));
                            house.Position.RotationZ = reader.GetSByte("rotation_z");
                            house.Position.WorldId = 1;
                            house.Position.ZoneId = WorldManager.Instance.GetZoneId(house.Position.WorldId, house.Position.X, house.Position.Y);
                            house.CurrentStep = reader.GetInt32("current_step");
                            house.NumAction = reader.GetInt32("current_action");
                            house.Permission = (HousingPermission)reader.GetByte("permission");
                            house.PlaceDate = reader.GetDateTime("place_date");
                            house.ProtectionEndDate = reader.GetDateTime("protected_until");
                            house.SellToPlayerId = reader.GetUInt32("sell_to");
                            house.SellPrice = reader.GetUInt32("sell_price");
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

        public void SetUntouchable(House house, bool isUntouchable)
        {
            if (isUntouchable)
            {
                if (house.Buffs.CheckBuff(BUFF_UNTOUCHABLE))
                    return;

                // Permanent Untouchable buff, should only be removed when failed tax payment, or demolishing by hand
                var protectionBuffTemplate = SkillManager.Instance.GetBuffTemplate(BUFF_UNTOUCHABLE);
                if (protectionBuffTemplate != null)
                {
                    var casterObj = new Models.Game.Skills.SkillCasterUnit(house.ObjId);
                    house.Buffs.AddBuff(new Models.Game.Skills.Buff(house, house, casterObj,
                        protectionBuffTemplate, null, System.DateTime.Now));
                }
                else
                {
                    _log.Error("Unable to find Untouchable buff template");
                }
            }
            else
            {
                // Remove Untouchable if it's enabled
                if (house.Buffs.CheckBuff(BUFF_UNTOUCHABLE))
                    house.Buffs.RemoveBuff(BUFF_UNTOUCHABLE);
            }
        }

        public void SetRemovalDebuff(House house, bool isDeteriorating)
        {
            if (isDeteriorating)
            {
                if (!house.Buffs.CheckBuff(REMOVAL_DEBUFF))
                {
                    // Permanent Untouchable buff, should only be removed when failed tax payment, or demolishing by hand
                    var protectionBuffTemplate = SkillManager.Instance.GetBuffTemplate(REMOVAL_DEBUFF);
                    if (protectionBuffTemplate != null)
                    {
                        var casterObj = new Models.Game.Skills.SkillCasterUnit(house.ObjId);
                        house.Buffs.AddBuff(new Models.Game.Skills.Buff(house, house, casterObj,
                            protectionBuffTemplate, null, System.DateTime.Now));
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
                if (house.Buffs.CheckBuff(REMOVAL_DEBUFF))
                    house.Buffs.RemoveBuff(REMOVAL_DEBUFF);
            }
        }



        public void ConstructHouseTax(GameConnection connection, uint designId, float x, float y, float z)
        {
            // TODO validation position and some range...

            var houseTemplate = _housingTemplates[designId];

            CalculateBuildingTaxInfo(connection.ActiveChar.AccountId, houseTemplate, true, out var totalTaxAmountDue, out var heavyTaxHouseCount, out var normalTaxHouseCount, out var hostileTaxRate);

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

        public void HouseTaxInfo(GameConnection connection, ushort tlId)
        {
            if (!_housesTl.ContainsKey(tlId))
                return;

            var house = _housesTl[tlId];

            CalculateBuildingTaxInfo(house.AccountId, house.Template, false, out var totalTaxAmountDue, out var heavyTaxHouseCount, out var normalTaxHouseCount, out var hostileTaxRate);

            var baseTax = (int)(house.Template.Taxation?.Tax ?? 0);
            var depositTax = baseTax * 2;

            var weeksDelta = house.ProtectionEndDate - DateTime.UtcNow;
            var weeks = (int)(weeksDelta.TotalDays / -7f);
            connection.SendPacket(
                new SCHouseTaxInfoPacket(
                    house.TlId,
                    0,  // TODO: implement when castles are added
                    depositTax, // this is used in the help text on (?) when you hover your mouse over it to display deposit tax for this building
                    totalTaxAmountDue, // Amount Due
                    house.ProtectionEndDate,
                    house.TaxDueDate > DateTime.UtcNow, // already payed if the tax-due date is past now
                    weeks,  // TODO: do proper calculation
                    house.Template.HeavyTax
                )
            );
        }

        public void Build(GameConnection connection, uint designId, Point position, float zRot,
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


            var zoneId = WorldManager.Instance.GetZoneId(1, position.X, position.Y);

            var houseTemplate = _housingTemplates[designId];
            CalculateBuildingTaxInfo(connection.ActiveChar.AccountId, houseTemplate, true, out var totalTaxAmountDue, out var heavyTaxHouseCount, out var normalTaxHouseCount, out var hostileTaxRate);

            if (FeaturesManager.Fsets.Check(Models.Game.Features.Feature.taxItem))
            {
                // Pay in Tax Certificate

                var userTaxCount = connection.ActiveChar.Inventory.GetItemsCount(SlotType.Inventory, Item.TaxCertificate);
                var userBoundTaxCount = connection.ActiveChar.Inventory.GetItemsCount(SlotType.Inventory, Item.BoundTaxCertificate);
                var totatUserTaxCount = userTaxCount + userBoundTaxCount;
                var totalCertsCost = (int)Math.Ceiling(totalTaxAmountDue / 10000f);

                // Alloyingly complex item consumption, maybe we need a seperate function in inventory to handle this kind of thing
                var consumedCerts = totalCertsCost;
                if (totalCertsCost > totatUserTaxCount)
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
                connection.ActiveChar.SubtractMoney(SlotType.Inventory, totalTaxAmountDue,Models.Game.Items.Actions.ItemTaskType.HouseCreation);
            }


            if (connection.ActiveChar.Inventory.Bag.ConsumeItem(Models.Game.Items.Actions.ItemTaskType.HouseBuilding, sourceDesignItem.TemplateId, 1, sourceDesignItem) <= 0)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.BagInvalidItem);
                return;
            }

            // Spawn the actual house
            var house = Create(designId,connection.ActiveChar.Faction.Id);

            // Fallback for un-translated buildings (en_us)
            if (house.Name == string.Empty)
            {
                var fakeLocalizedName = LocalizationManager.Instance.Get("items", "name", sourceDesignItem.Template.Id, houseTemplate.Name);
                if (fakeLocalizedName.EndsWith(" Design"))
                    fakeLocalizedName = fakeLocalizedName.Replace(" Design", "");
                house.Name = fakeLocalizedName;
            }

            house.Id = HousingIdManager.Instance.GetNextId();
            house.Position = position;
            house.Position.RotationZ = MathUtil.ConvertRadianToDirection(zRot);

            house.Position.WorldId = 1;
            house.Position.ZoneId = zoneId;
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
            connection.SendPacket(new SCHousePermissionChangedPacket(tlId, (byte)permission));
        }

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

        public void Demolish(GameConnection connection, House house, bool failedToPayTax)
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
                ReturnHouseItemsToOwner(house, failedToPayTax);
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
                UpdateHouseFaction(house, (uint)Factions.FACTION_MONSTROSITY);
                house.IsDirty = true;

                // TODO: better house killing handling
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
            //house.Despawn = DateTime.Now.AddSeconds(20);
            //SpawnManager.Instance.AddDespawn(house);
        }

        public bool CalculateBuildingTaxInfo(uint AccountId, HousingTemplate newHouseTemplate, bool buildingNewHouse, out int totalTaxToPay, out int heavyHouseCount, out int normalHouseCount, out int hostileTaxRate)
        {
            totalTaxToPay = 0;
            heavyHouseCount = 0;
            normalHouseCount = 0;
            hostileTaxRate = 0; // NOTE: When castles are added, this needs to be updated depending on ruling guild's settings

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
            // If less than 3 properties, or not a heavy tax peroperty, no extra multiplier needed
            if ((heavyHouseCount < 3) || (newHouseTemplate.HeavyTax == false))
                taxMultiplier = 1f;

            totalTaxToPay = (int)Math.Ceiling(newHouseTemplate.Taxation.Tax * taxMultiplier);

            // If this is a new house, add the deposit (base tax * 2)
            if (buildingNewHouse)
                totalTaxToPay += (int)(newHouseTemplate.Taxation.Tax * 2);

            return true;
        }

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
                    _log.Trace("New Tax Mail sent for {0} owned by {1}",house.Name, house.OwnerId);
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

        public void UpdateHouseFaction(House house, uint factionId)
        {
            house.BroadcastPacket(new SCUnitFactionChangedPacket(house.ObjId, house.Name, house.Faction?.Id ?? 0, factionId, false), true);
            house.Faction = FactionManager.Instance.GetFaction(factionId);
        }

        public void UpdateOwnedHousingFaction(uint characterId, uint factionId)
        {
            var myHouses = new Dictionary<uint, House>();
            GetByCharacterId(myHouses, characterId);
            foreach(var h in myHouses)
                if ((h.Value.Faction == null) || (h.Value.Faction.Id != factionId))
                    UpdateHouseFaction(h.Value, factionId);
        }

        public void ReturnHouseItemsToOwner(House house, bool failedToPayTax)
        {
            if (house.OwnerId <= 0)
                return;

            var returnedItems = new List<Item>();
            var returnedMoney = 0;

            // TODO: proper grades for design
            var designItemId = GetItemIdByDesign(house.Template.Id);
            var designItem = ItemManager.Instance.Create(designItemId, 1, 0);
            designItem.OwnerId = house.OwnerId;
            designItem.SlotType = SlotType.Mail;
            returnedItems.Add(designItem);

            // TODO: Grab a list of items in chests
            // TODO: Grab a list of furniture

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

                // Attach item
                newMail.Body.Attachments.Add(returnedItems[i]);

                // Send on last or 10th item of the mail
                if (((i % 10) == 9) || (i == returnedItems.Count-1))
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

        public int CalculateSaleCertifcates(House house, uint salePrice)
        {
            // NOTE: In earlier AA, you need 1 appraisal certificate for every 100 gold of sales price
            // TODO: In later versions, this depends on the building-type/size
            var certAmount = (int)Math.Ceiling(salePrice / 1000000f);
            if (certAmount < 1)
                certAmount = 1;
            return certAmount;
        }

        public bool SetForSale(ushort houseTlId, uint price, uint buyerId, Character seller) =>
            SetForSale(GetHouseByTlId(houseTlId), price, buyerId, seller);
        
        public bool SetForSale(House house,uint price, uint buyerId, Character seller)
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
            // TODO: broadcast changes
            house.BroadcastPacket(new SCHouseSetForSalePacket(house.TlId, price, house.SellToPlayerId, buyerName,house.Name),false);
            
            // TODO: spawn for sale markers
            return true;
        }

        public bool CancelForSale(House house, bool returnCertificates = true)
        {
            house.SellPrice = 0;
            house.SellToPlayerId = 0;
            if (returnCertificates)
            {
                // TODO: mail certificates back to owner
            }

            house.BroadcastPacket(new SCHouseResetForSalePacket(house.TlId,house.Name),false);
            // TODO: remove for sale markers
            
            return true;
        }

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
            
            // TODO: MailProfit to previous owner
            // TODO: Return bound furniture back to owner

            // Set new owner info
            house.SellPrice = 0;
            house.SellToPlayerId = 0;
            house.AccountId = character.AccountId;
            house.OwnerId = character.Id;
            house.CoOwnerId = character.Id;
            house.Permission = house.Template.AlwaysPublic ? HousingPermission.Public : HousingPermission.Private;
            UpdateHouseFaction(house,character.Faction.Id);
            UpdateTaxInfo(house); // send tax due mails etc if needed ...
            
            // TODO: broadcast changes
            house.BroadcastPacket(
                new SCHouseSoldPacket(house.TlId, previousOwner, character.Id, character.AccountId, character.Name,
                    house.Name), false);

            house.IsDirty = true;

            return true;
        }

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
                    Demolish(null, house, true);
                }
            }
            catch (Exception e)
            {
                _log.Error(e);
            }

            isCheckingTaxTiming = false;
        }

    }
}
