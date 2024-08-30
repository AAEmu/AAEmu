using System;
using System.Collections.Generic;
using System.IO;

using AAEmu.Commons.Exceptions;
using AAEmu.Commons.IO;
using AAEmu.Commons.Models;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Attendance;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Char.Templates;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Characters;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers.UnitManagers;

public class CharacterManager : Singleton<CharacterManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<byte, CharacterTemplate> _templates;
    private readonly Dictionary<AbilityType, AbilityItems> _abilityItems;
    private readonly Dictionary<int, List<Expand>> _expands;
    private readonly Dictionary<uint, AppellationTemplate> _appellations;
    private readonly Dictionary<uint, ActabilityTemplate> _actabilities;
    private readonly Dictionary<int, ExpertLimit> _expertLimits;
    private readonly Dictionary<int, ExpandExpertLimit> _expandExpertLimits;

    public CharacterManager()
    {
        _templates = new Dictionary<byte, CharacterTemplate>();
        _abilityItems = new Dictionary<AbilityType, AbilityItems>();
        _expands = new Dictionary<int, List<Expand>>();
        _appellations = new Dictionary<uint, AppellationTemplate>();
        _actabilities = new Dictionary<uint, ActabilityTemplate>();
        _expertLimits = new Dictionary<int, ExpertLimit>();
        _expandExpertLimits = new Dictionary<int, ExpandExpertLimit>();
    }

    public CharacterTemplate GetTemplate(Race race, Gender gender)
    {
        return _templates[(byte)(16 * (byte)gender + (byte)race)];
    }

    public AppellationTemplate GetAppellationsTemplate(uint id)
    {
        if (_appellations.TryGetValue(id, out var template))
            return template;
        return null;
    }

    public List<Expand> GetExpands(int step)
    {
        return _expands[step];
    }

    public ActabilityTemplate GetActability(uint id)
    {
        return _actabilities[id];
    }

    public ExpertLimit GetExpertLimit(int step)
    {
        if (_expertLimits.TryGetValue(step, out var limit))
            return limit;
        return null;
    }

    public ExpandExpertLimit GetExpandExpertLimit(int step)
    {
        if (_expandExpertLimits.TryGetValue(step, out var limit))
            return limit;
        return null;
    }

    public void Load()
    {
        Logger.Info("Loading character templates...");

        using (var connection = SQLite.CreateConnection())
        {
            var temp = new Dictionary<uint, byte>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM characters";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new CharacterTemplate();
                        var id = reader.GetUInt32("id");
                        template.Race = (Race)reader.GetByte("char_race_id");
                        template.Gender = (Gender)reader.GetByte("char_gender_id");
                        template.ModelId = reader.GetUInt32("model_id");
                        template.FactionId = (FactionsEnum)reader.GetUInt32("faction_id");
                        template.ZoneId = reader.GetUInt32("starting_zone_id");
                        template.ReturnDistrictId = reader.GetUInt32("default_return_district_id");
                        template.ResurrectionDistrictId = reader.GetUInt32("default_resurrection_district_id");
                        using (var command2 = connection.CreateCommand())
                        {
                            command2.CommandText = "SELECT * FROM item_body_parts WHERE model_id=@model_id";
                            command2.Parameters.AddWithValue("model_id", template.ModelId);
                            command2.Prepare();
                            using (var reader2 = new SQLiteWrapperReader(command2.ExecuteReader()))
                            {
                                while (reader2.Read())
                                {
                                    var itemId = reader2.GetUInt32("item_id", 0);
                                    var slot = reader2.GetInt32("slot_type_id") - 23;
                                    template.Items[slot] = itemId;
                                }
                            }
                        }

                        var templateId = (byte)(16 * (byte)template.Gender + (byte)template.Race);
                        _templates.Add(templateId, template);
                        temp.Add(id, templateId);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM character_buffs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var characterId = reader.GetUInt32("character_id");
                        var buffId = reader.GetUInt32("buff_id");

                        if (temp.TryGetValue(characterId, out var tempValue) && _templates.TryGetValue(tempValue, out var template))
                        {
                            template.Buffs.Add(buffId);
                        }
                        else
                        {
                            // Обработка случая, когда template не найден
                            Logger.Warn($"Template for characterId {characterId} not found.");
                        }
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM character_supplies";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var ability = (AbilityType)reader.GetByte("ability_id");
                        var item = new AbilitySupplyItem();
                        item.Id = reader.GetUInt32("item_id");
                        item.Amount = reader.GetInt32("amount");
                        item.Grade = reader.GetByte("grade_id");

                        if (!_abilityItems.TryGetValue(ability, out var abilityItems))
                        {
                            abilityItems = new AbilityItems();
                            _abilityItems.Add(ability, abilityItems);
                        }

                        abilityItems.Supplies.Add(item);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM character_equip_packs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var ability = (AbilityType)reader.GetUInt32("id");
                        var template = new AbilityItems { Ability = ability, Items = new EquipItemsTemplate() };
                        var clothPack = reader.GetUInt32("newbie_cloth_pack_id", 0);
                        var weaponPack = reader.GetUInt32("newbie_weapon_pack_id", 0);
                        if (clothPack > 0)
                        {
                            using (var command2 = connection.CreateCommand())
                            {
                                command2.CommandText = "SELECT * FROM equip_pack_cloths WHERE id=@id";
                                command2.Parameters.AddWithValue("id", clothPack);
                                command2.Prepare();
                                using (var reader2 = new SQLiteWrapperReader(command2.ExecuteReader()))
                                {
                                    while (reader2.Read())
                                    {
                                        template.Items.Back = reader2.GetUInt32("back_id");
                                        template.Items.BackGrade = reader2.GetByte("back_grade_id");
                                        template.Items.Backpack = reader2.GetByte("backpack_id");
                                        template.Items.BackpackGrade = reader2.GetUInt32("backpack_grade_id");
                                        template.Items.Belt = reader2.GetUInt32("belt_id");
                                        template.Items.BeltGrade = reader2.GetByte("belt_grade_id");
                                        template.Items.Bracelet = reader2.GetUInt32("bracelet_id");
                                        template.Items.BraceletGrade = reader2.GetByte("bracelet_grade_id");
                                        template.Items.Cosplay = reader2.GetUInt32("cosplay_id");
                                        template.Items.CosplayGrade = reader2.GetByte("cosplay_grade_id");
                                        template.Items.Gloves = reader2.GetUInt32("glove_id");
                                        template.Items.GlovesGrade = reader2.GetByte("glove_grade_id");
                                        template.Items.Headgear = reader2.GetUInt32("headgear_id");
                                        template.Items.HeadgearGrade = reader2.GetByte("headgear_grade_id");
                                        template.Items.Necklace = reader2.GetUInt32("necklace_id");
                                        template.Items.NecklaceGrade = reader2.GetByte("necklace_grade_id");
                                        template.Items.Pants = reader2.GetUInt32("pants_id");
                                        template.Items.PantsGrade = reader2.GetByte("pants_grade_id");
                                        template.Items.Shirt = reader2.GetUInt32("shirt_id");
                                        template.Items.ShirtGrade = reader2.GetByte("shirt_grade_id");
                                        template.Items.Shoes = reader2.GetUInt32("shoes_id");
                                        template.Items.ShoesGrade = reader2.GetByte("shoes_grade_id");
                                        template.Items.Stabilizer = reader2.GetUInt32("stabilizer_id");
                                        template.Items.StabilizerGrade = reader2.GetByte("stabilizer_grade_id");
                                        template.Items.Underpants = reader2.GetUInt32("underpants_id");
                                        template.Items.UnderpantsGrade = reader2.GetByte("underpants_grade_id");
                                        template.Items.Undershirts = reader2.GetUInt32("undershirt_id");
                                        template.Items.UndershirtsGrade = reader2.GetByte("undershirt_grade_id");
                                    }
                                }
                            }
                        }

                        if (weaponPack > 0)
                        {
                            using (var command2 = connection.CreateCommand())
                            {
                                command2.CommandText = "SELECT * FROM equip_pack_weapons WHERE id=@id";
                                command2.Parameters.AddWithValue("id", weaponPack);
                                command2.Prepare();
                                using (var reader2 = new SQLiteWrapperReader(command2.ExecuteReader()))
                                {
                                    while (reader2.Read())
                                    {
                                        template.Items.Mainhand = reader2.GetUInt32("mainhand_id");
                                        template.Items.MainhandGrade = reader2.GetByte("mainhand_grade_id");
                                        template.Items.Musical = reader2.GetUInt32("musical_id");
                                        template.Items.MusicalGrade = reader2.GetByte("musical_grade_id");
                                        template.Items.Offhand = reader2.GetUInt32("offhand_id");
                                        template.Items.OffhandGrade = reader2.GetByte("offhand_grade_id");
                                        template.Items.Ranged = reader2.GetUInt32("ranged_id");
                                        template.Items.RangedGrade = reader2.GetByte("ranged_grade_id");
                                    }
                                }
                            }
                        }

                        _abilityItems.Add(template.Ability, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM bag_expands";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var expand = new Expand();
                        expand.CurrencyId = reader.GetInt32("currency_id");
                        expand.IsBank = reader.GetBoolean("is_bank", true);
                        expand.ItemCount = reader.GetInt32("item_count");
                        expand.ItemId = reader.GetUInt32("item_id", 0);
                        expand.Price = reader.GetInt32("price");
                        expand.Step = reader.GetInt32("step");

                        if (!_expands.TryGetValue(expand.Step, out var expandList))
                        {
                            expandList = new List<Expand> { expand };
                            _expands.Add(expand.Step, expandList);
                        }
                        else
                        {
                            expandList.Add(expand);
                        }
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM appellations";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new AppellationTemplate();
                        template.Id = reader.GetUInt32("id");
                        template.ApplyAppellationAtOnce = reader.GetBoolean("apply_appellation_at_once");
                        template.BuffId = reader.GetUInt32("buff_id", 0);
                        template.IconId = reader.GetUInt32("icon_id", 0);
                        template.Name = reader.GetString("name");
                        template.OrderIndex = reader.GetUInt32("order_index", 0);
                        template.RouteDesc = reader.GetString("route_desc");
                        template.RouteKindId = reader.GetUInt32("route_kind_id", 0);
                        template.RoutePopup = reader.GetBoolean("route_popup");
                        template.RouteType = reader.GetUInt32("route_type", 0);

                        _appellations.Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM actability_groups";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ActabilityTemplate();
                        template.Id = reader.GetUInt32("id");
                        template.IconId = reader.GetUInt32("icon_id", 0);
                        template.Name = reader.GetString("name");
                        template.SkillPageVisible = reader.GetBoolean("skill_page_visible");
                        template.UnitAttributeId = reader.GetInt32("unit_attr_id");
                        template.Visible = reader.GetBoolean("visible");

                        _actabilities.Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM expert_limits ORDER BY up_limit ASC";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    var step = 0;
                    while (reader.Read())
                    {
                        var template = new ExpertLimit();
                        template.Id = reader.GetUInt32("id");
                        template.UpLimit = reader.GetInt32("up_limit");
                        template.ExpertLimitCount = reader.GetByte("expert_limit");
                        template.Advantage = reader.GetInt32("advantage");
                        template.CastAdvantage = reader.GetInt32("cast_adv");
                        template.UpCurrencyId = reader.GetUInt32("up_currency_id", 0);
                        template.UpPrice = reader.GetInt32("up_price");
                        template.DownCurrencyId = reader.GetUInt32("down_currency_id", 0);
                        template.DownPrice = reader.GetInt32("down_price");
                        _expertLimits.Add(step++, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM expand_expert_limits ORDER BY expand_count ASC";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    var step = 0;
                    while (reader.Read())
                    {
                        var template = new ExpandExpertLimit();
                        //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                        template.ExpandCount = reader.GetByte("expand_count");
                        template.LifePoint = reader.GetInt32("life_point");
                        template.ItemId = reader.GetUInt32("item_id", 0);
                        template.ItemCount = reader.GetInt32("item_count");
                        _expandExpertLimits.Add(step++, template);
                    }
                }
            }
        }

        var filePath = Path.Combine(FileManager.AppPath, "Data", "CharTemplates.json");
        var content = FileManager.GetFileContents(filePath);
        if (string.IsNullOrWhiteSpace(content))
            throw new IOException($"File {filePath} doesn't exists or is empty.");

        if (JsonHelper.TryDeserializeObject(content, out List<CharacterTemplateConfig> charTemplates, out _))
        {
            foreach (var charTemplate in charTemplates)
            {
                var point = charTemplate.Pos.Clone();
                // Recalculate ZoneId as this isn't included in the config
                // Always use main_world Id for this
                point.ZoneId = WorldManager.Instance.GetZoneId(WorldManager.DefaultWorldId, charTemplate.Pos.X, charTemplate.Pos.Y);
                // Convert the json's degrees to rads
                point.Roll = point.Roll.DegToRad();
                point.Pitch = point.Pitch.DegToRad();
                point.Yaw = point.Yaw.DegToRad();

                // Males
                var template = _templates[(byte)(16 + charTemplate.Id)];
                template.SpawnPosition = point;
                template.SpawnPosition.WorldId = WorldManager.DefaultWorldId;
                template.NumInventorySlot = charTemplate.NumInventorySlot;
                template.NumBankSlot = charTemplate.NumBankSlot;

                // Females
                template = _templates[(byte)(32 + charTemplate.Id)];
                template.SpawnPosition = point;
                template.SpawnPosition.WorldId = WorldManager.DefaultWorldId;
                template.NumInventorySlot = charTemplate.NumInventorySlot;
                template.NumBankSlot = charTemplate.NumBankSlot;
            }
        }
        else
            throw new GameException($"CharacterManager: Error parsing {filePath} file");

        Logger.Info("Loaded {0} character templates", _templates.Count);
    }

    public static void PlayerRoll(Character Self, int max)
    {

        var roll = Rand.Next(1, max);
        Self.BroadcastPacket(new SCChatMessagePacket(ChatType.System, string.Format(Self.Name + " rolled " + roll.ToString() + ".")), true);

    }

    public int GetEffectiveAccessLevel(Character character)
    {
        var accountDetails = AccountManager.Instance.GetAccountDetails(character.AccountId);
        return Math.Max(character.AccessLevel, accountDetails.AccessLevel);
    }

    public void Create(GameConnection connection, string name, Race race, Gender gender, uint[] bodyItems, UnitCustomModelParams customModel, AbilityType ability1, AbilityType ability2, AbilityType ability3, byte level)
    {
        name = name.NormalizeName();
        var nameValidationCode = NameManager.Instance.ValidationCharacterName(name);
        if (nameValidationCode != CharacterCreateError.Ok)
        {
            connection.SendPacket(new SCCharacterCreationFailedPacket(nameValidationCode));
            return;
        }

        // NOTE: This is purely a warning to log potential cheaters
        // If you have custom starting classes, make sure to comment or adjust this
        if ((ability2 != AbilityType.None) || (ability3 != AbilityType.None))
        {
            Logger.Error($"User tried to make a new character that has 2nd and/or 3rd ability already set. Account {connection.AccountId}, Name {name}, Class {ability1}, {ability2}, {ability3}");
        }

        var accountDetails = AccountManager.Instance.GetAccountDetails(connection.AccountId);

        // Get default access level for all users 
        var useAccessLevel = AppConfiguration.Instance.Account.AccessLevelDefault;

        // If it's the first character created, use first character access level settings 
        if (NameManager.Instance.NoNamesRegistered())
            useAccessLevel = Math.Max(AppConfiguration.Instance.Account.AccessLevelFirstCharacter, useAccessLevel);

        var characterId = CharacterIdManager.Instance.GetNextId();
        NameManager.Instance.AddCharacterName(characterId, name, connection.AccountId);
        var template = GetTemplate(race, gender);

        var character = new Character(customModel);
        character.Id = characterId;
        character.TemplateId = characterId;
        character.AccountId = connection.AccountId;
        character.Name = name;
        character.Race = race;
        character.Gender = gender;
        character.Transform.ApplyWorldSpawnPosition(template.SpawnPosition);
        character.Level = level;
        character.Faction = FactionManager.Instance.GetFaction(template.FactionId);
        character.FactionName = "";
        character.AccessLevel = useAccessLevel;
        // character.LaborPower = (short)AppConfiguration.Instance.Labor.Default;
        // character.LaborPowerModified = DateTime.UtcNow;
        character.InitializeLaborCache(accountDetails.Labor, accountDetails.LastUpdated); // Initialize Labor cache, so we don't need to query the DB every time we need to read it
        character.NumInventorySlots = template.NumInventorySlot;
        character.NumBankSlots = template.NumBankSlot;
        character.Inventory = new Inventory(character);
        character.Created = DateTime.UtcNow;
        character.Updated = DateTime.UtcNow;
        character.Ability1 = ability1;
        character.Ability2 = ability2;
        character.Ability3 = ability3;
        character.ReturnDistrictId = template.ReturnDistrictId;
        character.ResurrectionDistrictId = template.ResurrectionDistrictId;
        character.Slots = new ActionSlot[Character.MaxActionSlots];
        for (var i = 0; i < character.Slots.Length; i++)
            character.Slots[i] = new ActionSlot();

        var items = _abilityItems[ability1];
        SetEquipItemTemplate(character.Inventory, items.Items.Headgear, EquipmentItemSlot.Head, items.Items.HeadgearGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Necklace, EquipmentItemSlot.Neck, items.Items.NecklaceGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Shirt, EquipmentItemSlot.Chest, items.Items.ShirtGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Belt, EquipmentItemSlot.Waist, items.Items.BeltGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Pants, EquipmentItemSlot.Legs, items.Items.PantsGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Gloves, EquipmentItemSlot.Hands, items.Items.GlovesGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Shoes, EquipmentItemSlot.Feet, items.Items.ShoesGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Bracelet, EquipmentItemSlot.Arms, items.Items.BraceletGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Back, EquipmentItemSlot.Back, items.Items.BackGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Undershirts, EquipmentItemSlot.Undershirt, items.Items.UndershirtsGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Underpants, EquipmentItemSlot.Underpants, items.Items.UnderpantsGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Mainhand, EquipmentItemSlot.Mainhand, items.Items.MainhandGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Offhand, EquipmentItemSlot.Offhand, items.Items.OffhandGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Ranged, EquipmentItemSlot.Ranged, items.Items.RangedGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Musical, EquipmentItemSlot.Musical, items.Items.MusicalGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Cosplay, EquipmentItemSlot.Cosplay, items.Items.CosplayGrade);
        SetEquipItemTemplate(character.Inventory, items.Items.Stabilizer, EquipmentItemSlot.Stabilizer, items.Items.StabilizerGrade);
        for (var i = 0; i < 7; i++)
        {
            if (bodyItems[i] == 0 && template.Items[i] > 0)
                bodyItems[i] = template.Items[i];
            SetEquipItemTemplate(character.Inventory, bodyItems[i], (EquipmentItemSlot)(i + 19), 0);
        }

        byte slot = 10;
        foreach (var item in items.Supplies)
        {
            character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Invalid, item.Id, item.Amount, item.Grade);
            //var createdItem = ItemManager.Instance.Create(item.Id, item.Amount, item.Grade);
            //character.Inventory.AddItem(Models.Game.Items.Actions.ItemTaskType.Invalid, createdItem);

            character.SetAction(slot, ActionSlotType.ItemType, item.Id);
            slot++;
        }

        items = _abilityItems[0];
        if (items != null)
            foreach (var item in items.Supplies)
            {
                character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Invalid, item.Id, item.Amount, item.Grade);
                //var createdItem = ItemManager.Instance.Create(item.Id, item.Amount, item.Grade);
                //character.Inventory.AddItem(ItemTaskType.Invalid, createdItem);

                character.SetAction(slot, ActionSlotType.ItemType, item.Id);
                slot++;
            }

        character.Abilities = new CharacterAbilities(character);
        character.Abilities.SetAbility(character.Ability1, 0);

        character.Actability = new CharacterActability(character);
        foreach (var (id, actabilityTemplate) in _actabilities)
            character.Actability.Actabilities.Add(id, new Actability(actabilityTemplate));

        character.Skills = new CharacterSkills(character);
        foreach (var skill in SkillManager.Instance.GetDefaultSkills())
        {
            if (!skill.AddToSlot)
                continue;
            character.SetAction(skill.Slot, ActionSlotType.Spell, skill.Template.Id);
        }

        slot = 1;
        while (character.Slots[slot].Type != ActionSlotType.None)
            slot++;
        foreach (var skill in SkillManager.Instance.GetStartAbilitySkills(character.Ability1))
        {
            character.Skills.AddSkill(skill, 1, false);
            character.SetAction(slot, ActionSlotType.Spell, skill.Id);
            slot++;
        }

        character.Appellations = new CharacterAppellations(character);
        character.Quests = new CharacterQuests(character);
        character.Mails = new CharacterMails(character);
        character.Portals = new CharacterPortals(character);
        character.Friends = new CharacterFriends(character);
        character.Attendances = new CharacterAttendances(character);

        character.Hp = character.MaxHp;
        character.Mp = character.MaxMp;

        if (character.SaveDirectlyToDatabase())
        {
            connection.Characters.Add(character.Id, character);
            connection.SendPacket(new SCCreateCharacterResponsePacket(character));
        }
        else
        {
            // There is no actual response for internal DB saving error for the client.
            // Just send a generic Failed error (Name already in use for pending deletion)
            connection.SendPacket(new SCCharacterCreationFailedPacket(CharacterCreateError.Failed));
            CharacterIdManager.Instance.ReleaseId(characterId);
            NameManager.Instance.RemoveCharacterName(characterId);
            // TODO release items...
            DeleteCharacterAssets(character, true);
        }
    }

    /// <summary>
    /// Removed all items and assets this character currently owns
    /// </summary>
    /// <param name="character">Character to delete assets from</param>
    /// <param name="fullWipe">Do owned items need to be actually deleted</param>
    public static void DeleteCharacterAssets(Character character, bool fullWipe)
    {
        // Demolish owned houses
        var myHouses = new Dictionary<uint, House>();
        if (HousingManager.Instance.GetByCharacterId(myHouses, character.Id) > 0)
        {
            foreach (var (houseId, house) in myHouses)
            {
                house.Permission = HousingPermission.Public;
                // force expire the house
                // This should technically kill the house, and return the minimum amount of furniture
                house.ProtectionEndDate = DateTime.UtcNow.AddDays(-21);
                HousingManager.UpdateTaxInfo(house);
            }
        }

        // Remove from Guild
        if (character.Expedition != null)
            ExpeditionManager.Leave(character);

        // Remove from Family
        if (character.Family > 0)
            FamilyManager.Instance.LeaveFamily(character);

        // TODO: Remove from player nation
        // TODO: Delete leadership

        // Return all mails to sender (if needed)
        // The main reason we do this is so other people's items wouldn't get delete if fullWipe is enabled
        foreach (var (mailId, mail) in MailManager.Instance._allPlayerMails)
        {
            if (mail.CanReturnMail() && !mail.ReturnToSender())
                Logger.Warn(
                    "DeleteCharacterAssets - Unable to return mail to sender for mail: {0}, deleted char: {1}({2}), sender: {3}({4})",
                    mail.Id,
                    mail.Header.ReceiverName, mail.Header.ReceiverId,
                    mail.Header.SenderName, mail.Header.SenderId);
        }

        if (!fullWipe)
            return;

        Logger.Warn("DeleteCharacterAssets - fullWipe is currently not implemented yet, charId: {0}", character.Id);
        // TODO: Wipe all mails
        // TODO: Wipe all items/gold (this also deletes all pets/vehicles)
    }

    /// <summary>
    /// Mark characters marked for deletion as deleted after their time is finished
    /// </summary>
    /// <param name="character"></param>
    /// <param name="gameConnection"></param>
    /// <param name="dbConnection"></param>
    /// <returns>Returns true if a character was marked deleted, otherwise false</returns>
    public static bool CheckForDeletedCharactersDeletion(Character character, GameConnection gameConnection, MySqlConnection dbConnection)
    {
        if ((character.DeleteTime > DateTime.MinValue) && (character.DeleteTime <= DateTime.UtcNow))
        {
            Logger.Info("CheckForDeletedCharactersDeletion - Deleting Account:{0} Id:{1} Name:{2}", character.AccountId, character.Id, character.Name);
            using (var command = dbConnection.CreateCommand())
            {
                var deletedName = character.Name;
                if (AppConfiguration.Instance.Account.DeleteReleaseName)
                {
                    deletedName = "!" + character.Name;
                    NameManager.Instance.RemoveCharacterName(character.Id);
                    NameManager.Instance.AddCharacterName(character.Id, deletedName, character.AccountId);
                }

                command.Connection = dbConnection;
                command.CommandText = "UPDATE `characters` SET `deleted`='1', `delete_time`=@new_delete_time, `name`=@deletedname WHERE `id`=@char_id and `account_id`=@account_id;";
                command.Parameters.AddWithValue("@new_delete_time", DateTime.MinValue);
                command.Parameters.AddWithValue("@char_id", character.Id);
                command.Parameters.AddWithValue("@account_id", character.AccountId);
                command.Parameters.AddWithValue("@deletedname", deletedName);

                var res = command.ExecuteNonQuery();
                // Send update to current connection
                if (res > 0)
                {
                    DeleteCharacterAssets(character, false);

                    // Send delete packet to the player if online
                    if (gameConnection != null)
                    {
                        gameConnection.SendPacket(new SCCharacterDeletedPacket(character.Id, character.Name));
                        // Not sure if this is the way it should be send or not, but it seems to work with status 1
                        gameConnection.SendPacket(new SCCharacterDeleteResponsePacket(character.Id, 1, character.DeleteRequestTime, character.DeleteTime));
                    }
                }
                return res > 0;
            }
        }
        else
        if (character.DeleteRequestTime > DateTime.MinValue)
        {
            Logger.Warn("CheckForDeletedCharactersDeletion - Delete request for Account:{0} Id:{1} Name:{2}, but character is no longer marked for deletion (possibly cancelled delete)", character.AccountId, character.Id, character.Name);
        }
        return false;
    }

    public static void CheckForDeletedCharacters()
    {
        var nextCheckTime = DateTime.MaxValue;
        var deleteList = new List<(uint, uint)>(); // charId, accountId

        Logger.Debug("CheckForDeletedCharacters - Begin");
        using (var connection = MySQL.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                // TODO: Update this query to be more efficient
                command.CommandText = "SELECT `id`, `name`, `account_id`, `delete_time` FROM characters WHERE `deleted`=0";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // Check the delete time for this entry
                        var deleteTime = reader.GetDateTime("delete_time");
                        var charId = reader.GetUInt32("id");
                        var accountId = reader.GetUInt32("account_id");
                        if ((deleteTime > DateTime.MinValue) && (deleteTime <= DateTime.UtcNow))
                        {
                            deleteList.Add((charId, accountId));
                        }
                        else
                        if ((deleteTime > DateTime.MinValue) && (deleteTime < nextCheckTime))
                        {
                            nextCheckTime = deleteTime;
                        }
                    }
                }
            }

            // Actually start deleting
            foreach (var (charId, accountId) in deleteList)
            {
                var character = Character.Load(connection, charId, accountId);
                if (character != null)
                {
                    var accountConnection = GameConnectionTable.Instance?.GetConnectionByAccount(character.AccountId) ?? null;
                    if (CheckForDeletedCharactersDeletion(character, accountConnection, connection))
                        Logger.Info("CheckForDeletedCharacters - Delete charId:{0}", charId);
                    else
                        // Failed to delete character from DB
                        Logger.Error("CheckForDeletedCharacters - Failed to delete character for deletion charId:{0}", charId);
                }
                else
                {
                    // Failed to load character for deletion somehow
                    Logger.Error("CheckForDeletedCharacters - Failed to load character for deletion charId:{0}", charId);
                }
            }
        }

        // Start a Delete Tick Task
        if (nextCheckTime < DateTime.MaxValue)
        {
            var deleteCheckTask = new CharacterDeleteTask();
            TaskManager.Instance?.Schedule(deleteCheckTask, nextCheckTime - DateTime.UtcNow);
            Logger.Debug("CheckForDeletedCharacters - Next delete scheduled at " + nextCheckTime.ToString());
        }
        else
        {
            Logger.Debug("CheckForDeletedCharacters - No new deletions scheduled");
        }
    }

    public static void SetDeleteCharacter(GameConnection gameConnection, uint characterId)
    {
        if (gameConnection.Characters.TryGetValue(characterId, out var character))
        {
            character.DeleteRequestTime = DateTime.UtcNow;

            var targetDeleteDelay = 0;

            // Get timings from settings
            foreach (var timing in AppConfiguration.Instance.Account.DeleteTimings)
            {
                if (character.Level >= timing.Level)
                    targetDeleteDelay = timing.Delay;
            }

            // Add the actual timing
            character.DeleteTime = character.DeleteRequestTime.AddMinutes(targetDeleteDelay);

            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE characters SET `delete_request_time` = @delete_request_time, `delete_time` = @delete_time WHERE `id` = @id";
                    command.Parameters.AddWithValue("@delete_request_time", character.DeleteRequestTime);
                    command.Parameters.AddWithValue("@delete_time", character.DeleteTime);
                    command.Parameters.AddWithValue("@id", character.Id);
                    command.Prepare();
                    if (command.ExecuteNonQuery() == 1)
                    {
                        gameConnection.SendPacket(new SCCharacterDeleteResponsePacket(character.Id, 2, character.DeleteRequestTime, character.DeleteTime));
                    }
                    else
                    {
                        // Failed to mark for deletion
                        // Not the correct message, but it seems funny enough
                        gameConnection.SendPacket(new SCErrorMsgPacket(ErrorMessageType.CannotDeleteCharWhileBotSuspected, 0, true));
                    }
                }
            }
        }
        else
        {
            gameConnection.SendPacket(new SCCharacterDeleteResponsePacket(characterId, 0));
        }
        // Trigger our task queueing
        CheckForDeletedCharacters();
    }

    public static void SetRestoreCharacter(GameConnection gameConnection, uint characterId)
    {
        if (gameConnection.Characters.TryGetValue(characterId, out var character))
        {
            character.DeleteRequestTime = DateTime.MinValue;
            character.DeleteTime = DateTime.MinValue;
            gameConnection.SendPacket(new SCCharacterDeleteCanceledPacket(character.Id, 3));

            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE characters SET `delete_request_time` = @delete_request_time, `delete_time` = @delete_time WHERE `id` = @id";
                    command.Parameters.AddWithValue("@delete_request_time", character.DeleteRequestTime);
                    command.Parameters.AddWithValue("@delete_time", character.DeleteTime);
                    command.Parameters.AddWithValue("@id", character.Id);
                    command.Prepare();
                    command.ExecuteNonQuery();
                }
            }
        }
        else
        {
            gameConnection.SendPacket(new SCCharacterDeleteCanceledPacket(characterId, 4));
        }
    }
    public static List<LoginCharacterInfo> LoadCharacters(ulong accountId)
    {
        var result = new List<LoginCharacterInfo>();
        using (var connection = MySQL.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT `id`, `name`, `race`, `gender`,`delete_time` FROM characters WHERE `account_id`=@accountId and `deleted`=0";
                command.Parameters.AddWithValue("@accountId", accountId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // Skip this char in the list if it's read to be deleted
                        var deleteTime = reader.GetDateTime("delete_time");
                        if ((deleteTime > DateTime.MinValue) && (deleteTime < DateTime.UtcNow))
                            continue;

                        var character = new LoginCharacterInfo();
                        character.AccountId = accountId;
                        character.Id = reader.GetUInt32("id");
                        character.Name = reader.GetString("name");
                        character.Race = reader.GetByte("race");
                        character.Gender = reader.GetByte("gender");
                        result.Add(character);
                    }
                }
            }
        }
        return result;
    }

    private static void SetEquipItemTemplate(Inventory inventory, uint templateId, EquipmentItemSlot slot, byte grade)
    {
        Item item = null;
        if (templateId > 0)
        {
            item = ItemManager.Instance.Create(templateId, 1, grade);
            item.SlotType = SlotType.Equipment;
            item.Slot = (int)slot;
        }

        inventory.Equipment.AddOrMoveExistingItem(ItemTaskType.Invalid, item, (int)slot);
        //inventory.Equip[(int) slot] = item;
    }

    public static void ApplyBeautySalon(Character character, uint hairModel, UnitCustomModelParams modelParams)
    {
        // TODO: Add support for future X-day Salon Certificate items

        if (character.Inventory.GetItemsCount(SlotType.Inventory, Item.SalonCertificate) <= 0)
            return;

        var oldHair = character.Equipment.GetItemBySlot((byte)EquipmentItemSlot.Hair);

        // Check if hair changed
        if ((oldHair != null) && (oldHair.TemplateId != hairModel))
        {
            // Remove old hair item
            oldHair._holdingContainer.RemoveItem(ItemTaskType.Invalid, oldHair, true);
            // Create new hair item
            if (!character.Equipment.AcquireDefaultItemEx(ItemTaskType.Invalid, hairModel, 1, -1,
                    out var newItemsList, out var _, character.Id, (int)EquipmentItemSlot.Hair))
            {
                Logger.Error($"Failed to add new hairstyle for player {character.Name} ({character.Id})!");
            }

            if (newItemsList.Count != 1)
            {
                Logger.Error($"Something failed during hairstyle creation for player {character.Name} ({character.Id})!");
            }
        }
        character.ModelParams = modelParams;

        character.BroadcastPacket(new SCCharacterGenderAndModelModifiedPacket(character), true);

        if (character.Inventory.Bag.ConsumeItem(ItemTaskType.EditCosmetic, Item.SalonCertificate, 1, null) <= 0)
            Logger.Error($"Could not consume salon certificate for player {character.Name} ({character.Id})!");

        // The client will do a salon leave request after it gets the SCCharacterGenderAndModelModifiedPacket
    }

    public bool IsCharacterPendingDeletion(string name)
    {
        using (var connection = MySQL.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM characters WHERE `name` = @name";
                command.Parameters.AddWithValue("@name", name);
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        return reader.GetBoolean("deleted") || reader.GetDateTime("delete_request_time") > DateTime.MinValue;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Starts the tracker for online time
    /// </summary>
    public void StartOnlineTracking()
    {
        var onlineTrackerTasks = new CharacterOnlineTrackingTask();
        TaskManager.Instance.Schedule(onlineTrackerTasks, TimeSpan.Zero, CharacterOnlineTrackingTask.CheckPrecision);
    }
}
