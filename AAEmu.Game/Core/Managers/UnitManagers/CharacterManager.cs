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
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Char.Templates;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Characters;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers.UnitManagers;

public class CharacterManager(
    IWorldManager worldManager,
    IAccountManager accountManager,
    INameManager nameManager,
    ICharacterIdManager characterIdManager,
    IFactionManager factionManager,
    ISkillManager skillManager,
    IItemManager itemManager,
    IHousingManager housingManager,
    IFamilyManager familyManager,
    IMailManager mailManager,
    ITaskManager taskManager) : Singleton<CharacterManager>, ICharacterManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<byte, CharacterTemplate> _templates = [];
    private readonly HashSet<byte> _creatableTemplates = [];
    private readonly Dictionary<byte, AbilityItems> _abilityItems = [];
    private readonly Dictionary<int, List<Expand>> _expands = [];
    private readonly Dictionary<uint, AppellationTemplate> _appellations = [];
    private readonly Dictionary<uint, ActabilityTemplate> _actabilities = [];
    private readonly Dictionary<uint, ActabilityCategoriesTemplate> _actabilitiesCategories = [];
    private readonly Dictionary<int, ExpertLimit> _expertLimits = [];
    private readonly Dictionary<int, ExpandExpertLimit> _expandExpertLimits = [];
    private readonly object _characterDeletionLock = new();

    public CharacterTemplate GetTemplate(Race race, Gender gender)
    {
        return _templates[(byte)(16 * (byte)gender + (byte)race)];
    }

    /// <summary>
    /// Whether this race/gender may be used to create a new character, mirroring the
    /// <c>characters.creatable</c> column. Templates for non-creatable rows are still loaded so that
    /// existing characters keep resolving; only the creation path is gated.
    /// </summary>
    public bool IsCreatable(Race race, Gender gender)
    {
        return _creatableTemplates.Contains((byte)(16 * (byte)gender + (byte)race));
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

    public uint GetActabilityIdByCategoryId(uint id)
    {
        if (_actabilitiesCategories.TryGetValue(id, out var actabilityCategory))
        {
            return _actabilities.GetValueOrDefault(actabilityCategory.GroupId)?.Id ?? 0;
        }
        return 0;
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
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM characters";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new CharacterTemplate();
                        template.Race = (Race)reader.GetByte("char_race_id");
                        template.Gender = (Gender)reader.GetByte("char_gender_id");
                        template.ModelId = reader.GetUInt32("model_id");
                        template.FactionId = (FactionsEnum)reader.GetUInt32("faction_id");
                        template.ZoneId = reader.GetUInt32("starting_zone_id");
                        template.ReturnDistrictId = reader.GetUInt32("default_return_district_id");
                        template.ResurrectionDistrictId = reader.GetUInt32("default_resurrection_district_id");
                        using (var command2 = connection.CreateCommand())
                        {
                            command2.CommandText = "SELECT * FROM item_body_parts WHERE model_id=@model_id ORDER BY id";
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
                        if (reader.GetBoolean("creatable", true))
                            _creatableTemplates.Add(templateId);
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
                        var ability = reader.GetByte("ability_id");
                        var item = new AbilitySupplyItem
                        {
                            Id = reader.GetUInt32("item_id"),
                            Amount = reader.GetInt32("amount"),
                            Grade = reader.GetByte("grade_id")
                        };

                        if (!_abilityItems.ContainsKey(ability))
                            _abilityItems.Add(ability, new AbilityItems());
                        _abilityItems[ability].Supplies.Add(item);
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
                        // 10.0.2.13: ability_id was removed; the row id now identifies the ability slot.
                        var ability = reader.GetByte("id");
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
                                        template.Items.Headgear = reader2.GetUInt32("headgear_id");
                                        template.Items.HeadgearGrade = reader2.GetByte("headgear_grade_id");
                                        template.Items.Necklace = reader2.GetUInt32("necklace_id");
                                        template.Items.NecklaceGrade = reader2.GetByte("necklace_grade_id");
                                        template.Items.Shirt = reader2.GetUInt32("shirt_id");
                                        template.Items.ShirtGrade = reader2.GetByte("shirt_grade_id");
                                        template.Items.Belt = reader2.GetUInt32("belt_id");
                                        template.Items.BeltGrade = reader2.GetByte("belt_grade_id");
                                        template.Items.Pants = reader2.GetUInt32("pants_id");
                                        template.Items.PantsGrade = reader2.GetByte("pants_grade_id");
                                        template.Items.Gloves = reader2.GetUInt32("glove_id");
                                        template.Items.GlovesGrade = reader2.GetByte("glove_grade_id");
                                        template.Items.Shoes = reader2.GetUInt32("shoes_id");
                                        template.Items.ShoesGrade = reader2.GetByte("shoes_grade_id");
                                        template.Items.Bracelet = reader2.GetUInt32("bracelet_id");
                                        template.Items.BraceletGrade = reader2.GetByte("bracelet_grade_id");
                                        template.Items.Back = reader2.GetUInt32("back_id");
                                        template.Items.BackGrade = reader2.GetByte("back_grade_id");
                                        template.Items.Cosplay = reader2.GetUInt32("cosplay_id");
                                        template.Items.CosplayGrade = reader2.GetByte("cosplay_grade_id");
                                        template.Items.Undershirts = reader2.GetUInt32("undershirt_id");
                                        template.Items.UndershirtsGrade = reader2.GetByte("undershirt_grade_id");
                                        template.Items.Underpants = reader2.GetUInt32("underpants_id");
                                        template.Items.UnderpantsGrade = reader2.GetByte("underpants_grade_id");
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
                                        template.Items.Offhand = reader2.GetUInt32("offhand_id");
                                        template.Items.OffhandGrade = reader2.GetByte("offhand_grade_id");
                                        template.Items.Ranged = reader2.GetUInt32("ranged_id");
                                        template.Items.RangedGrade = reader2.GetByte("ranged_grade_id");
                                        template.Items.Musical = reader2.GetUInt32("musical_id");
                                        template.Items.MusicalGrade = reader2.GetByte("musical_grade_id");
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
                        var expand = new Expand
                        {
                            IsBank = reader.GetBoolean("is_bank", true), Step = reader.GetInt32("step"), Price = reader.GetInt32("price"),
                            ItemId = reader.GetUInt32("item_id", 0),
                            ItemCount = reader.GetInt32("item_count"),
                            CurrencyId = reader.GetInt32("currency_id")
                        };

                        if (!_expands.TryGetValue(expand.Step, out var value))
                            _expands.Add(expand.Step, [expand]);
                        else
                            value.Add(expand);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, buff_id FROM appellations";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new AppellationTemplate { Id = reader.GetUInt32("id"), BuffId = reader.GetUInt32("buff_id", 0) };

                        _appellations.Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT a.*, COALESCE(e.actability_view_group_id, 0) AS view_group_id " +
                    "FROM actability_groups a " +
                    "LEFT JOIN actability_view_group_elems e ON e.actability_group_id = a.id";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ActabilityTemplate
                        {
                            Id = reader.GetUInt32("id"),
                            Name = reader.GetString("name"),
                            UnitAttributeId = reader.GetInt32("unit_attr_id"),
                            ViewGroupId = reader.GetUInt32("view_group_id"),
                            CountsTowardExpertLimit = reader.GetBoolean("skill_page_visible") &&
                                                     reader.GetUInt32("view_group_id") != 0
                        };
                        _actabilities.Add(template.Id, template);
                    }
                }
            }

            // 10.0.2.13: the actability_categories table was removed. Its data is now split between
            // actability_view_groups (id, name, visible_order) and actability_view_group_elems
            // (id, actability_group_id, actability_view_group_id, visible_order). Reconstruct the old
            // template by reading each element and mapping actability_group_id -> GroupId, joining to the
            // view group for the display name. visible_ui has no equivalent, so default it to true.
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT e.id AS id, e.actability_group_id AS group_id, e.visible_order AS visible_order, g.name AS name " +
                    "FROM actability_view_group_elems e " +
                    "LEFT JOIN actability_view_groups g ON g.id = e.actability_view_group_id";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ActabilityCategoriesTemplate
                        {
                            Id = reader.GetUInt32("id"),
                            Name = reader.IsDBNull("name") ? string.Empty : reader.GetString("name"),
                            GroupId = reader.GetUInt32("group_id"),
                            VisibleUi = true,
                            VisibleOrder = reader.GetInt32("visible_order")
                        };
                        _actabilitiesCategories.TryAdd(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                // Actability.Step is the zero-based native expert-limit sequence. IDs are the sequence;
                // ordering by up_limit incorrectly inserts the language-only id 32 between ids 2 and 3.
                command.CommandText = "SELECT * FROM expert_limits ORDER BY id ASC";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    var step = 0;
                    while (reader.Read())
                    {
                        var template = new ExpertLimit
                        {
                            Id = reader.GetUInt32("id"),
                            UpLimit = reader.GetInt32("up_limit"),
                            ExpertLimitCount = reader.GetByte("expert_limit"),
                            Advantage = reader.GetInt32("advantage"),
                            CastAdvantage = reader.GetInt32("cast_adv"),
                            UpCurrencyId = reader.GetUInt32("up_currency_id", 0),
                            UpPrice = reader.GetInt32("up_price"),
                            DownCurrencyId = reader.GetUInt32("down_currency_id", 0),
                            DownPrice = reader.GetInt32("down_price"),
                            UseIntensified = reader.GetBoolean("use_intensified")
                        };
                        _expertLimits.Add(step++, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT expert_limit_id, actability_view_group_id, count " +
                    "FROM intensified_expert_limits";
                command.Prepare();
                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                {
                    var expertLimitId = reader.GetUInt32("expert_limit_id");
                    var template = _expertLimits.Values.FirstOrDefault(limit => limit.Id == expertLimitId);
                    if (template == null)
                        continue;

                    template.IntensifiedViewGroupLimits[reader.GetUInt32("actability_view_group_id")] =
                        reader.GetByte("count");
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
                        var template = new ExpandExpertLimit
                        {
                            Id = reader.GetUInt32("id"),
                            ExpandCount = reader.GetByte("expand_count"),
                            LifePoint = reader.GetInt32("life_point"),
                            ItemId = reader.GetUInt32("item_id", 0),
                            ItemCount = reader.GetInt32("item_count")
                        };
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
                point.ZoneId = worldManager.GetZoneId(worldManager.GetWorldTemplateByName("main_world"), charTemplate.Pos.X, charTemplate.Pos.Y);
                // Convert the json's degrees to rads
                point.Roll = point.Roll.DegToRad();
                point.Pitch = point.Pitch.DegToRad();
                point.Yaw = point.Yaw.DegToRad();

                // Males
                var template = _templates[(byte)(16 + charTemplate.Id)];
                template.SpawnPosition = point;
                template.SpawnPosition.WorldId = WorldManager.DefaultWorldTemplateId;
                template.NumInventorySlot = charTemplate.NumInventorySlot;
                template.NumBankSlot = charTemplate.NumBankSlot;

                // Females
                template = _templates[(byte)(32 + charTemplate.Id)];
                template.SpawnPosition = point;
                template.SpawnPosition.WorldId = WorldManager.DefaultWorldTemplateId;
                template.NumInventorySlot = charTemplate.NumInventorySlot;
                template.NumBankSlot = charTemplate.NumBankSlot;
            }
        }
        else
            throw new GameException($"CharacterManager: Error parsing {filePath} file");

        Logger.Info("Loaded {0} character templates", _templates.Count);
    }

    public void PlayerRoll(Character player, int max)
    {
        var roll = Random.Shared.Next(1, max);
        player.BroadcastPacket(new SCChatMessagePacket(ChatType.System, $"{player.Name} rolled {roll}."), true);
    }

    public int GetEffectiveAccessLevel(Character character)
    {
        var accountDetails = accountManager.GetAccountDetails(character.AccountId);
        return Math.Max(character.AccessLevel, accountDetails.AccessLevel);
    }

    public void Create(GameConnection connection, string name, Race race, Gender gender, uint[] bodyItems, UnitCustomModelParams customModel, AbilityType ability1, AbilityType ability2, AbilityType ability3, byte level)
    {
        // The client builds its race list from characters.creatable, so a request for a non-creatable
        // race/gender only arrives from a modified client or a crafted packet. Reject it before any
        // name or id is reserved.
        if (!IsCreatable(race, gender))
        {
            Logger.Error($"User tried to create a character with a non-creatable race/gender. Account {connection.AccountId}, Name {name}, Race {race}, Gender {gender}");
            connection.SendPacket(new SCCharacterCreationFailedPacket(CharacterCreateError.Failed));
            return;
        }

        name = name.NormalizeName();
        var nameValidationCode = nameManager.ValidateCharacterName(name);
        if (nameValidationCode != CharacterCreateError.Ok)
        {
            connection.SendPacket(new SCCharacterCreationFailedPacket(nameValidationCode));
            return;
        }

        // NOTE: This is purely a warning to log potential cheaters
        // If you have custom starting classes, make sure to comment or adjust this
        if (ability2 != AbilityType.None || ability3 != AbilityType.None)
        {
            Logger.Error($"User tried to make a new character that has 2nd and/or 3rd ability already set. Account {connection.AccountId}, Name {name}, Class {ability1}, {ability2}, {ability3}");
        }

        var accountDetails = accountManager.GetAccountDetails(connection.AccountId);

        // Get default access level for all users 
        var useAccessLevel = AppConfiguration.Instance.Account.AccessLevelDefault;

        // If it's the first character created, use first character access level settings 
        if (nameManager.NoNamesRegistered())
            useAccessLevel = Math.Max(AppConfiguration.Instance.Account.AccessLevelFirstCharacter, useAccessLevel);

        var characterId = characterIdManager.GetNextId();
        nameManager.AddCharacter(characterId, name, connection.AccountId);
        var template = GetTemplate(race, gender);

        var character = new Character(customModel)
        {
            Id = characterId, TemplateId = characterId, AccountId = connection.AccountId, Name = name,
            Race = race,
            Gender = gender
        };
        character.Transform.ApplyWorldSpawnPosition(template.SpawnPosition);
        if (WorldIntegration.ZoneAuthority
            && WorldIntegration.IsZoneLoaded != null
            && !WorldIntegration.IsZoneLoaded(character.Transform.ZoneId))
        {
            Logger.Warn(
                "Create '{0}' race={1} faction={2} zoneId={3} pos=({4:F0},{5:F0},{6:F0}) — no ZoneLoaded dedicate; NPCs/mobs will not appear until that zone is running",
                name, race, template.FactionId, character.Transform.ZoneId,
                character.Transform.World.Position.X, character.Transform.World.Position.Y,
                character.Transform.World.Position.Z);
        }
        character.Level = level;
        character.Faction = factionManager.GetFaction(template.FactionId);
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

        // A v10 skillset may ship no 1.2-style starter loadout — skip its gear/supplies if the entry is absent
        // (missing key is normal), but always apply the appearance body items below.
        _abilityItems.TryGetValue((byte)ability1, out var items);
        if (items?.Items != null)
        {
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
        }
        for (var i = 0; i < 7; i++)
        {
            if (bodyItems[i] == 0 && template.Items[i] > 0)
                bodyItems[i] = template.Items[i];
            SetEquipItemTemplate(character.Inventory, bodyItems[i], (EquipmentItemSlot)(i + 19), 0);
        }

        byte slot = 10;
        foreach (var item in items?.Supplies ?? [])
        {
            character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Invalid, item.Id, item.Amount, item.Grade);
            //var createdItem = itemManager.Create(item.Id, item.Amount, item.Grade);
            //character.Inventory.AddItem(Models.Game.Items.Actions.ItemTaskType.Invalid, createdItem);

            character.SetAction(slot, ActionSlotType.ItemType, item.Id);
            slot++;
        }

        _abilityItems.TryGetValue(0, out items);
        if (items != null)
            foreach (var item in items.Supplies)
            {
                character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Invalid, item.Id, item.Amount, item.Grade);
                //var createdItem = itemManager.Create(item.Id, item.Amount, item.Grade);
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
        character.SkillActiveTypes = new CharacterSkillActiveTypes(character);
        character.HeirSkills = new CharacterHeirSkills(character);
        foreach (var skill in skillManager.GetDefaultSkills())
        {
            if (!skill.AddToSlot)
                continue;
            character.SetAction(skill.Slot, ActionSlotType.Spell, skill.Template.Id);
        }

        slot = 1;
        while (character.Slots[slot].Type != ActionSlotType.None)
            slot++;
        foreach (var skill in skillManager.GetStartAbilitySkills(character.Ability1))
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
        character.FavoriteCrafts = new CharacterFavoriteCrafts(character);

        character.Hp = character.MaxHp;
        character.Mp = character.MaxMp;
        character.RestoreSavedHpMp();

        if (character.SaveDirectlyToDatabase())
        {
            connection.Characters.Add(character.Id, character);
            connection.SendPacket(new SCCreateCharacterResponsePacket(character));
        }
        else
        {
            // SaveDirectlyToDatabase has rolled back, but Inventory construction registered transient items and
            // containers globally. Discard those allocations before allowing this character ID to be reused.
            var cleanupSucceeded = false;
            try
            {
                itemManager.DiscardUnsavedCharacterState(characterId);
                cleanupSucceeded = true;
            }
            catch (Exception exception)
            {
                Logger.Fatal(exception,
                    "CreateCharacter - Failed to discard transient state for character id {0}; keeping its name and id reserved",
                    characterId);
            }

            if (cleanupSucceeded)
            {
                nameManager.RemoveCharacterId(characterId);
                characterIdManager.ReleaseId(characterId);
            }

            // There is no specific internal-save error packet; use the generic creation failure.
            connection.SendPacket(new SCCharacterCreationFailedPacket(CharacterCreateError.Failed));
        }
    }

    /// <summary>
    /// Cleans external relationships after a character is durably soft-deleted.
    /// </summary>
    /// <param name="character">Character to delete assets from</param>
    public void DeleteCharacterAssets(Character character)
    {
        // Demolish owned houses
        var myHouses = new Dictionary<uint, House>();
        if (housingManager.GetByCharacterId(myHouses, character.Id) > 0)
        {
            foreach (var (houseId, house) in myHouses)
            {
                house.Permission = HousingPermission.Public;
                // force expire the house
                // This should technically kill the house, and return the minimum amount of furniture
                house.ProtectionEndDate = DateTime.UtcNow.AddDays(-21);
                housingManager.UpdateTaxInfo(house);
            }
        }

        // Remove from Guild
        if (character.Expedition != null)
            ExpeditionManager.Leave(character);

        // Remove from Family
        if (character.Family > 0)
            familyManager.LeaveFamily(character);

        // TODO: Remove from player nation
        // TODO: Delete leadership

        // Return player mail addressed to this character so another player's attachments cannot be
        // consumed by a later full wipe. Delivery state is irrelevant here: visible inbox mail has
        // already been marked delivered, but the deleting receiver is still authorized to return it.
        List<BaseMail> receivedMails;
        lock (mailManager.AllPlayerMails)
        {
            receivedMails = mailManager.AllPlayerMails.Values
                .Where(mail => mail.Header.ReceiverId == character.Id)
                .ToList();
        }

        foreach (var mail in receivedMails)
        {
            if (mail.CanBeReturnedBy(character.Id) && !mail.ReturnToSenderFor(character.Id))
                Logger.Warn(
                    "DeleteCharacterAssets - Unable to return mail to sender for mail: {0}, deleted char: {1}({2}), sender: {3}({4})",
                    mail.Id,
                    mail.Header.ReceiverName, mail.Header.ReceiverId,
                    mail.Header.SenderName, mail.Header.SenderId);
        }

    }

    /// <summary>
    /// Mark characters marked for deletion as deleted after their time is finished
    /// </summary>
    /// <param name="character"></param>
    /// <param name="gameConnection"></param>
    /// <param name="dbConnection"></param>
    /// <returns>Returns true if a character was marked deleted, otherwise false</returns>
    public bool CheckForDeletedCharactersDeletion(Character character, GameConnection gameConnection, MySqlConnection dbConnection)
    {
        lock (_characterDeletionLock)
        {
            if (character.DeleteTime > DateTime.MinValue && character.DeleteTime <= DateTime.UtcNow)
            {
                Logger.Info("CheckForDeletedCharactersDeletion - Deleting Account:{0} Id:{1} Name:{2}", character.AccountId, character.Id, character.Name);
                using var command = dbConnection.CreateCommand();
                var originalName = character.Name;
                var deleteRequestTime = character.DeleteRequestTime;
                var deleteTime = character.DeleteTime;
                var deletedName = character.Name;
                if (AppConfiguration.Instance.Account.DeleteReleaseName)
                    deletedName = "!" + character.Name;

                command.Connection = dbConnection;
                command.CommandText =
                    "UPDATE `characters` SET `deleted`='1', `delete_time`=@new_delete_time, `name`=@deletedname " +
                    "WHERE `id`=@char_id AND `account_id`=@account_id AND `deleted`=0 " +
                    "AND `delete_time`=@expected_delete_time AND `delete_time`<=@delete_cutoff;";
                command.Parameters.AddWithValue("@new_delete_time", DateTime.MinValue);
                command.Parameters.AddWithValue("@char_id", character.Id);
                command.Parameters.AddWithValue("@account_id", character.AccountId);
                command.Parameters.AddWithValue("@deletedname", deletedName);
                command.Parameters.AddWithValue("@expected_delete_time", character.DeleteTime);
                command.Parameters.AddWithValue("@delete_cutoff", DateTime.UtcNow);

                var res = command.ExecuteNonQuery();
                if (res == 1)
                {
                    // Cache changes must follow the durable row update. Otherwise a failed or cancelled
                    // deletion releases the character name until the next restart.
                    if (AppConfiguration.Instance.Account.DeleteReleaseName)
                    {
                        nameManager.RemoveCharacterId(character.Id);
                        nameManager.AddCharacter(character.Id, deletedName, character.AccountId);
                    }

                    try
                    {
                        DeleteCharacterAssets(character);
                    }
                    catch (Exception exception)
                    {
                        // The character row is already durably deleted. Keep lobby/name caches coherent and
                        // leave the remaining cross-manager cleanup visible in the log for an operator retry.
                        Logger.Error(exception,
                            "CheckForDeletedCharactersDeletion - Asset cleanup failed for deleted charId:{0}",
                            character.Id);
                    }

                    character.Name = deletedName;
                    character.DeleteTime = DateTime.MinValue;
                    character.DeleteRequestTime = DateTime.MinValue;

                    // Send delete packet to the player if online
                    if (gameConnection != null)
                    {
                        gameConnection.Characters.Remove(character.Id);
                        gameConnection.SendPacket(new SCCharacterDeletedPacket(character.Id, originalName));
                        // Not sure if this is the way it should be sent or not, but it seems to work with status 1
                        gameConnection.SendPacket(new SCDeleteCharacterResponsePacket(character.Id, 1, deleteRequestTime, deleteTime));
                    }
                }
                return res == 1;
            }
            if (character.DeleteRequestTime > DateTime.MinValue)
            {
                Logger.Warn("CheckForDeletedCharactersDeletion - Delete request for Account:{0} Id:{1} Name:{2}, but character is no longer marked for deletion (possibly cancelled delete)", character.AccountId, character.Id, character.Name);
            }
            return false;
        }
    }

    public void CheckForDeletedCharacters()
    {
        lock (_characterDeletionLock)
            CheckForDeletedCharactersCore();
    }

    private void CheckForDeletedCharactersCore()
    {
        var nextCheckTime = DateTime.MaxValue;
        var deleteList = new List<(uint, uint)>(); // charId, accountId

        Logger.Debug("CheckForDeletedCharacters - Begin");
        using (var connection = MySQL.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT `id`, `account_id`, `delete_time` FROM characters " +
                    "WHERE `deleted`=0 AND `delete_time`>@minimum_delete_time";
                command.Parameters.AddWithValue("@minimum_delete_time", DateTime.MinValue);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // Check the delete time for this entry
                        var deleteTime = reader.GetDateTime("delete_time");
                        var charId = reader.GetUInt32("id");
                        var accountId = reader.GetUInt32("account_id");
                        if (deleteTime > DateTime.MinValue && deleteTime <= DateTime.UtcNow)
                        {
                            deleteList.Add((charId, accountId));
                        }
                        else
                        if (deleteTime > DateTime.MinValue && deleteTime < nextCheckTime)
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
            taskManager.Schedule(deleteCheckTask, nextCheckTime - DateTime.UtcNow);
            Logger.Debug("CheckForDeletedCharacters - Next delete scheduled at " + nextCheckTime.ToString());
        }
        else
        {
            Logger.Debug("CheckForDeletedCharacters - No new deletions scheduled");
        }
    }

    public void SetDeleteCharacter(GameConnection gameConnection, uint characterId)
    {
        lock (_characterDeletionLock)
        {
            if (gameConnection.Characters.TryGetValue(characterId, out var character))
            {
                // The client already refuses this ("Must deselect as Main Character before
                // deleting."), but that check lives entirely in its UI - nothing stopped a request
                // that skipped it from going through here.
                if (character.IsRepresent)
                {
                    Logger.Info($"SetDeleteCharacter: refusing to delete main character {characterId} on account {gameConnection.AccountId}");
                    gameConnection.SendPacket(new SCDeleteCharacterResponsePacket(characterId, 0));
                    return;
                }

                var deleteRequestTime = DateTime.UtcNow;
                var targetDeleteDelay = 0;

                // Get timings from settings
                foreach (var timing in AppConfiguration.Instance.Account.DeleteTimings)
                {
                    if (character.Level >= timing.Level)
                        targetDeleteDelay = timing.Delay;
                }

                var deleteTime = deleteRequestTime.AddMinutes(targetDeleteDelay);

                using (var connection = MySQL.CreateConnection())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "UPDATE characters SET `delete_request_time`=@delete_request_time, `delete_time`=@delete_time " +
                        "WHERE `id`=@id AND `account_id`=@account_id AND `deleted`=0";
                    command.Parameters.AddWithValue("@delete_request_time", deleteRequestTime);
                    command.Parameters.AddWithValue("@delete_time", deleteTime);
                    command.Parameters.AddWithValue("@id", character.Id);
                    command.Parameters.AddWithValue("@account_id", gameConnection.AccountId);
                    command.Prepare();
                    if (command.ExecuteNonQuery() == 1)
                    {
                        character.DeleteRequestTime = deleteRequestTime;
                        character.DeleteTime = deleteTime;
                        gameConnection.SendPacket(new SCDeleteCharacterResponsePacket(character.Id, 2, character.DeleteRequestTime, character.DeleteTime));
                    }
                    else
                    {
                        // Failed to mark for deletion
                        // Not the correct message, but it seems funny enough
                        gameConnection.SendPacket(new SCErrorMsgPacket(ErrorMessageType.CannotDeleteCharWhileBotSuspected, 0, true));
                    }
                }
            }
            else
            {
                gameConnection.SendPacket(new SCDeleteCharacterResponsePacket(characterId, 0));
            }
        }
        // Trigger our task queueing
        CheckForDeletedCharacters();
    }

    public void SetRestoreCharacter(GameConnection gameConnection, uint characterId)
    {
        lock (_characterDeletionLock)
        {
            if (gameConnection.Characters.TryGetValue(characterId, out var character))
            {
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "UPDATE characters SET `delete_request_time`=@delete_request_time, `delete_time`=@delete_time " +
                    "WHERE `id`=@id AND `account_id`=@account_id AND `deleted`=0 " +
                    "AND `delete_request_time`>@minimum_delete_time";
                command.Parameters.AddWithValue("@delete_request_time", DateTime.MinValue);
                command.Parameters.AddWithValue("@delete_time", DateTime.MinValue);
                command.Parameters.AddWithValue("@id", character.Id);
                command.Parameters.AddWithValue("@account_id", gameConnection.AccountId);
                command.Parameters.AddWithValue("@minimum_delete_time", DateTime.MinValue);
                command.Prepare();
                if (command.ExecuteNonQuery() == 1)
                {
                    character.DeleteRequestTime = DateTime.MinValue;
                    character.DeleteTime = DateTime.MinValue;
                    gameConnection.SendPacket(new SCCancelCharacterDeleteResponsePacket(character.Id, 3));
                }
                else
                    gameConnection.SendPacket(new SCCancelCharacterDeleteResponsePacket(characterId, 4));
            }
            else
            {
                gameConnection.SendPacket(new SCCancelCharacterDeleteResponsePacket(characterId, 4));
            }
        }
    }
    /// <summary>
    /// Records which character an account nominated as its main ("represent") character, or clears
    /// the nomination. At most one character per account holds it, so setting one clears the rest.
    /// </summary>
    /// <remarks>
    /// The client drives this from the character select screen and keeps its own copy - there is no
    /// field in the character list to send it back, and no server-to-client packet for it. We store it
    /// so the server knows what the player chose; the guard in <see cref="SetDeleteCharacter"/> is
    /// what that knowledge is for.
    /// </remarks>
    public void SetRepresentCharacter(GameConnection gameConnection, uint characterId, bool isDeleted)
    {
        lock (_characterDeletionLock)
        {
            if (!isDeleted && !gameConnection.Characters.ContainsKey(characterId))
            {
                Logger.Warn($"SetRepresentCharacter: character {characterId} is not on account {gameConnection.AccountId}");
                gameConnection.SendPacket(new SCRepreSentCharacterPacket(characterId, false, false, false));
                return;
            }

            using (var connection = MySQL.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                // Clear the whole account first, then set the one that was picked. Doing it in that
                // order keeps the "at most one" rule true even if a previous nomination is stale.
                command.CommandText = "UPDATE characters SET `represent`=0 WHERE `account_id`=@account_id";
                command.Parameters.AddWithValue("@account_id", gameConnection.AccountId);
                command.Prepare();
                command.ExecuteNonQuery();
            }

            foreach (var accountCharacter in gameConnection.Characters.Values)
                accountCharacter.IsRepresent = false;

            if (isDeleted)
            {
                Logger.Info($"SetRepresentCharacter: account {gameConnection.AccountId} cleared its main character");
                // Zero with success set is what clears the client's own copy: its handler stores
                // whatever id we send whenever success is true.
                gameConnection.SendPacket(new SCRepreSentCharacterPacket(0, true, false, true));
                return;
            }

            using (var connection = MySQL.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "UPDATE characters SET `represent`=1 WHERE `id`=@id AND `account_id`=@account_id AND `deleted`=0";
                command.Parameters.AddWithValue("@id", characterId);
                command.Parameters.AddWithValue("@account_id", gameConnection.AccountId);
                command.Prepare();
                if (command.ExecuteNonQuery() != 1)
                {
                    Logger.Warn($"SetRepresentCharacter: could not mark character {characterId} on account {gameConnection.AccountId}");
                    gameConnection.SendPacket(new SCRepreSentCharacterPacket(characterId, false, false, false));
                    return;
                }
            }

            if (gameConnection.Characters.TryGetValue(characterId, out var character))
                character.IsRepresent = true;

            Logger.Info($"SetRepresentCharacter: account {gameConnection.AccountId} nominated character {characterId}");
            gameConnection.SendPacket(new SCRepreSentCharacterPacket(characterId, true, false, false));
        }
    }

    public static List<LoginCharacterInfo> LoadCharacters(uint accountId)
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
                        if (deleteTime > DateTime.MinValue && deleteTime < DateTime.UtcNow)
                            continue;

                        var character = new LoginCharacterInfo
                        {
                            AccountId = accountId, Id = reader.GetUInt32("id"), Name = reader.GetString("name"), Race = reader.GetByte("race"),
                            Gender = reader.GetByte("gender")
                        };
                        result.Add(character);
                    }
                }
            }
        }
        return result;
    }

    private void SetEquipItemTemplate(Inventory inventory, uint templateId, EquipmentItemSlot slot, byte grade)
    {
        Item item = null;
        if (templateId > 0)
        {
            item = itemManager.Create(templateId, 1, grade);
            item.SlotType = SlotType.Equipment;
            item.Slot = (int)slot;
        }

        inventory.Equipment.AddOrMoveExistingItem(0, item, (int)slot);
        //inventory.Equip[(int) slot] = item;
    }

    public static void ApplyBeautySalon(Character character, uint hairModel, UnitCustomModelParams modelParams)
    {
        // TODO: Add support for future X-day Salon Certificate items

        if (character.Inventory.GetItemsCount(SlotType.Inventory, Item.SalonCertificate) <= 0)
            return;

        var oldHair = character.Equipment.GetItemBySlot((byte)EquipmentItemSlot.Hair);

        // Check if hair changed
        if (oldHair != null && oldHair.TemplateId != hairModel)
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

    public virtual bool IsCharacterPendingDeletion(string name)
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
        taskManager.Schedule(onlineTrackerTasks, TimeSpan.Zero, CharacterOnlineTrackingTask.CheckPrecision);
    }

    /// <summary>
    /// Adds crime points for offline characters
    /// </summary>
    /// <param name="playerId"></param>
    /// <param name="crimePointsToAdd"></param>
    public static void AddOfflineCrimePoints(uint playerId, short crimePointsToAdd)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE characters " +
            "SET `crime_point` = LEAST(GREATEST(`crime_point` + @crime_point, 0), @max_crime_point), " +
            "`crime_record` = LEAST(GREATEST(CAST(`crime_record` AS SIGNED) + @crime_point, 0), @max_crime_record) " +
            "WHERE `id` = @id";
        command.Parameters.AddWithValue("@crime_point", crimePointsToAdd);
        command.Parameters.AddWithValue("@max_crime_point", short.MaxValue);
        command.Parameters.AddWithValue("@max_crime_record", int.MaxValue);
        command.Parameters.AddWithValue("@id", playerId);
        command.Prepare();
        if (command.ExecuteNonQuery() != 1)
        {
            Logger.Warn($"Failed to update offline crime points for player {playerId}! (Add {crimePointsToAdd})");
        }
    }

}
