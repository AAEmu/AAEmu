using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
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

namespace AAEmu.Game.Core.Managers;

public class HousingManager : Singleton<HousingManager>
{
    private const uint ForSaleMarkerDoodadId = 6760;
    private const int MaxHeavyTaxCounted = 10; // Maximum number of heavy tax buildings to take into account for tax calculation
    private const int HoursForFailedTaxToReturnHouse = 22;
    private const double CopperPerCertificate = 1000000.0; // For older versions of AA, 1 sale certificate / 100g
    private const int TaxPaysForDays = 7; // Number of days 1 week worth of tax pays for
    private Dictionary<uint, House> _houses;
    private Dictionary<ushort, House> _housesTl; // TODO or so mb tlId is id in the active zone? or type of house
    private Dictionary<uint, HousingDecoration> _housingDecorations;
    private List<ItemHousingDecoration> _housingItemHousingDecorations;
    private List<HousingItemHousings> _housingItemHousings;
    private Dictionary<uint, HousingTemplate> _housingTemplates;
    private bool _isCheckingTaxTiming;
    private List<uint> _removedHousings;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Gets all houses for a given Account
    /// </summary>
    /// <param name="values"></param>
    /// <param name="accountId"></param>
    /// <returns></returns>
    public int GetByAccountId(Dictionary<uint, House> values, ulong accountId)
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

    /// <summary>
    /// Creates House and set it's untouchable buff
    /// </summary>
    /// <param name="templateId"></param>
    /// <param name="factionId"></param>
    /// <param name="objectId"></param>
    /// <param name="tlId"></param>
    /// <returns></returns>
    private House Create(uint templateId, FactionsEnum factionId, uint objectId = 0, ushort tlId = 0)
    {
        if (!_housingTemplates.TryGetValue(templateId, out var template))
            return null;

        var house = new House
        {
            TlId = tlId > 0 ? tlId : (ushort)HousingTldManager.Instance.GetNextId(),
            ObjId = objectId > 0 ? objectId : ObjectIdManager.Instance.GetNextId(),
            Template = template,
            TemplateId = template.Id, // duplicate Id
            Id = template.Id,
            Faction = FactionManager.Instance.GetFaction(factionId),
            Name = LocalizationManager.Instance.Get("housings", "name", template.Id)
        };
        house.Hp = house.MaxHp;
        // Force public on always public properties on create
        if (template.AlwaysPublic)
            house.Permission = HousingPermission.Public;

        SetUntouchable(house, true);

        return house;
    }

    /// <summary>
    /// Load housing definitions, player houses and starts tax check timer
    /// </summary>
    /// <exception cref="IOException"></exception>
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
            Logger.Info("Loading Housing Information ...");

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM item_housings";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new HousingItemHousings();
                        //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                        template.Item_Id = reader.GetUInt32("item_id");
                        template.Design_Id = reader.GetUInt32("design_id");
                        _housingItemHousings.Add(template);
                    }
                }
            }

            Logger.Info("Loading Housing Templates...");

            var filePath = Path.Combine(FileManager.AppPath, "Data", "housing_bindings.json");
            var contents = FileManager.GetFileContents(filePath);
            if (string.IsNullOrWhiteSpace(contents))
                throw new IOException(
                    $"File {filePath} doesn't exists or is empty.");

            if (JsonHelper.TryDeserializeObject(contents, out List<HousingBindingTemplate> binding, out _))
                Logger.Info("Housing bindings loaded...");
            else
                Logger.Warn("Housing bindings not loaded...");

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
                        //template.GardenRadius = reader.GetFloat("garden_radius"); // there is no such field in the database for version 3.0.3.0
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
                            command2.CommandText = "SELECT * FROM housing_binding_doodads WHERE housing_id=@housing_id";
                            command2.Parameters.AddWithValue("housing_id", template.Id);
                            command2.Prepare();
                            using (var reader2 = new SQLiteWrapperReader(command2.ExecuteReader()))
                            {
                                var doodads = new List<HousingBindingDoodad>();
                                while (reader2.Read())
                                {
                                    var bindingDoodad = new HousingBindingDoodad();
                                    bindingDoodad.AttachPointId = (AttachPointKind)reader2.GetUInt32("attach_point_id");
                                    bindingDoodad.DoodadId = reader2.GetUInt32("doodad_id");

                                    if (templateBindings != null && templateBindings.AttachPointId.TryGetValue(bindingDoodad.AttachPointId, out var pos))
                                        bindingDoodad.Position = pos.Clone();

                                    bindingDoodad.Position ??= new WorldSpawnPosition();

                                    doodads.Add(bindingDoodad);
                                }

                                template.HousingBindingDoodad = doodads.ToArray();
                            }
                        }
                    }
                }
            }

            Logger.Info($"Loaded Housing Templates {_housingTemplates.Count}");

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

                        var template = new HousingBuildStep
                        {
                            Id = reader.GetUInt32("id"), 
                            HousingId = housingId, 
                            Step = reader.GetInt16("step"), 
                            ModelId = reader.GetUInt32("model_id"),
                            SkillId = reader.GetUInt32("skill_id"),
                            NumActions = reader.GetInt32("num_actions")
                        };

                        _housingTemplates[housingId].BuildSteps.Add(template.Step, template);
                    }
                }
            }

            Logger.Info("Loaded Decoration Templates...");

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
                        //template.Name = reader.GetString("name"); // there is no such field in the database for version 3.0.3.0
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
                        //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                        template.ItemId = reader.GetUInt32("item_id");
                        template.DesignId = reader.GetUInt32("design_id");
                        template.Restore = reader.GetBoolean("restore", true);

                        _housingItemHousingDecorations.Add(template);
                    }
                }
            }
        }

        Logger.Info("Loading Player Buildings ...");
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
                        var factionId = (FactionsEnum)reader.GetUInt32("faction_id");
                        var house = Create(templateId, factionId);
                        house.Id = reader.GetUInt32("id");
                        house.AccountId = reader.GetUInt64("account_id");
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

        Logger.Info($"Loaded {_houses.Count} Player Buildings");

        var houseCheckTask = new HousingTaxTask();
        TaskManager.Instance.Schedule(houseCheckTask, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));

        Logger.Info("Started Housing Tax Timer");
    }

    /// <summary>
    /// Saves player housing information
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="transaction"></param>
    /// <returns></returns>
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

    /// <summary>
    /// Spawn all houses
    /// </summary>
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
    private static void SetUntouchable(House house, bool isUntouchable)
    {
        if (isUntouchable)
        {
            if (house.Buffs.CheckBuff((uint)BuffConstants.Untouchable))
                return;

            // Permanent Untouchable buff, should only be removed when failed tax payment, or demolishing by hand
            var protectionBuffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Untouchable);
            if (protectionBuffTemplate != null)
            {
                var casterObj = new SkillCasterUnit(house.ObjId);
                house.Buffs.AddBuff(new Buff(house, house, casterObj,
                    protectionBuffTemplate, null, DateTime.UtcNow));
            }
            else
            {
                Logger.Error("Unable to find Untouchable buff template");
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
    private static void SetRemovalDebuff(House house, bool isDeteriorating)
    {
        if (isDeteriorating)
        {
            if (!house.Buffs.CheckBuff((uint)BuffConstants.RemovalDebuff))
            {
                // Permanent Untouchable buff, should only be removed when failed tax payment, or demolishing by hand
                var protectionBuffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.RemovalDebuff);
                if (protectionBuffTemplate != null)
                {
                    var casterObj = new SkillCasterUnit(house.ObjId);
                    house.Buffs.AddBuff(new Buff(house, house, casterObj,
                        protectionBuffTemplate, null, DateTime.UtcNow));
                }
                else
                {
                    Logger.Error("Unable to find Removal Debuff template");
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

    /// <summary>
    /// Sends tax information about a house
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="designId"></param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public void ConstructHouseTax(GameConnection connection, uint designId, float x, float y, float z)
    {
        // TODO validation position and some range...

        var houseTemplate = _housingTemplates[designId];

        CalculateBuildingTaxInfo(connection.ActiveChar.AccountId, houseTemplate, true, out var totalTaxAmountDue, out var heavyTaxHouseCount, out var normalTaxHouseCount, out _, out _);

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
    public void HouseTaxInfo(GameConnection connection, ushort tlId, uint objId)
    {
        if (!_housesTl.TryGetValue(tlId, out var house))
            return;

        CalculateBuildingTaxInfo(house.AccountId, house.Template, false, out var totalTaxAmountDue, out _, out _, out _, out _);

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
        else if (house.ProtectionEndDate <= DateTime.UtcNow)
        {
            requiresPayment = true;
            weeksWithoutPay = 1;
        }

        // Logger.Debug($"SCHouseTaxInfoPacket; tlId:{house.TlId}, domTaxRate: 0, deposit: {depositTax}, taxDue:{totalTaxAmountDue}, protectEnd:{house.ProtectionEndDate}, isPaid:{requiresPayment}, weeksWithoutPay:{weeksWithoutPay}, isHeavy:{house.Template.HeavyTax}");
        
        connection.SendPacket(
            new SCHouseTaxInfoPacket(
                house.TlId,
                0,  // TODO: implement when castles are added
                0,
                depositTax, // this is used in the help text on (?) when you hover your mouse over it to display deposit tax for this building
                totalTaxAmountDue, // Amount Due
                house.ProtectionEndDate,
                requiresPayment,
                weeksWithoutPay,  // TODO: do proper calculation ?
                -1,
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
        if ((sourceDesignItem == null) || (sourceDesignItem.OwnerId != connection.ActiveChar.Id))
        {
            // Invalid itemId supplied or the id is not owned by the user
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.BagInvalidItem);
            return;
        }

        // var zoneId = WorldManager.Instance.GetZoneId(connection.ActiveChar.Transform.WorldId, posX, posY);

        var houseTemplate = _housingTemplates[designId];
        CalculateBuildingTaxInfo(connection.ActiveChar.AccountId, houseTemplate, true, out var totalTaxAmountDue, out _, out _, out _, out _);

        if (FeaturesManager.Fsets.Check(Models.Game.Features.Feature.taxItem))
        {
            // Pay in Tax Certificate

            var userTaxCount = connection.ActiveChar.Inventory.GetItemsCount(SlotType.Bag, Item.TaxCertificate);
            var userBoundTaxCount = connection.ActiveChar.Inventory.GetItemsCount(SlotType.Bag, Item.BoundTaxCertificate);
            var totalUserTaxCount = userTaxCount + userBoundTaxCount;
            var totalCertsCost = (int)Math.Ceiling(totalTaxAmountDue / 10000f);

            // Annoyingly complex item consumption, maybe we need a separate function in inventory to handle this kind of thing
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
                    connection.ActiveChar.Inventory.Bag.ConsumeItem(ItemTaskType.HouseCreation, Item.BoundTaxCertificate, c, null);
                    consumedCerts -= c;
                }
                c = consumedCerts;
                if ((userTaxCount > 0) && (c > 0))
                {
                    if (c > userTaxCount)
                        c = userTaxCount;
                    connection.ActiveChar.Inventory.Bag.ConsumeItem(ItemTaskType.HouseCreation, Item.TaxCertificate, c, null);
                    consumedCerts -= c;
                }

                if (consumedCerts != 0)
                    Logger.Error($"Something went wrong when paying tax for new building for player {connection.ActiveChar.Name}");
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
            connection.ActiveChar.SubtractMoney(SlotType.Bag, totalTaxAmountDue, ItemTaskType.HouseCreation);
        }

        if (connection.ActiveChar.Inventory.Bag.ConsumeItem(ItemTaskType.HouseBuilding, sourceDesignItem.TemplateId, 1, sourceDesignItem) <= 0)
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
        house.Ht = ht;
        house.Permission = HousingPermission.Private;
        house.AllowRecover = true;
        house.PlaceDate = DateTime.UtcNow;
        house.ProtectionEndDate = DateTime.UtcNow.AddDays(TaxPaysForDays);
        _houses.Add(house.Id, house);
        _housesTl.Add(house.TlId, house);
        connection.ActiveChar.SendPacket(new SCHouseStatePacket(house));
        house.Spawn();
        UpdateTaxInfo(house);
        ResidentManager.Instance.AddResidenMemberInfo(connection.ActiveChar);
    }

    /// <summary>
    /// Update house permission settings
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    /// <param name="permission"></param>
    public void ChangeHousePermission(GameConnection connection, ushort tlId, HousingPermission permission)
    {
        if (!_housesTl.TryGetValue(tlId, out var house))
            return; // invalid house

        if (house.OwnerId != connection.ActiveChar.Id)
            return; // not the owner
        
        house.Permission = permission;
        house.BroadcastPacket(new SCHousePermissionChangedPacket(tlId, (byte)permission), false);
    }

    /// <summary>
    /// Rename house
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    /// <param name="name"></param>
    public void ChangeHouseName(GameConnection connection, ushort tlId, string name)
    {
        if (!_housesTl.TryGetValue(tlId, out var house))
            return;

        if (house.OwnerId != connection.ActiveChar.Id)
            return;

        house.Name = name.NormalizeName();
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
        if (connection is null || house.OwnerId == connection.ActiveChar.Id)
        {
            // VERIFY: check if tax paid, cannot manually demolish or sell a house with unpaid taxes ?
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

            ownerChar?.SendPacket(new SCHouseRemovedPacket(house.TlId));
            // Make killable
            UpdateHouseFaction(house, FactionsEnum.Monstrosity);
            if (connection?.ActiveChar != null)
            {
                ResidentManager.Instance.RemoveResidenMemberInfo(connection.ActiveChar);
            }

            SetForSaleMarkers(house, false);

            house.IsDirty = true;

            // TODO: better house killing handling
            _removedHousings.Add(house.Id);
        }
        else
        {
            // Non-owner should not be able to press demolish
            connection.ActiveChar?.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
        }
    }

    /// <summary>
    /// Fully removes a house from the world
    /// </summary>
    /// <param name="house"></param>
    public void RemoveDeadHouse(House house)
    {
        // Remove house from housing tables
        _removedHousings.Add(house.Id);
        _houses.Remove(house.Id);
        _housesTl.Remove(house.TlId);
        HousingTldManager.Instance.ReleaseId(house.TlId);
        HousingIdManager.Instance.ReleaseId(house.Id);
        // TODO: not sure how to handle this, just instant delete it for now
        house.Delete();
        // TODO: Add to despawn handler
        //house.Despawn = DateTime.UtcNow.AddSeconds(20);
        //SpawnManager.Instance.AddDespawn(house);
    }

    /// <summary>
    /// Helper function to calculate due tax
    /// </summary>
    /// <param name="accountId"></param>
    /// <param name="newHouseTemplate"></param>
    /// <param name="buildingNewHouse"></param>
    /// <param name="totalTaxToPay"></param>
    /// <param name="heavyHouseCount"></param>
    /// <param name="normalHouseCount"></param>
    /// <param name="hostileTaxRate"></param>
    /// <param name="oneWeekTaxCount"></param>
    /// <returns></returns>
    public bool CalculateBuildingTaxInfo(ulong accountId, HousingTemplate newHouseTemplate, bool buildingNewHouse, out int totalTaxToPay, out int heavyHouseCount, out int normalHouseCount, out int hostileTaxRate, out int oneWeekTaxCount)
    {
        totalTaxToPay = 0;
        heavyHouseCount = 0;
        normalHouseCount = 0;
        hostileTaxRate = 0; // NOTE: When castles are added, this needs to be updated depending on ruling guild's settings
        oneWeekTaxCount = 0;

        var userHouses = new Dictionary<uint, House>();
        if (GetByAccountId(userHouses, accountId) <= 0)
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
        var taxMultiplier = (heavyHouseCount < MaxHeavyTaxCounted ? heavyHouseCount : MaxHeavyTaxCounted) * 0.5f;
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
    public static void UpdateTaxInfo(House house)
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
                Logger.Trace($"New Tax Mail sent for {house.Name} owned by {house.OwnerId}");
            }
            else
            {
                foreach (var mail in allMails)
                {
                    MailForTax.UpdateTaxInfo(mail, house);
                    Logger.Trace($"Tax Mail {mail.Id} updated for {house.Name} ({house.Id}) owned by {house.OwnerId}");
                }
            }
        }
    }

    /// <summary>
    /// Adds a week to the protection end date (pay 1 week's tax)
    /// </summary>
    /// <param name="house"></param>
    /// <returns></returns>
    public static bool PayWeeklyTax(House house)
    {
        house.ProtectionEndDate = house.ProtectionEndDate.AddDays(TaxPaysForDays);
        return true;
    }

    /// <summary>
    /// Get house by DB Id
    /// </summary>
    /// <param name="houseId"></param>
    /// <returns></returns>
    public House GetHouseById(uint houseId)
    {
        return _houses.GetValueOrDefault(houseId);
    }

    /// <summary>
    /// Get house by TlId
    /// </summary>
    /// <param name="houseTlId"></param>
    /// <returns></returns>
    private House GetHouseByTlId(ushort houseTlId)
    {
        return _housesTl.GetValueOrDefault(houseTlId);
    }

    /// <summary>
    /// Changes the faction of the house
    /// </summary>
    /// <param name="house"></param>
    /// <param name="factionId"></param>
    private static void UpdateHouseFaction(House house, FactionsEnum factionId)
    {
        house.BroadcastPacket(new SCUnitFactionChangedPacket(house.ObjId, house.Name, house.Faction?.Id ?? 0, factionId, false), true);
        house.Faction = FactionManager.Instance.GetFaction(factionId);
    }

    /// <summary>
    /// Helper function for when the owning character changes faction
    /// </summary>
    /// <param name="characterId"></param>
    /// <param name="factionId"></param>
    public void UpdateOwnedHousingFaction(uint characterId, FactionsEnum factionId)
    {
        // TODO: Does this also need to be done when temporary changing factions? (like arena)
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
    private void ReturnHouseItemsToOwner(House house, bool failedToPayTax, bool forceRestoreAllDecor, ICharacter newOwner)
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
            if (designTemplate != null && designItem != null)
            {
                designItem.Grade = (designTemplate.FixedGrade >= 0) ? (byte)designTemplate.FixedGrade : (byte)0;
                designItem.OwnerId = house.OwnerId;
                designItem.SlotType = SlotType.MailAttachment;
                returnedItems.Add(designItem);
            }

            // Return taxes
            if (!failedToPayTax)
            {
                if (FeaturesManager.Fsets.Check(Models.Game.Features.Feature.taxItem))
                {
                    var taxItem = ItemManager.Instance.Create(Item.BoundTaxCertificate, (int)(house.Template.Taxation.Tax / 5000), 0);
                    taxItem.OwnerId = house.OwnerId;
                    taxItem.SlotType = SlotType.MailAttachment;
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
                f.OwnerDbId = 0;
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
                f.OwnerDbId = 0;
                Logger.Warn($"ReturnHouseItemsToOwner - Furniture doesn't have design info for Doodad Id:{f.ObjId} Template:{f.TemplateId}");
                continue;
            }

            var thisDoodadsItem = ItemManager.Instance.GetItemByItemId(f.ItemId);
            var returnedThisItem = false;

            var wantReturned = ((newOwner == null) && decoInfo.Restore) || forceRestoreAllDecor;

            // If item is bound, always return it owner
            if (f.ItemId > 0)
            {
                var item = ItemManager.Instance.GetItemByItemId(f.ItemId);
                if (item.ItemFlags.HasFlag(ItemFlag.SoulBound))
                    wantReturned = true;
            }

            // If this doodad is a Coffer and has a ItemContainer attached, also return all item of that container
            if ((f is DoodadCoffer coffer) && (f.GetItemContainerId() > 0))
            {
                // TODO: Check if items should stay in the coffer when house is sold.
                // Move it to new owner's SystemContainer first so they don't get destroyed
                var ownerSystemContainer = ItemManager.Instance.GetItemContainerForCharacter(house.OwnerId, SlotType.Money, null, 0);
                for (var i = coffer.ItemContainer.Items.Count - 1; i >= 0; i--)
                {
                    var cofferItem = coffer.ItemContainer.Items[i];
                    //if (cofferItem.HasFlag(ItemFlag.SoulBound) || forceRestoreAllDecor)
                    {
                        ownerSystemContainer?.AddOrMoveExistingItem(ItemTaskType.Invalid, cofferItem);
                        returnedItems.Add(cofferItem);
                    }
                }
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
                    f.ItemId = 0;
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
                if (thisDoodadsItem is { SlotType: SlotType.Money })
                {
                    returnedItems.Add(thisDoodadsItem);
                    returnedThisItem = true;
                    f.ItemId = 0; // don't auto-delete
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
                    // It's a new one, add an item slot
                    var furnitureItem = ItemManager.Instance.Create(f.ItemTemplateId, 1, 0);
                    var furnitureTemplate = ItemManager.Instance.GetTemplate(f.ItemTemplateId);
                    furnitureItem.Grade = (furnitureTemplate.FixedGrade >= 0) ? (byte)furnitureTemplate.FixedGrade : (byte)0;
                    furnitureItem.OwnerId = house.OwnerId;
                    furnitureItem.SlotType = SlotType.MailAttachment;
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
            // Split items into mails of maximum 10 attachments
            if ((i % 10) == 0)
            {
                // TODO: proper mail handler
                newMail = new BaseMail
                {
                    MailType = MailType.Demolish,
                    ReceiverName = NameManager.Instance.GetCharacterName(house.OwnerId), // Doesn't seem like this needs to be set
                    Header =
                    {
                        ReceiverId = house.OwnerId, 
                        SenderId = 0, 
                        SenderName = ".houseDemolish", 
                        Extra = house.Id
                    },
                    Title = "title",
                    Body = { 
                        Text = "body", // Yes, that's indeed what it needs to be set to
                        SendDate = DateTime.UtcNow,
                        RecvDate = DateTime.UtcNow.AddHours(failedToPayTax ? HoursForFailedTaxToReturnHouse : 0)
                    }
                };
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
                returnedItems[i].SlotType = SlotType.MailAttachment;

            // Attach item
            newMail.Body.Attachments.Add(returnedItems[i]);

            // Send on last or 10th item of the mail
            if (((i % 10) == 9) || (i == returnedItems.Count - 1))
                newMail.Send();
        }

        if (newMail != null)
        {
            Logger.Trace($"Demolition mail sent to {newMail.ReceiverName}");
        }
    }

    /// <summary>
    /// Get house design by item template
    /// </summary>
    /// <param name="itemId"></param>
    /// <returns></returns>
    private uint GetDesignByItemId(uint itemId)
    {
        var design = _housingItemHousings.FirstOrDefault(h => h.Item_Id == itemId);
        return design?.Design_Id ?? 0;
    }

    /// <summary>
    /// Get original item template based on house design
    /// </summary>
    /// <param name="designId"></param>
    /// <returns></returns>
    private uint GetItemIdByDesign(uint designId)
    {
        var design = _housingItemHousings.FirstOrDefault(h => h.Design_Id == designId);
        return design?.Item_Id ?? 0;
    }

    /// <summary>
    /// Helper function to calculate how many Appraisal Certificates are needed to sell a house at a given price
    /// </summary>
    /// <param name="house"></param>
    /// <param name="salePrice"></param>
    /// <returns></returns>
    private static int CalculateSaleCertifcates(House house, uint salePrice)
    {
        // NOTE: In earlier AA, you need 1 appraisal certificate for every 100 gold of sales price
        // TODO: In later versions, this depends on the building-type/size
        var certAmount = (int)Math.Ceiling(salePrice / CopperPerCertificate);
        if (certAmount < 1)
            certAmount = 1;
        return certAmount;
    }

    /// <summary>
    /// Sets or removes For Sale Signs on the property
    /// </summary>
    /// <param name="house"></param>
    /// <param name="isForSale"></param>
    private static void SetForSaleMarkers(House house, bool isForSale)
    {
        if (isForSale)
        {
            for (var postId = 0; postId < 4; postId++)
            {
                var xMultiplier = (postId % 2) == 0 ? -1 : 1f;
                var yMultiplier = (postId / 2) == 0 ? -1 : 1f;
                var zRot = ((135f + (90f * postId) % 360)).DegToRad();

                var doodad = DoodadManager.Instance.Create(0, ForSaleMarkerDoodadId, null, true);
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
                doodad.OwnerDbId = house.Id;
                doodad.InitDoodad();

                doodad.Spawn();
            }
        }
        else
        {
            // Get all doodads related to this house
            var thisHouseSalePosts = WorldManager.Instance.GetDoodadByHouseDbId(house.Id);
            for (var c = thisHouseSalePosts.Count - 1; c >= 0; c--)
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

    /// <summary>
    /// Puts up a house for sale
    /// </summary>
    /// <param name="house"></param>
    /// <param name="price"></param>
    /// <param name="buyerId">Use CharacterId for selling to a specific person</param>
    /// <param name="seller">Current owner of the property (needed to manipulate inventory)</param>
    /// <returns></returns>
    public static bool SetForSale(House house, uint price, uint buyerId, Character seller)
    {
        if (house == null)
            return false;

        if (!house.Template.IsSellable)
            return false;

        // Check if buyer exists (we just check if the name exists)
        var buyerName = NameManager.Instance.GetCharacterName(buyerId);
        if ((buyerId != 0) && (buyerName == null))
            return false;

        buyerName ??= "";

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

    public bool SetForSale(ushort houseTlId, uint price, uint buyerId, Character seller) => SetForSale(GetHouseByTlId(houseTlId), price, buyerId, seller);

    /// <summary>
    /// Cancels a sale
    /// </summary>
    /// <param name="house"></param>
    /// <param name="returnCertificates"></param>
    /// <returns></returns>
    public static bool CancelForSale(House house, bool returnCertificates = true)
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
                var mail = new BaseMail
                {
                    MailType = MailType.HousingSale, 
                    Header =
                    {
                        ReceiverId = house.OwnerId, 
                        SenderName = ".houseSellCancel"
                    }, 
                    ReceiverName = NameManager.Instance.GetCharacterName(house.OwnerId),
                    Title = "title(" + ZoneManager.Instance.GetZoneByKey(house.Transform.ZoneId)?.GroupId.ToString() + ",'" + house.Name + "')",
                    Body =
                    {
                        Text = "body('" + house.Name + "', " + Item.AppraisalCertificate.ToString() + ", " + certAmount.ToString() + ")"
                    }
                };
                mail.Body.Attachments.AddRange(addedItems);
                mail.Body.SendDate = DateTime.UtcNow;
                mail.Body.RecvDate = DateTime.UtcNow.AddMilliseconds(1);
                mail.Send();
            }
            else
            {
                // Failed to create Appraisal certificate ?
                Logger.Warn("CancelForSale - Failed to create Appraisal Certificates for mail");
                return false;
            }
        }

        house.BroadcastPacket(new SCHouseResetForSalePacket(house.TlId, house.Name), false);
        SetForSaleMarkers(house, false);

        return true;
    }

    public bool CancelForSale(ushort houseTlId, bool returnCertificates = true) => CancelForSale(GetHouseByTlId(houseTlId), returnCertificates);

    /// <summary>
    /// Updates all furniture on the house to a new owner and broadcasts packets for it
    /// </summary>
    /// <param name="house"></param>
    /// <param name="characterId"></param>
    /// <returns>The number of items that have their owner information updated</returns>
    private static uint UpdateFurnitureOwner(House house, uint characterId)
    {
        uint res = 0;
        var furnitureList = WorldManager.Instance.GetDoodadByHouseDbId(house.Id);
        foreach (var furniture in furnitureList)
        {
            if (furniture.AttachPoint != AttachPointKind.None)
                continue;
            furniture.OwnerId = characterId;
            furniture.BroadcastPacket(new SCDoodadOriginatorPacket(furniture.ObjId, characterId, 0), true);
            res++;
        }
        return res;
    }

    /// <summary>
    /// Buys the house using money amount
    /// </summary>
    /// <param name="houseTlId"></param>
    /// <param name="money"></param>
    /// <param name="character"></param>
    /// <returns>Returns true if successful</returns>
    public bool BuyHouse(ushort houseTlId, uint money, Character character)
    {
        var house = GetHouseByTlId(houseTlId);

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

        if (house.OwnerId == character.Id)
        {
            // Cannot buy own building
            character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsOwner);
            return false;
        }

        // NOTE: check tax due maybe ?

        if (!character.SubtractMoney(SlotType.Bag, (int)house.SellPrice, ItemTaskType.BuyHouse))
        {
            // Not enough money
            character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsNotEnoughMoney);
            return false;
        }

        var previousOwner = house.OwnerId;
        var previousOwnerName = NameManager.Instance.GetCharacterName(previousOwner);

        // Mail confirmation mail to new owner
        var newOwnerMail = new BaseMail
        {
            MailType = MailType.HousingSale, 
            Header =
            {
                ReceiverId = character.Id, 
                SenderName = ".houseBought"
            }, 
            ReceiverName = character.Name,
            Title = "title(" + ZoneManager.Instance.GetZoneByKey(house.Transform.ZoneId)?.GroupId.ToString() + ",'" + house.Name + "')",
            Body =
            {
                Text = "body('" + previousOwnerName + "', '" + house.Name + "', " + house.SellPrice.ToString() + ")",
                SendDate = DateTime.UtcNow,
                RecvDate = DateTime.UtcNow.AddMilliseconds(1)
            }
        };
        newOwnerMail.Send();

        // Send sales money to previous owner
        var profitMail = new BaseMail
        {
            MailType = MailType.HousingSale, 
            Header =
            {
                ReceiverId = previousOwner, 
                SenderName = ".houseSold"
            }, 
            ReceiverName = previousOwnerName,
            Title = "title('" + character.Name + "','" + house.Name + "')",
            Body =
            {
                Text = "body('" + character.Name + "', '" + house.Name + "', " + house.SellPrice.ToString() + ")",
                CopperCoins = (int)house.SellPrice, // add the money
                SendDate = DateTime.UtcNow,
                RecvDate = DateTime.UtcNow.AddMilliseconds(1)
            }
        };
        profitMail.Send();

        ReturnHouseItemsToOwner(house, false, false, character);

        // Set new owner info
        house.SellPrice = 0;
        house.SellToPlayerId = 0;
        house.AccountId = character.AccountId;
        house.OwnerId = character.Id;
        house.CoOwnerId = character.Id; // not entirely sure if this actually needs to change
        house.Permission = house.Template.AlwaysPublic ? HousingPermission.Public : HousingPermission.Private;
        UpdateHouseFaction(house, character.Faction.Id);
        UpdateTaxInfo(house); // send tax due mails etc if needed ...

        // TODO: broadcast changes
        house.BroadcastPacket(
            new SCHouseSoldPacket(house.TlId, previousOwner, character.Id, character.AccountId, character.Name,
                house.Name), false);

        SetForSaleMarkers(house, false);

        character.SendPacket(new SCHouseStatePacket(house));
        var oldOwner = WorldManager.Instance.GetCharacterById(previousOwner);
        if ((oldOwner != null) && (oldOwner.IsOnline))
            oldOwner.SendPacket(new SCHouseRemovedPacket(house.TlId));

        UpdateFurnitureOwner(house, character.Id);
        ResidentManager.Instance.RemoveResidenMemberInfo(oldOwner);
        ResidentManager.Instance.AddResidenMemberInfo(character);

        house.IsDirty = true;

        return true;
    }

    /// <summary>
    /// Ticker function for checking all houses if they need tax mails sent
    /// </summary>
    public void CheckHousingTaxes()
    {
        if (_isCheckingTaxTiming)
            return;
        _isCheckingTaxTiming = true;
        try
        {
            // Logger.Trace("CheckHousingTaxes");
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
            Logger.Error(e);
        }

        _isCheckingTaxTiming = false;
    }

    /// <summary>
    /// Get decoration design by Id
    /// </summary>
    /// <param name="designId"></param>
    /// <returns></returns>
    private HousingDecoration GetDecorationDesignFromId(uint designId)
    {
        return _housingDecorations.GetValueOrDefault(designId);
    }

    /// <summary>
    /// Get decoration design from it's doodad counterpart
    /// </summary>
    /// <param name="doodadId"></param>
    /// <returns></returns>
    private HousingDecoration GetDecorationDesignFromDoodadId(uint doodadId)
    {
        var deco = _housingDecorations.FirstOrDefault(x => x.Value.DoodadId == doodadId).Value;
        return default ? null : deco;
    }

    /// <summary>
    /// Places a piece of furniture at a given location, using item and design
    /// </summary>
    /// <param name="player"></param>
    /// <param name="houseTlId"></param>
    /// <param name="designId"></param>
    /// <param name="pos"></param>
    /// <param name="quat"></param>
    /// <param name="parentObjId"></param>
    /// <param name="itemId"></param>
    /// <returns></returns>
    public bool DecorateHouse(Character player, ushort houseTlId, uint designId, Vector3 pos, Quaternion quat, uint parentObjId, ulong itemId)
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
        var house = GetHouseByTlId(houseTlId);
        if ((house == null) || (house.TlId != houseTlId))
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

        var doodad = DoodadManager.Instance.Create(0, decorationDesign.DoodadId, house, true);
        doodad.Transform.Parent = house.Transform;
        doodad.Transform.Local.SetPosition(pos.X, pos.Y, pos.Z);
        doodad.Transform.Local.ApplyFromQuaternion(quat);
        doodad.ItemTemplateId = item.TemplateId; // designId;
        doodad.ItemId = (item.Template.MaxCount <= 1) ? itemId : 0;
        doodad.OwnerDbId = house.Id;

        if (house.Id > 0 && item is BigFish fish)
        {
            var weight = (short)fish.Weight;
            var length = (short)fish.Length;
            doodad.Data = (length << 16) + weight;
        }

        doodad.OwnerId = player.Id;
        doodad.ParentObjId = house.ObjId;
        doodad.ParentObj = house;
        doodad.AttachPoint = AttachPointKind.None;
        doodad.OwnerType = DoodadOwnerType.Housing;
        doodad.UccId = itemUcc?.Id ?? 0;
        doodad.IsPersistent = true;

        if (doodad is DoodadCoffer coffer)
        {
            coffer.InitializeCoffer(player.Id);
        }

        doodad.InitDoodad();
        doodad.Spawn();
        doodad.Save();

        bool res;
        if (item.Template.MaxCount > 1)
        {
            // Stackable items are simply consumed
            res = (player.Inventory.Bag.ConsumeItem(ItemTaskType.DoodadCreate, item.TemplateId, 1, item) == 1);
        }
        else
        {
            // Non-stackable items are stored in the owner's system container as to retain crafter information and such 
            res = player.Inventory.SystemContainer.AddOrMoveExistingItem(ItemTaskType.DoodadCreate, item);
        }

        // Logger.Debug($"DecorateHouse => DoodadTemplate: {doodad.TemplateId} , DoodadId {doodad.ObjId}, Pos: {doodad.Transform}");
        return res;
    }

    /// <summary>
    /// Toggles the allow furniture recovery flag
    /// </summary>
    /// <param name="character"></param>
    /// <param name="houseTl"></param>
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
