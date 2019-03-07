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

        // Grade Enchanting
        private Dictionary<uint, EquipSlotEnchantingCost> _enchantingCosts;
        private Dictionary<int, GradeTemplate> _gradesOrdered;
        private Dictionary<uint, ItemGradeEnchantingSupport> _enchantingSupports;

        public ItemTemplate GetTemplate(uint id)
        {
            return _templates.ContainsKey(id) ? _templates[id] : null;
        }

        public GradeTemplate GetGradeTemplate(int grade)
        {
            return _grades.ContainsKey(grade) ? _grades[grade] : null;
        }

        public Holdable GetHoldable(uint id)
        {
            return _holdables.ContainsKey(id) ? _holdables[id] : null;
        }

        public EquipSlotEnchantingCost GetEquipSlotEnchantingCost(uint slotTypeId)
        {
            return _enchantingCosts.ContainsKey(slotTypeId) ? _enchantingCosts[slotTypeId] : null;
        }

        public GradeTemplate GetGradeTemplateByOrder(int gradeOrder)
        {
            return _gradesOrdered.ContainsKey(gradeOrder) ? _gradesOrdered[gradeOrder] : null;
        }

        public ItemGradeEnchantingSupport GetItemGradEnchantingSupportByItemId(uint itemId)
        {
            return _enchantingSupports.ContainsKey(itemId) ? _enchantingSupports[itemId] : null;
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
                item = (Item)Activator.CreateInstance(template.ClassType, id, template, count);
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                _log.Error(ex.InnerException);
                item = new Item(id, template, count);
            }

            item.Grade = grade;
            if (item.Template.FixedGrade >= 0)
                item.Grade = (byte)item.Template.FixedGrade;
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
            _enchantingCosts = new Dictionary<uint, EquipSlotEnchantingCost>();
            _gradesOrdered = new Dictionary<int, GradeTemplate>();
            _enchantingSupports = new Dictionary<uint, ItemGradeEnchantingSupport>();
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
                            var template = new GradeTemplate();
                            template.Grade = reader.GetInt32("id");
                            template.GradeOrder = reader.GetInt32("grade_order");
                            template.HoldableDps = reader.GetFloat("var_holdable_dps");
                            template.HoldableArmor = reader.GetFloat("var_holdable_armor");
                            template.HoldableMagicDps = reader.GetFloat("var_holdable_magic_dps");
                            template.WearableArmor = reader.GetFloat("var_wearable_armor");
                            template.WearableMagicResistance = reader.GetFloat("var_wearable_magic_resistance");
                            template.Durability = reader.GetFloat("durability_value");
                            template.UpgradeRatio = reader.GetInt32("upgrade_ratio");
                            template.StatMultiplier = reader.GetInt32("stat_multiplier");
                            template.RefundMultiplier = reader.GetInt32("refund_multiplier");
                            template.EnchantSuccessRatio = reader.GetInt32("grade_enchant_success_ratio");
                            template.EnchantGreatSuccessRatio = reader.GetInt32("grade_enchant_great_success_ratio");
                            template.EnchantBreakRatio = reader.GetInt32("grade_enchant_break_ratio");
                            template.EnchantDowngradeRatio = reader.GetInt32("grade_enchant_downgrade_ratio");
                            template.EnchantCost = reader.GetInt32("grade_enchant_cost");
                            template.HoldableHealDps = reader.GetFloat("var_holdable_heal_dps");
                            template.EnchantDowngradeMin = reader.GetInt32("grade_enchant_downgrade_min");
                            template.EnchantDowngradeMax = reader.GetInt32("grade_enchant_downgrade_max");
                            template.CurrencyId = reader.GetInt32("currency_id");
                            _grades.Add(template.Grade, template);
                            _gradesOrdered.Add(template.GradeOrder, template);
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
                            var template = new SummonMateTemplate
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
                    command.CommandText = "SELECT * FROM item_summon_slaves";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new SummonSlaveTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                SlaveId = reader.GetUInt32("slave_id")
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
                    command.CommandText = "SELECT * FROM item_backpacks";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new BackpackTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                AssetId = reader.GetUInt32("asset_id"),
                                BackpackType = (BackpackType)reader.GetUInt32("backpack_type_id"),
                                DeclareSiegeZoneGroupId = reader.GetUInt32("declare_siege_zone_group_id"),
                                Heavy = reader.GetBoolean("heavy"),
                                Asset2Id = reader.GetUInt32("asset2_id"),
                                NormalSpeciality = reader.GetBoolean("normal_specialty"),
                                UseAsStat = reader.GetBoolean("use_as_stat"),
                                SkinKindId = reader.GetUInt32("skin_kind_id")
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

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM equip_slot_enchanting_costs";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new EquipSlotEnchantingCost();
                            template.Id = reader.GetUInt32("id");
                            template.SlotTypeId = reader.GetUInt32("slot_type_id");
                            template.Cost = reader.GetInt32("cost");
                            if (!_enchantingCosts.ContainsKey(template.SlotTypeId))
                                _enchantingCosts.Add(template.SlotTypeId, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_grade_enchanting_supports";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new ItemGradeEnchantingSupport();
                            template.Id = reader.GetUInt32("id");
                            template.ItemId = reader.GetUInt32("item_id");
                            template.RequireGradeMin = reader.GetInt32("require_grade_min");
                            template.RequireGradeMax = reader.GetInt32("require_grade_max");
                            template.AddSuccessRatio = reader.GetInt32("add_success_ratio");
                            template.AddSuccessMul = reader.GetInt32("add_success_mul");
                            template.AddGreatSuccessRatio = reader.GetInt32("add_great_success_ratio");
                            template.AddGreatSuccessMul = reader.GetInt32("add_great_success_mul");
                            template.AddBreakRatio = reader.GetInt32("add_break_ratio");
                            template.AddBreakMul = reader.GetInt32("add_break_mul");
                            template.AddDowngradeRatio = reader.GetInt32("add_downgrade_ratio");
                            template.AddDowngradeMul = reader.GetInt32("add_downgrade_mul");
                            template.AddGreatSuccessGrade = reader.GetInt32("add_great_success_grade");

                            if (!_enchantingSupports.ContainsKey(template.ItemId))
                                _enchantingSupports.Add(template.ItemId, template);
                        }
                    }
                }

                _log.Info("Loaded {0} items", _templates.Count);
            }
        }
    }
}
