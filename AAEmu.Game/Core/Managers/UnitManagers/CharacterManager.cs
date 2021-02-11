using System;
using System.Collections.Generic;
using System.IO;
using AAEmu.Commons.IO;
using AAEmu.Commons.Models;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Char.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.DB;
using AAEmu.Game.Models.Game.Chat;
using NLog;
using AAEmu.Game.Models.Game.Items.Actions;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers.UnitManagers
{
    public class CharacterManager : Singleton<CharacterManager>
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<byte, CharacterTemplate> _templates;
        private readonly Dictionary<byte, AbilityItems> _abilityItems;
        private readonly Dictionary<int, List<Expand>> _expands;
        private readonly Dictionary<uint, AppellationTemplate> _appellations;
        private readonly Dictionary<uint, ActabilityTemplate> _actabilities;
        private readonly Dictionary<int, ExpertLimit> _expertLimits;
        private readonly Dictionary<int, ExpandExpertLimit> _expandExpertLimits;

        public CharacterManager()
        {
            _templates = new Dictionary<byte, CharacterTemplate>();
            _abilityItems = new Dictionary<byte, AbilityItems>();
            _expands = new Dictionary<int, List<Expand>>();
            _appellations = new Dictionary<uint, AppellationTemplate>();
            _actabilities = new Dictionary<uint, ActabilityTemplate>();
            _expertLimits = new Dictionary<int, ExpertLimit>();
            _expandExpertLimits = new Dictionary<int, ExpandExpertLimit>();
        }

        public CharacterTemplate GetTemplate(byte race, byte gender)
        {
            return _templates[(byte) (16 * gender + race)];
        }

        public AppellationTemplate GetAppellationsTemplate(uint id)
        {
            if (_appellations.ContainsKey(id))
                return _appellations[id];
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
            if (_expertLimits.ContainsKey(step))
                return _expertLimits[step];
            return null;
        }

        public ExpandExpertLimit GetExpandExpertLimit(int step)
        {
            if (_expandExpertLimits.ContainsKey(step))
                return _expandExpertLimits[step];
            return null;
        }

        public void CombatTick(TimeSpan delta)
        {
            //Not sure if we should put htis here or world
            foreach(var character in WorldManager.Instance.GetAllCharacters())
            {
                if (character.IsInCombat && character.LastCombatActivity.AddSeconds(30) < DateTime.Now)
                {
                    character.BroadcastPacket(new SCCombatClearedPacket(character.ObjId), true);
                    character.IsInCombat = false;
                }
                
                if (character.IsInPostCast && character.LastCast.AddSeconds(5) < DateTime.Now)
                {
                    character.IsInPostCast = false;
                }
            }
        }

        public void RegenTick(TimeSpan delta)
        {
            foreach (var character in WorldManager.Instance.GetAllCharacters())
            {
                if (character.IsDead || !character.NeedsRegen || character.IsDrowning)
                    continue;

                if (character.IsInCombat)
                    character.Hp += character.PersistentHpRegen;
                else
                    character.Hp += character.HpRegen;

                if (character.IsInPostCast)
                    character.Mp += character.PersistentMpRegen;
                else
                    character.Mp += character.MpRegen;

                character.Hp = Math.Min(character.Hp, character.MaxHp);
                character.Mp = Math.Min(character.Mp, character.MaxMp);
                character.BroadcastPacket(new SCUnitPointsPacket(character.ObjId, character.Hp, character.Mp), true);
            }
        }
        
        public void BreathTick(TimeSpan delta)
        {
            foreach (var character in WorldManager.Instance.GetAllCharacters())
            {
                if(character.IsDead || !character.IsUnderWater)
                    continue;
                
                character.DoChangeBreath();
            }
        }

        public void Load()
        {
            Log.Info("Loading character templates...");

            TickManager.Instance.OnTick.Subscribe(BreathTick, TimeSpan.FromMilliseconds(1000));
            TickManager.Instance.OnTick.Subscribe(CombatTick, TimeSpan.FromMilliseconds(1000));
            TickManager.Instance.OnTick.Subscribe(RegenTick, TimeSpan.FromMilliseconds(1000));
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
                            template.FactionId = reader.GetUInt32("faction_id");
                            template.ZoneId = reader.GetUInt32("starting_zone_id");
                            template.ReturnDictrictId = reader.GetUInt32("default_return_district_id");
                            template.ResurrectionDictrictId =
                                reader.GetUInt32("default_resurrection_district_id");
                            using (var command2 = connection.CreateCommand())
                            {
                                command2.CommandText = "SELECT * FROM item_body_parts WHERE model_id=@model_id";
                                command2.Prepare();
                                command2.Parameters.AddWithValue("model_id", template.ModelId);
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
                            var template = _templates[temp[characterId]];
                            template.Buffs.Add(buffId);
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
                            var ability = reader.GetByte("ability_id");
                            var template = new AbilityItems { Ability = ability, Items = new EquipItemsTemplate() };
                            var clothPack = reader.GetUInt32("newbie_cloth_pack_id", 0);
                            var weaponPack = reader.GetUInt32("newbie_weapon_pack_id", 0);
                            if (clothPack > 0)
                            {
                                using (var command2 = connection.CreateCommand())
                                {
                                    command2.CommandText = "SELECT * FROM equip_pack_cloths WHERE id=@id";
                                    command2.Prepare();
                                    command2.Parameters.AddWithValue("id", clothPack);
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
                                    command2.Prepare();
                                    command2.Parameters.AddWithValue("id", weaponPack);
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
                            var expand = new Expand();
                            expand.IsBank = reader.GetBoolean("is_bank", true);
                            expand.Step = reader.GetInt32("step");
                            expand.Price = reader.GetInt32("price");
                            expand.ItemId = reader.GetUInt32("item_id", 0);
                            expand.ItemCount = reader.GetInt32("item_count");
                            expand.CurrencyId = reader.GetInt32("currency_id");

                            if (!_expands.ContainsKey(expand.Step))
                                _expands.Add(expand.Step, new List<Expand> { expand });
                            else
                                _expands[expand.Step].Add(expand);
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
                            var template = new AppellationTemplate();
                            template.Id = reader.GetUInt32("id");
                            template.BuffId = reader.GetUInt32("buff_id", 0);

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
                            template.Name = reader.GetString("name");
                            template.UnitAttributeId = reader.GetInt32("unit_attr_id");
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
                            template.Id = reader.GetUInt32("id");
                            template.ExpandCount = reader.GetByte("expand_count");
                            template.LifePoint = reader.GetInt32("life_point");
                            template.ItemId = reader.GetUInt32("item_id", 0);
                            template.ItemCount = reader.GetInt32("item_count");
                            _expandExpertLimits.Add(step++, template);
                        }
                    }
                }
            }

            var content = FileManager.GetFileContents($"{FileManager.AppPath}Data/CharTemplates.json");
            if (string.IsNullOrWhiteSpace(content))
                throw new IOException(
                    $"File {FileManager.AppPath + "Data/CharTemplates.json"} doesn't exists or is empty.");

            if (JsonHelper.TryDeserializeObject(content, out List<CharacterTemplateConfig> charTemplates, out _))
            {
                foreach (var charTemplate in charTemplates)
                {
                    var point = new Point(charTemplate.Pos.X, charTemplate.Pos.Y, charTemplate.Pos.Z);
                    point.WorldId = charTemplate.Pos.WorldId;
                    point.ZoneId = WorldManager
                        .Instance
                        .GetZoneId(charTemplate.Pos.WorldId, charTemplate.Pos.X, charTemplate.Pos.Y); // TODO ...
                    
                    point.RotationX = charTemplate.Pos.RotationX;
                    point.RotationY = charTemplate.Pos.RotationY;
                    point.RotationZ = charTemplate.Pos.RotationZ;
                    
                    var template = _templates[(byte)(16 + charTemplate.Id)];
                    template.Position = point;
                    template.NumInventorySlot = charTemplate.NumInventorySlot;
                    template.NumBankSlot = charTemplate.NumBankSlot;

                    template = _templates[(byte)(32 + charTemplate.Id)];
                    template.Position = point;
                    template.NumInventorySlot = charTemplate.NumInventorySlot;
                    template.NumBankSlot = charTemplate.NumBankSlot;
                }
            }
            else
                throw new Exception($"CharacterManager: Parse {FileManager.AppPath + "Data/CharTemplates.json"} file");

            Log.Info("Loaded {0} character templates", _templates.Count);
        }

        public void PlayerRoll(Character Self, int max)
        {

            var roll = Rand.Next(1, max);
            Self.BroadcastPacket(new SCChatMessagePacket(ChatType.System, string.Format(Self.Name + " rolled " + roll.ToString() + ".")), true);
            
        }

        public void Create(GameConnection connection, string name, byte race, byte gender, uint[] body,
            UnitCustomModelParams customModel, byte ability1)
        {
            var nameValidationCode = NameManager.Instance.ValidationCharacterName(name);
            if (nameValidationCode == 0)
            {
                var characterId = CharacterIdManager.Instance.GetNextId();
                NameManager.Instance.AddCharacterName(characterId, name);
                var template = GetTemplate(race, gender);

                var character = new Character(customModel);
                character.Id = characterId;
                character.AccountId = connection.AccountId;
                character.Name = name.Substring(0, 1).ToUpper() + name.Substring(1);
                character.Race = (Race) race;
                character.Gender = (Gender) gender;
                character.Position = template.Position.Clone();
                character.Position.ZoneId = template.ZoneId;
                character.Level = 1;
                character.Faction = FactionManager.Instance.GetFaction(template.FactionId);
                character.FactionName = "";
                character.LaborPower = 50;
                character.LaborPowerModified = DateTime.UtcNow;
                character.NumInventorySlots = template.NumInventorySlot;
                character.NumBankSlots = template.NumBankSlot;
                character.Inventory = new Inventory(character);
                character.Updated = DateTime.UtcNow;
                character.Ability1 = (AbilityType) ability1;
                character.Ability2 = AbilityType.None;
                character.Ability3 = AbilityType.None;
                character.ReturnDictrictId = PortalManager.Instance.GetStarterSubZoneId(template.Race); // Use start zone id
                character.ResurrectionDictrictId = template.ResurrectionDictrictId;
                character.Slots = new ActionSlot[85];
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
                for (var i = 0; i < 7; i++)
                {
                    if (body[i] == 0 && template.Items[i] > 0)
                        body[i] = template.Items[i];
                    SetEquipItemTemplate(character.Inventory, body[i], (EquipmentItemSlot) (i + 19), 0);
                }

                byte slot = 10;
                foreach (var item in items.Supplies)
                {
                    character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Invalid, item.Id, item.Amount, item.Grade);
                    //var createdItem = ItemManager.Instance.Create(item.Id, item.Amount, item.Grade);
                    //character.Inventory.AddItem(Models.Game.Items.Actions.ItemTaskType.Invalid, createdItem);

                    character.SetAction(slot, ActionSlotType.Item, item.Id);
                    slot++;
                }

                items = _abilityItems[0];
                if (items != null)
                    foreach (var item in items.Supplies)
                    {
                        character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Invalid, item.Id, item.Amount, item.Grade);
                        //var createdItem = ItemManager.Instance.Create(item.Id, item.Amount, item.Grade);
                        //character.Inventory.AddItem(ItemTaskType.Invalid, createdItem);

                        character.SetAction(slot, ActionSlotType.Item, item.Id);
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
                    character.SetAction(skill.Slot, ActionSlotType.Skill, skill.Template.Id);
                }

                slot = 1;
                while (character.Slots[slot].Type != ActionSlotType.None)
                    slot++;
                foreach (var skill in SkillManager.Instance.GetStartAbilitySkills(character.Ability1))
                {
                    character.Skills.AddSkill(skill, 1, false);
                    character.SetAction(slot, ActionSlotType.Skill, skill.Id);
                    slot++;
                }
                
                character.Appellations = new CharacterAppellations(character);
                character.Quests = new CharacterQuests(character);
                character.Mails = new CharacterMails(character);
                character.Portals = new CharacterPortals(character);
                character.Friends = new CharacterFriends(character);
                
                character.Hp = character.MaxHp;
                character.Mp = character.MaxMp;

                if (character.SaveDirectlyToDatabase())
                {
                    connection.Characters.Add(character.Id, character);
                    connection.SendPacket(new SCCreateCharacterResponsePacket(character));
                }
                else
                {
                    connection.SendPacket(new SCCharacterCreationFailedPacket(3));
                    CharacterIdManager.Instance.ReleaseId(characterId);
                    NameManager.Instance.RemoveCharacterName(characterId);
                    // TODO release items...
                }
                
            }
            else
            {
                connection.SendPacket(new SCCharacterCreationFailedPacket(nameValidationCode));
            }
        }

        public List<LoginCharacterInfo> LoadCharacters(uint accountId)
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

        private void SetEquipItemTemplate(Inventory inventory, uint templateId, EquipmentItemSlot slot, byte grade)
        {
            Item item = null;
            if (templateId > 0)
            {
                item = ItemManager.Instance.Create(templateId, 1, grade);
                item.SlotType = SlotType.Equipment;
                item.Slot = (int)slot;
            }

            inventory.Equipment.AddOrMoveExistingItem(0, item, (int)slot);
            //inventory.Equip[(int) slot] = item;
        }
    }
}
