using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class ItemManager : Singleton<ItemManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<int, GradeTemplate> _grades;
        private Dictionary<uint, Holdable> _holdables;
        private Dictionary<uint, Wearable> _wearables;
        private Dictionary<uint, WearableKind> _wearableKinds;
        private Dictionary<uint, WearableSlot> _wearableSlots;
        private Dictionary<uint, AttributeModifiers> _modifiers;
        private Dictionary<uint, ItemTemplate> _templates;
        private ItemConfig _config;

        public ItemTemplate GetTemplate(uint id)
        {
            return _templates.ContainsKey(id) ? _templates[id] : null;
        }

        public GradeTemplate GetGradeTemplate(int grade)
        {
            return _grades.ContainsKey(grade) ? _grades[grade] : null;
        }

        public float GetDurabilityRepairCostFactor()
        {
            return _config.DurabilityRepairCostFactor;
        }

        public float GetDurabilityConst()
        {
            return _config.DurabilityConst;
        }

        public float GetHoldableDurabilityConst()
        {
            return _config.HoldableDurabilityConst;
        }

        public float GetWearableDurabilityConst()
        {
            return _config.WearableDurabilityConst;
        }

        public float GetItemStatConst()
        {
            return _config.ItemStatConst;
        }

        public float GetHoldableStatConst()
        {
            return _config.HoldableStatConst;
        }

        public float GetWearableStatConst()
        {
            return _config.WearableStatConst;
        }

        public float GetStatValueConst()
        {
            return _config.StatValueConst;
        }

        public AttributeModifiers GetAttributeModifiers(uint id)
        {
            return _modifiers[id];
        }

        public Item Create(uint templateId, int count, byte grade, bool generateId = true)
        {
            var id = generateId ? ItemIdManager.Instance.GetNextId() : 0u;
            var template = GetTemplate(templateId);
            if (template == null)
                return null;

            Item item;
            try
            {
                item = (Item) Activator.CreateInstance(template.ClassType, id, template, count);
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                _log.Error(ex.InnerException);
                item = new Item(id, template, count);
            }

            item.Grade = grade;
            if (item.Template.FixedGrade >= 0)
                item.Grade = (byte) item.Template.FixedGrade;
            item.CreateTime = DateTime.UtcNow;
            return item;
        }

        public void Load()
        {
            _grades = new Dictionary<int, GradeTemplate>();
            _holdables = new Dictionary<uint, Holdable>();
            _wearables = new Dictionary<uint, Wearable>();
            _wearableKinds = new Dictionary<uint, WearableKind>();
            _wearableSlots = new Dictionary<uint, WearableSlot>();
            _modifiers = new Dictionary<uint, AttributeModifiers>();
            _templates = new Dictionary<uint, ItemTemplate>();
            _config = new ItemConfig();
            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_configs";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        if (!reader.Read())
                            return;
                        _config.DurabilityDecrementChance = reader.GetFloat("durability_decrement_chance");
                        _config.DurabilityRepairCostFactor = reader.GetFloat("durability_repair_cost_factor");
                        _config.DurabilityConst = reader.GetFloat("durability_const");
                        _config.HoldableDurabilityConst = reader.GetFloat("holdable_durability_const");
                        _config.WearableDurabilityConst = reader.GetFloat("wearable_durability_const");
                        _config.DeathDurabilityLossRatio = reader.GetInt32("death_durability_loss_ratio");
                        _config.ItemStatConst = reader.GetInt32("item_stat_const");
                        _config.HoldableStatConst = reader.GetInt32("holdable_stat_const");
                        _config.WearableStatConst = reader.GetInt32("wearable_stat_const");
                        _config.StatValueConst = reader.GetInt32("stat_value_const");
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_grades";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new GradeTemplate
                            {
                                Grade = reader.GetInt32("id"),
                                HoldableDps = reader.GetFloat("var_holdable_dps"),
                                HoldableArmor = reader.GetFloat("var_holdable_armor"),
                                HoldableMagicDps = reader.GetFloat("var_holdable_magic_dps"),
                                WearableArmor = reader.GetFloat("var_wearable_armor"),
                                WearableMagicResistance = reader.GetFloat("var_wearable_magic_resistance"),
                                Durability = reader.GetFloat("durability_value"),
                                UpgradeRatio = reader.GetInt32("upgrade_ratio"),
                                StatMultiplier = reader.GetInt32("stat_multiplier"),
                                RefundMultiplier = reader.GetInt32("refund_multiplier"),
                                EnchantSuccessRatio = reader.GetInt32("grade_enchant_success_ratio"),
                                EnchantGreatSuccessRatio = reader.GetInt32("grade_enchant_great_success_ratio"),
                                EnchantBreakRatio = reader.GetInt32("grade_enchant_break_ratio"),
                                NumSockets = reader.GetInt32("num_sockets")
                            };
                            _grades.Add(template.Grade, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM holdables";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new Holdable
                            {
                                Id = reader.GetUInt32("id"),
                                KindId = reader.GetUInt32("kind_id"),
                                Speed = reader.GetInt32("speed"),
                                ExtraDamagePierceFactor = reader.GetInt32("extra_damage_pierce_factor"),
                                ExtraDamageSlashFactor = reader.GetInt32("extra_damage_slash_factor"),
                                ExtraDamageBluntFactor = reader.GetInt32("extra_damage_blunt_factor"),
                                MaxRange = reader.GetInt32("max_range"),
                                Angle = reader.GetInt32("angle"),
                                EnchantedDps1000 = reader.GetInt32("enchanted_dps1000"),
                                SlotTypeId = reader.GetUInt32("slot_type_id"),
                                DamageScale = reader.GetInt32("damage_scale"),
                                FormulaDps = new Formula(reader.GetString("formula_dps")),
                                FormulaMDps = new Formula(reader.GetString("formula_mdps")),
                                FormulaArmor = new Formula(reader.GetString("formula_armor")),
                                MinRange = reader.GetInt32("min_range"),
                                SheathePriority = reader.GetInt32("sheathe_priority"),
                                DurabilityRatio = reader.GetFloat("durability_ratio"),
                                RenewCategory = reader.GetInt32("renew_category"),
                                ItemProcId = reader.GetInt32("item_proc_id"),
                                StatMultiplier = reader.GetInt32("stat_multiplier")
                            };

                            _holdables.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM wearables";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new Wearable
                            {
                                TypeId = reader.GetUInt32("armor_type_id"),
                                SlotTypeId = reader.GetUInt32("slot_type_id"),
                                ArmorBp = reader.GetInt32("armor_bp"),
                                MagicResistanceBp = reader.GetInt32("magic_resistance_bp")
                            };
                            _wearables.Add(template.TypeId * 128 + template.SlotTypeId, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM wearable_kinds";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new WearableKind
                            {
                                TypeId = reader.GetUInt32("armor_type_id"),
                                ArmorRatio = reader.GetInt32("armor_ratio"),
                                MagicResistanceRatio = reader.GetInt32("magic_resistance_ratio"),
                                FullBufId = reader.GetUInt32("full_buff_id"),
                                HalfBufId = reader.GetUInt32("half_buff_id"),
                                ExtraDamagePierce = reader.GetInt32("extra_damage_pierce"),
                                ExtraDamageSlash = reader.GetInt32("extra_damage_slash"),
                                ExtraDamageBlunt = reader.GetInt32("extra_damage_blunt"),
                                DurabilityRatio = reader.GetFloat("durability_ratio")
                            };
                            _wearableKinds.Add(template.TypeId, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM wearable_slots";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new WearableSlot
                            {
                                SlotTypeId = reader.GetUInt32("slot_type_id"),
                                Coverage = reader.GetInt32("coverage")
                            };
                            _wearableSlots.Add(template.SlotTypeId, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM equip_item_attr_modifiers";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new AttributeModifiers
                            {
                                Id = reader.GetUInt32("id"), // TODO ... alias
                                StrWeight = reader.GetInt32("str_weight"),
                                DexWeight = reader.GetInt32("dex_weight"),
                                StaWeight = reader.GetInt32("sta_weight"),
                                IntWeight = reader.GetInt32("int_weight"),
                                SpiWeight = reader.GetInt32("spi_weight")
                            };
                            _modifiers.Add(template.Id, template);
                        }
                    }
                }

                _log.Info("Loading items...");
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_armors";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var slotTypeId = reader.GetUInt32("slot_type_id");
                            var typeId = reader.GetUInt32("type_id");

                            var template = new ArmorTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                WearableTemplate = _wearables[typeId * 128 + slotTypeId],
                                KindTemplate = _wearableKinds[typeId],
                                SlotTemplate = _wearableSlots[slotTypeId],
                                BaseEnchantable = reader.GetBoolean("base_enchantable", true),
                                ModSetId = reader.GetUInt32("mod_set_id", 0),
                                Repairable = reader.GetBoolean("repairable", true),
                                DurabilityMultiplier = reader.GetInt32("durability_multiplier"),
                                BaseEquipment = reader.GetBoolean("base_equipment", true),
                                RechargeBuffId = reader.GetUInt32("recharge_buff_id", 0),
                                ChargeLifetime = reader.GetInt32("charge_lifetime"),
                                ChargeCount = reader.GetInt32("charge_count")
                            };
                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_weapons";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var holdableId = reader.GetUInt32("holdable_id");
                            var template = new WeaponTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                BaseEnchantable = reader.GetBoolean("base_enchantable"),
                                HoldableTemplate = _holdables[holdableId],
                                ModSetId = reader.GetUInt32("mod_set_id", 0),
                                Repairable = reader.GetBoolean("repairable", true),
                                DurabilityMultiplier = reader.GetInt32("durability_multiplier"),
                                BaseEquipment = reader.GetBoolean("base_equipment", true),
                                RechargeBuffId = reader.GetUInt32("recharge_buff_id", 0),
                                ChargeLifetime = reader.GetInt32("charge_lifetime"),
                                ChargeCount = reader.GetInt32("charge_count")
                            };
                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_accessories";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var slotTypeId = reader.GetUInt32("slot_type_id");
                            var typeId = reader.GetUInt32("type_id");

                            var template = new AccessoryTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                WearableTemplate = _wearables[typeId * 128 + slotTypeId],
                                KindTemplate = _wearableKinds[typeId],
                                SlotTemplate = _wearableSlots[slotTypeId],
                                ModSetId = reader.GetUInt32("mod_set_id", 0),
                                Repairable = reader.GetBoolean("repairable", true),
                                DurabilityMultiplier = reader.GetInt32("durability_multiplier"),
                                RechargeBuffId = reader.GetUInt32("recharge_buff_id", 0),
                                ChargeLifetime = reader.GetInt32("charge_lifetime"),
                                ChargeCount = reader.GetInt32("charge_count")
                            };
                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_summon_mates";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new SummonTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                NpcId = reader.GetUInt32("npc_id")
                            };
                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_body_parts";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            if (reader.IsDBNull("item_id"))
                                continue;
                            var template = new BodyPartTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                ModelId = reader.GetUInt32("model_id"),
                                NpcOnly = reader.GetBoolean("npc_only", true),
                                BeautyShopOnly = reader.GetBoolean("beautyshop_only", true)
                            };
                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_enchanting_gems";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new RuneTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                EquipSlotGroupId = reader.GetUInt32("equip_slot_group_id", 0),
                                EquipLevel = reader.GetByte("equip_level", 0),
                                ItemGradeId = reader.GetByte("item_grade_id", 0)
                            };
                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM items";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var id = reader.GetUInt32("id");
                            var template = _templates.ContainsKey(id) ? _templates[id] : new ItemTemplate();
                            template.Id = id;
                            template.Level = reader.GetInt32("level");
                            template.Price = reader.GetInt32("price");
                            template.Refund = reader.GetInt32("refund");
                            template.BindId = reader.GetUInt32("bind_id");
                            template.PickupLimit = reader.GetInt32("pickup_limit");
                            template.MaxCount = reader.GetInt32("max_stack_size");
                            template.Capacity = reader.GetInt32("capacity");
                            template.Sellable = reader.GetBoolean("sellable", true);
                            template.UseSkillId = reader.GetUInt32("use_skill_id");
                            template.BuffId = reader.GetUInt32("buff_id");
                            template.Gradable = reader.GetBoolean("gradable", true);
                            template.LootMulti = reader.GetBoolean("loot_multi", true);
                            template.LootQuestId = reader.GetUInt32("loot_quest_id");
                            template.HonorPrice = reader.GetInt32("honor_price");
                            template.ExpAbsLifetime = reader.GetInt32("exp_abs_lifetime");
                            template.ExpOnlineLifetime = reader.GetInt32("exp_online_lifetime");
                            template.ExpDate = reader.IsDBNull("exp_online_lifetime") ? reader.GetInt32("exp_date") : 0;
                            template.LevelRequirement = reader.GetInt32("level_requirement");
                            template.LevelLimit = reader.GetInt32("level_limit");
                            template.FixedGrade = reader.GetInt32("fixed_grade");
                            template.LivingPointPrice = reader.GetInt32("living_point_price");
                            template.CharGender = reader.GetByte("char_gender_id");
                            if (!_templates.ContainsKey(id))
                                _templates.Add(template.Id, template);
                        }
                    }
                }

                _log.Info("Loaded {0} items", _templates.Count);
            }
        }
    }
}