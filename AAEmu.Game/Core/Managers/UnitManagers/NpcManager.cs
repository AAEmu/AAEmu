using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers.UnitManagers
{
    public class NpcManager : Singleton<NpcManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, NpcTemplate> _templates;
        private Dictionary<uint, MerchantGoods> _goods;
        private Dictionary<uint, TotalCharacterCustom> _totalCharacterCustoms;
        private Dictionary<uint, Dictionary<uint, List<BodyPartTemplate>>> _itemBodyParts;
        private Dictionary<uint, List<uint>> _tccLookup;
        // you can provide a seed here if you want NPCs to more reliable retain their appearance between reboots, or leave out the seed to get it random every time
        private Random _loadCustomRandom = new Random(123456789);

        public bool Exist(uint templateId)
        {
            return _templates.ContainsKey(templateId);
        }

        public NpcTemplate GetTemplate(uint templateId)
        {
            if (_templates.ContainsKey(templateId))
                return _templates[templateId];
            return null;
        }

        public MerchantGoods GetGoods(uint id)
        {
            if (_goods.ContainsKey(id))
                return _goods[id];
            return null;
        }

        public Npc Create(uint objectId, uint id)
        {
            if (!_templates.ContainsKey(id))
                return null;

            var template = _templates[id];

            var npc = new Npc();
            npc.ObjId = objectId > 0 ? objectId : ObjectIdManager.Instance.GetNextId();
            npc.TemplateId = id;
            npc.Template = template;
            npc.ModelId = template.ModelId;
            npc.Faction = FactionManager.Instance.GetFaction(template.FactionId);
            npc.Level = template.Level;
            npc.Patrol = null;

            if (template.TotalCustomId == 0)
            {
                // load random hairstyles
                var templ = LoadCustom(template);
                template.HairId = templ.HairId;
                template.ModelParams = templ.ModelParams;
                template.BodyItems = templ.BodyItems;
            }

            SetEquipItemTemplate(npc, template.Items.Headgear, EquipmentItemSlot.Head);
            SetEquipItemTemplate(npc, template.Items.Necklace, EquipmentItemSlot.Neck);
            SetEquipItemTemplate(npc, template.Items.Shirt, EquipmentItemSlot.Chest);
            SetEquipItemTemplate(npc, template.Items.Belt, EquipmentItemSlot.Waist);
            SetEquipItemTemplate(npc, template.Items.Pants, EquipmentItemSlot.Legs);
            SetEquipItemTemplate(npc, template.Items.Gloves, EquipmentItemSlot.Hands);
            SetEquipItemTemplate(npc, template.Items.Shoes, EquipmentItemSlot.Feet);
            SetEquipItemTemplate(npc, template.Items.Bracelet, EquipmentItemSlot.Arms);
            SetEquipItemTemplate(npc, template.Items.Back, EquipmentItemSlot.Back);
            SetEquipItemTemplate(npc, template.Items.Undershirts, EquipmentItemSlot.Undershirt);
            SetEquipItemTemplate(npc, template.Items.Underpants, EquipmentItemSlot.Underpants);
            SetEquipItemTemplate(npc, template.Items.Mainhand, EquipmentItemSlot.Mainhand);
            SetEquipItemTemplate(npc, template.Items.Offhand, EquipmentItemSlot.Offhand);
            SetEquipItemTemplate(npc, template.Items.Ranged, EquipmentItemSlot.Ranged);
            SetEquipItemTemplate(npc, template.Items.Musical, EquipmentItemSlot.Musical);
            SetEquipItemTemplate(npc, template.Items.Cosplay, EquipmentItemSlot.Cosplay);

            for (var i = 0; i < 7; i++)
            {
                EquipmentItemSlot slot = (EquipmentItemSlot)(i + 19);
                if ((slot == EquipmentItemSlot.Hair) && (template.ModelParams != null))
                    SetEquipItemTemplate(npc, template.HairId, EquipmentItemSlot.Hair);
                else
                    SetEquipItemTemplate(npc, template.BodyItems[i].ItemId, slot, 0, template.BodyItems[i].NpcOnly);
            }

            foreach (var buffId in template.Buffs)
            {
                var buff = SkillManager.Instance.GetBuffTemplate(buffId);
                if (buff == null)
                {
                    _log.Warn("BuffId {0} for npc {1} not found", buffId, npc.TemplateId);
                    continue;
                }

                var obj = new SkillCasterUnit(npc.ObjId);
                buff.Apply(npc, obj, npc, null, null, new EffectSource(), null, DateTime.Now);
            }

            foreach (var bonusTemplate in template.Bonuses)
            {
                var bonus = new Bonus();
                bonus.Template = bonusTemplate;
                bonus.Value = bonusTemplate.Value; // TODO using LinearLevelBonus
                npc.AddBonus(0, bonus);
            }

            npc.Hp = npc.MaxHp;
            npc.Mp = npc.MaxMp;

            return npc;
        }

        private NpcTemplate LoadCustom(NpcTemplate template)
        {
            var _template = new NpcTemplate();
            var totalCustomId = (uint)template.TotalCustomId;

            if (totalCustomId != 0 || template.FactionId == 115 || template.FactionId == 116) // 115 - Monstrosity, 116 - Animal
            {
                return template;
            }

            //_log.Info("Loading random npc {0} custom templates...", template.ModelId);
            var modelParamsId = 0u;
            switch ((Race)template.CharRaceId)
            {
                case Race.None:
                case Race.Nuian: // Nuian male
                    modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)10 : (byte)11;
                    break;
                case Race.Dwarf: // Dwarf male
                                 //modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)14 : (byte)15;
                    break;
                case Race.Elf: // Elf male
                    modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)16 : (byte)17;
                    break;
                case Race.Hariharan: // Hariharan male
                    modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)18 : (byte)19;
                    break;
                case Race.Ferre: // Ferre male
                    modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)20 : (byte)21;
                    break;
                case Race.Warborn: // Warborn male
                                   //modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)24 : (byte)25;
                    break;
                case Race.Fairy:
                    break;
                case Race.Returned:
                    break;
                default:
                    break;
            }

            // choose randomly from the list totalCustomId
            if (modelParamsId != 0)
            {
                var li = _tccLookup[modelParamsId];
                var index = _loadCustomRandom.Next(_tccLookup[modelParamsId].Count);
                totalCustomId = li[index];
            }
            else
            {
                return template;
            }

            if (totalCustomId > 0)
            {
                var tc = _totalCharacterCustoms[totalCustomId];

                _template.HairId = tc.HairId;

                _template.ModelParams = new UnitCustomModelParams(UnitCustomModelType.Face);
                _template.ModelParams
                    .SetModelId(tc.ModelId)
                    .SetHairColorId(tc.HairColorId)
                    .SetSkinColorId(tc.SkinColorId);

                _template.ModelParams.Face.MovableDecalAssetId = tc.FaceMovableDecalAssetId;
                _template.ModelParams.Face.MovableDecalScale = tc.FaceMovableDecalScale;
                _template.ModelParams.Face.MovableDecalRotate = tc.FaceMovableDecalRotate;
                _template.ModelParams.Face.MovableDecalMoveX = tc.FaceMovableDecalMoveX;
                _template.ModelParams.Face.MovableDecalMoveY = tc.FaceMovableDecalMoveY;

                _template.ModelParams.Face.SetFixedDecalAsset(0, tc.FaceFixedDecalAsset0Id, tc.FaceFixedDecalAsset0Weight);
                _template.ModelParams.Face.SetFixedDecalAsset(1, tc.FaceFixedDecalAsset1Id, tc.FaceFixedDecalAsset1Weight);
                _template.ModelParams.Face.SetFixedDecalAsset(2, tc.FaceFixedDecalAsset2Id, tc.FaceFixedDecalAsset2Weight);
                _template.ModelParams.Face.SetFixedDecalAsset(3, tc.FaceFixedDecalAsset3Id, tc.FaceFixedDecalAsset3Weight);

                _template.ModelParams.Face.DiffuseMapId = tc.FaceDiffuseMapId;
                _template.ModelParams.Face.NormalMapId = tc.FaceNormalMapId;
                _template.ModelParams.Face.EyelashMapId = tc.FaceEyelashMapId;
                _template.ModelParams.Face.LipColor = tc.LipColor;
                _template.ModelParams.Face.LeftPupilColor = tc.LeftPupilColor;
                _template.ModelParams.Face.RightPupilColor = tc.RightPupilColor;
                _template.ModelParams.Face.EyebrowColor = tc.EyebrowColor;
                _template.ModelParams.Face.MovableDecalWeight = tc.FaceMovableDecalWeight;
                _template.ModelParams.Face.NormalMapWeight = tc.FaceNormalMapWeight;
                _template.ModelParams.Face.DecoColor = tc.DecoColor;
                _template.ModelParams.Face.Modifier = tc.Modifier;
            }
            else
            {
                _template.ModelParams = new UnitCustomModelParams(UnitCustomModelType.Skin);
            }

            foreach (var (modelId, ibp) in _itemBodyParts)
            {
                if (modelId != template.ModelId) { continue; }

                foreach (var (slotTypeId, bp) in ibp)
                {
                    var rbp = bp[bp.Count - 1];
                    if (modelId != template.ModelId) { continue; }

                    switch (slotTypeId)
                    {
                        case (byte)EquipmentItemSlotType.Face:
                            _template.BodyItems[rbp.SlotTypeId - 23] = (rbp.ItemId, rbp.NpcOnly);
                            break;
                        case (byte)EquipmentItemSlotType.Hair:
                            if (rbp.ItemId == template.HairId)
                            {
                                _template.BodyItems[rbp.SlotTypeId - 23] = (rbp.ItemId, rbp.NpcOnly);
                            }
                            else
                            {
                                if (template.HairId != 0)
                                {
                                    _template.BodyItems[rbp.SlotTypeId - 23] = (template.HairId, rbp.NpcOnly);
                                }
                            }

                            break;
                        case (byte)EquipmentItemSlotType.Beard:
                        case (byte)EquipmentItemSlotType.Body:
                        case (byte)EquipmentItemSlotType.Glasses:
                        case (byte)EquipmentItemSlotType.Tail:
                            _template.BodyItems[rbp.SlotTypeId - 23] = (rbp.ItemId, rbp.NpcOnly);
                            break;
                    }
                }
            }

            //_log.Info("Loaded npc {0} random hair {1} and hairColor {2}", template.ModelId, _template.HairId, _template.ModelParams.HairColorId);

            return _template;
        }

        public void Load()
        {
            _templates = new Dictionary<uint, NpcTemplate>();
            _goods = new Dictionary<uint, MerchantGoods>();
            _tccLookup = new Dictionary<uint, List<uint>>();
            _totalCharacterCustoms = new Dictionary<uint, TotalCharacterCustom>();
            _itemBodyParts = new Dictionary<uint, Dictionary<uint, List<BodyPartTemplate>>>();

            _log.Info("Loading npc templates...");
            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {

                    // Pre-Load customs
                    command.CommandText = "SELECT * FROM total_character_customs";
                    command.Prepare();
                    using (var sqliteDataReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                    {
                        while (reader.Read())
                        {
                            var custom = new TotalCharacterCustom();
                            custom.Id = reader.GetUInt32("id");
                            custom.ModelId = reader.GetUInt32("model_id");
                            custom.Name = reader.GetString("name");
                            custom.NpcOnly = reader.GetBoolean("npcOnly", true);
                            custom.HairId = reader.GetUInt32("hair_id");
                            custom.HairColorId = reader.GetUInt32("hair_color_id");
                            custom.SkinColorId = reader.GetUInt32("skin_color_id");
                            custom.FaceMovableDecalAssetId = reader.GetUInt32("face_movable_decal_asset_id");
                            custom.FaceMovableDecalScale = reader.GetFloat("face_movable_decal_scale");
                            custom.FaceMovableDecalRotate = reader.GetFloat("face_movable_decal_rotate");
                            custom.FaceMovableDecalMoveX = reader.GetInt16("face_movable_decal_move_x");
                            custom.FaceMovableDecalMoveY = reader.GetInt16("face_movable_decal_move_y");
                            custom.FaceFixedDecalAsset0Id = reader.GetUInt32("face_fixed_decal_asset_0_id");
                            custom.FaceFixedDecalAsset1Id = reader.GetUInt32("face_fixed_decal_asset_1_id");
                            custom.FaceFixedDecalAsset2Id = reader.GetUInt32("face_fixed_decal_asset_2_id");
                            custom.FaceFixedDecalAsset3Id = reader.GetUInt32("face_fixed_decal_asset_3_id");
                            custom.FaceDiffuseMapId = reader.GetUInt32("face_diffuse_map_id");
                            custom.FaceNormalMapId = reader.GetUInt32("face_normal_map_id");
                            custom.FaceEyelashMapId = reader.GetUInt32("face_eyelash_map_id");
                            custom.LipColor = reader.GetUInt32("lip_color");
                            custom.LeftPupilColor = reader.GetUInt32("left_pupil_color");
                            custom.RightPupilColor = reader.GetUInt32("right_pupil_color");
                            custom.EyebrowColor = reader.GetUInt32("eyebrow_color");
                            object blob = reader.GetValue("modifier");
                            if (blob != null)
                                custom.Modifier = (byte[])blob;
                            custom.OwnerTypeId = reader.GetUInt32("owner_type_id");
                            custom.FaceMovableDecalWeight = reader.GetFloat("face_movable_decal_weight");
                            custom.FaceFixedDecalAsset0Weight = reader.GetFloat("face_fixed_decal_asset_0_weight");
                            custom.FaceFixedDecalAsset1Weight = reader.GetFloat("face_fixed_decal_asset_1_weight");
                            custom.FaceFixedDecalAsset2Weight = reader.GetFloat("face_fixed_decal_asset_2_weight");
                            custom.FaceFixedDecalAsset3Weight = reader.GetFloat("face_fixed_decal_asset_3_weight");
                            custom.FaceNormalMapWeight = reader.GetFloat("face_normal_map_weight");
                            custom.DecoColor = reader.GetUInt32("deco_color");

                            _totalCharacterCustoms.Add(custom.Id, custom);
                        }
                    }

                    // Create a cached reference list by Model ID
                    foreach (var c in _totalCharacterCustoms)
                    {
                        if (!_tccLookup.ContainsKey(c.Value.ModelId))
                            _tccLookup.Add(c.Value.ModelId, new List<uint>());
                        _tccLookup[c.Value.ModelId].Add(c.Value.Id);
                    }

                    command.CommandText = "SELECT * FROM item_body_parts";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        // Pre-Load body parts
                        while (reader.Read())
                        {
                            var bp = new BodyPartTemplate();
                            var bodyParts = new List<BodyPartTemplate>();
                            var slotBodyParts = new Dictionary<uint, List<BodyPartTemplate>>();

                            bp.ItemId = reader.GetUInt32("item_id", 0);
                            bp.ModelId = reader.GetUInt32("model_id", 0);
                            bp.NpcOnly = reader.GetBoolean("npc_only", true);
                            bp.SlotTypeId = reader.GetUInt32("slot_type_id");
                            bodyParts.Add(bp);

                            if (!slotBodyParts.ContainsKey(bp.SlotTypeId))
                            {
                                slotBodyParts.Add(bp.SlotTypeId, bodyParts);
                            }
                            else
                            {
                                slotBodyParts[bp.SlotTypeId].Add(bp);
                            }

                            if (!_itemBodyParts.ContainsKey(bp.ModelId))
                            {
                                _itemBodyParts.Add(bp.ModelId, slotBodyParts);
                            }
                            else
                            {
                                if (!_itemBodyParts[bp.ModelId].ContainsKey(bp.SlotTypeId))
                                {
                                    _itemBodyParts[bp.ModelId].Add(bp.SlotTypeId, bodyParts);
                                }
                                else
                                {
                                    _itemBodyParts[bp.ModelId][bp.SlotTypeId].Add(bp);
                                }
                            }
                        }
                    }

                    command.CommandText = "SELECT * from npcs";
                    command.Prepare();
                    using (var sqliteDataReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                    {
                        while (reader.Read())
                        {
                            var template = new NpcTemplate();
                            template.Id = reader.GetUInt32("id");
                            template.Name = reader.GetString("name");
                            template.CharRaceId = reader.GetInt32("char_race_id");
                            template.NpcGradeId = (NpcGradeType)reader.GetByte("npc_grade_id");
                            template.NpcKindId = (NpcKindType)reader.GetByte("npc_kind_id");
                            template.Level = reader.GetByte("level");
                            template.NpcTemplateId = (NpcTemplateType)reader.GetByte("npc_template_id");
                            template.ModelId = reader.GetUInt32("model_id");
                            template.FactionId = reader.GetUInt32("faction_id");
                            template.SkillTrainer = reader.GetBoolean("skill_trainer", true);
                            template.AiFileId = reader.GetInt32("ai_file_id");
                            template.Merchant = reader.GetBoolean("merchant", true);
                            template.NpcNicknameId = reader.GetInt32("npc_nickname_id");
                            template.Auctioneer = reader.GetBoolean("auctioneer", true);
                            template.ShowNameTag = reader.GetBoolean("show_name_tag", true);
                            template.VisibleToCreatorOnly = reader.GetBoolean("visible_to_creator_only", true);
                            template.NoExp = reader.GetBoolean("no_exp", true);
                            template.PetItemId = reader.GetInt32("pet_item_id", 0);
                            template.BaseSkillId = reader.GetInt32("base_skill_id");
                            template.TrackFriendship = reader.GetBoolean("track_friendship", true);
                            template.Priest = reader.GetBoolean("priest", true);
                            template.NpcTedencyId = reader.GetInt32("npc_tendency_id", 0);
                            template.Blacksmith = reader.GetBoolean("blacksmith", true);
                            template.Teleporter = reader.GetBoolean("teleporter", true);
                            template.Opacity = reader.GetFloat("opacity");
                            template.AbilityChanger = reader.GetBoolean("ability_changer", true);
                            template.Scale = reader.GetFloat("scale");
                            template.SightRangeScale = reader.GetFloat("sight_range_scale");
                            template.SightFovScale = reader.GetFloat("sight_fov_scale");
                            template.MilestoneId = reader.GetInt32("milestone_id", 0);
                            template.AttackStartRangeScale = reader.GetFloat("attack_start_range_scale");
                            template.Aggression = reader.GetBoolean("aggression", true);
                            template.ExpMultiplier = reader.GetFloat("exp_multiplier");
                            template.ExpAdder = reader.GetInt32("exp_adder");
                            template.Stabler = reader.GetBoolean("stabler", true);
                            template.AcceptAggroLink = reader.GetBoolean("accept_aggro_link", true);
                            template.RecrutingBattlefieldId = reader.GetInt32("recruiting_battle_field_id");
                            template.ReturnDistance = reader.GetFloat("return_distance");
                            template.NpcAiParamId = reader.GetInt32("npc_ai_param_id");
                            template.NonPushableByActor = reader.GetBoolean("non_pushable_by_actor", true);
                            template.Banker = reader.GetBoolean("banker", true);
                            template.AggroLinkSpecialRuleId = reader.GetInt32("aggro_link_special_rule_id");
                            template.AggroLinkHelpDist = reader.GetFloat("aggro_link_help_dist");
                            template.AggroLinkSightCheck = reader.GetBoolean("aggro_link_sight_check", true);
                            template.Expedition = reader.GetBoolean("expedition", true);
                            template.HonorPoint = reader.GetInt32("honor_point");
                            template.Trader = reader.GetBoolean("trader", true);
                            template.AggroLinkSpecialGuard = reader.GetBoolean("aggro_link_special_guard", true);
                            template.AggroLinkSpecialIgnoreNpcAttacker =
                                reader.GetBoolean("aggro_link_special_ignore_npc_attacker", true);
                            template.AbsoluteReturnDistance = reader.GetFloat("absolute_return_distance");
                            template.Repairman = reader.GetBoolean("repairman", true);
                            template.ActivateAiAlways = reader.GetBoolean("activate_ai_always", true);
                            template.Specialty = reader.GetBoolean("specialty", true);
                            template.SpecialtyCoinId = reader.GetUInt32("specialty_coin_id", 0);
                            template.UseRangeMod = reader.GetBoolean("use_range_mod", true);
                            template.NpcPostureSetId = reader.GetInt32("npc_posture_set_id");
                            template.MateEquipSlotPackId = reader.GetInt32("mate_equip_slot_pack_id", 0);
                            template.MateKindId = reader.GetInt32("mate_kind_id", 0);
                            template.EngageCombatGiveQuestId = reader.GetInt32("engage_combat_give_quest_id", 0);
                            template.NoApplyTotalCustom = reader.GetBoolean("no_apply_total_custom", true);
                            template.BaseSkillStrafe = reader.GetBoolean("base_skill_strafe", true);
                            template.BaseSkillDelay = reader.GetFloat("base_skill_delay");
                            template.NpcInteractionSetId = reader.GetInt32("npc_interaction_set_id", 0);
                            template.UseAbuserList = reader.GetBoolean("use_abuser_list", true);
                            template.ReturnWhenEnterHousingArea =
                                reader.GetBoolean("return_when_enter_housing_area", true);
                            template.LookConverter = reader.GetBoolean("look_converter", true);
                            template.UseDDCMSMountSkill = reader.GetBoolean("use_ddcms_mount_skill", true);
                            template.CrowdEffect = reader.GetBoolean("crowd_effect", true);

                            var bodyPack = reader.GetInt32("equip_bodies_id", 0);
                            var clothPack = reader.GetInt32("equip_cloths_id", 0);
                            var weaponPack = reader.GetInt32("equip_weapons_id", 0);
                            template.TotalCustomId = reader.GetUInt32("total_custom_id", 0);
                            using (var command2 = connection.CreateCommand())
                            {
                                command2.CommandText = "SELECT char_race_id, char_gender_id FROM characters WHERE model_id = @model_id";
                                command2.Prepare();
                                command2.Parameters.AddWithValue("model_id", template.ModelId);
                                using (var sqliteReader2 = command2.ExecuteReader())
                                using (var reader2 = new SQLiteWrapperReader(sqliteReader2))
                                {
                                    if (reader2.Read())
                                    {
                                        template.Race = reader2.GetByte("char_race_id");
                                        template.Gender = reader2.GetByte("char_gender_id");
                                    }
                                }
                            }

                            _templates.Add(template.Id, template);

                            if (clothPack > 0)
                            {
                                using (var command2 = connection.CreateCommand())
                                {
                                    command2.CommandText = "SELECT * FROM equip_pack_cloths WHERE id=@id";
                                    command2.Prepare();
                                    command2.Parameters.AddWithValue("id", clothPack);
                                    using (var sqliteReader2 = command2.ExecuteReader())
                                    using (var reader2 = new SQLiteWrapperReader(sqliteReader2))
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
                                    using (var sqliteReader2 = command2.ExecuteReader())
                                    using (var reader2 = new SQLiteWrapperReader(sqliteReader2))
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

                            if ((template.TotalCustomId > 0) && _totalCharacterCustoms.TryGetValue(template.TotalCustomId, out var tc))
                            {
                                template.HairId = tc.HairId;

                                template.ModelParams = new UnitCustomModelParams(UnitCustomModelType.Face);
                                template.ModelParams
                                    .SetModelId(tc.ModelId)
                                    .SetHairColorId(tc.HairColorId)
                                    .SetSkinColorId(tc.SkinColorId);

                                template.ModelParams.Face.MovableDecalAssetId = tc.FaceMovableDecalAssetId;
                                template.ModelParams.Face.MovableDecalScale = tc.FaceMovableDecalScale;
                                template.ModelParams.Face.MovableDecalRotate = tc.FaceMovableDecalRotate;
                                template.ModelParams.Face.MovableDecalMoveX = tc.FaceMovableDecalMoveX;
                                template.ModelParams.Face.MovableDecalMoveY = tc.FaceMovableDecalMoveY;

                                template.ModelParams.Face.SetFixedDecalAsset(0, tc.FaceFixedDecalAsset0Id, tc.FaceFixedDecalAsset0Weight);
                                template.ModelParams.Face.SetFixedDecalAsset(1, tc.FaceFixedDecalAsset1Id, tc.FaceFixedDecalAsset1Weight);
                                template.ModelParams.Face.SetFixedDecalAsset(2, tc.FaceFixedDecalAsset2Id, tc.FaceFixedDecalAsset2Weight);
                                template.ModelParams.Face.SetFixedDecalAsset(3, tc.FaceFixedDecalAsset3Id, tc.FaceFixedDecalAsset3Weight);

                                template.ModelParams.Face.DiffuseMapId = tc.FaceDiffuseMapId;
                                template.ModelParams.Face.NormalMapId = tc.FaceNormalMapId;
                                template.ModelParams.Face.EyelashMapId = tc.FaceEyelashMapId;
                                template.ModelParams.Face.LipColor = tc.LipColor;
                                template.ModelParams.Face.LeftPupilColor = tc.LeftPupilColor;
                                template.ModelParams.Face.RightPupilColor = tc.RightPupilColor;
                                template.ModelParams.Face.EyebrowColor = tc.EyebrowColor;
                                template.ModelParams.Face.MovableDecalWeight = tc.FaceMovableDecalWeight;
                                template.ModelParams.Face.NormalMapWeight = tc.FaceNormalMapWeight;
                                template.ModelParams.Face.DecoColor = tc.DecoColor;
                                template.ModelParams.Face.Modifier = tc.Modifier;
                                // reader2.GetBytes("modifier", 0, template.ModelParams.Face.Modifier, 0, 128);
                            }
                            else
                            {
                                template.ModelParams = new UnitCustomModelParams(UnitCustomModelType.Skin);
                            }

                            if (template.NpcPostureSetId > 0)
                            {
                                using (var command2 = connection.CreateCommand())
                                {
                                    command2.CommandText = "SELECT * FROM npc_postures WHERE npc_posture_set_id=@id";
                                    command2.Prepare();
                                    command2.Parameters.AddWithValue("id", template.NpcPostureSetId);
                                    using (var sqliteReader2 = command2.ExecuteReader())
                                    using (var reader2 = new SQLiteWrapperReader(sqliteReader2))
                                    {
                                        if (reader2.Read())
                                            template.AnimActionId = reader2.GetUInt32("anim_action_id");
                                    }
                                }
                            }

                            foreach (var (modelId, ibp) in _itemBodyParts)
                            {
                                if (modelId != template.ModelId) { continue; }

                                foreach (var (slotTypeId, bp) in ibp)
                                {
                                    var rbp = bp[bp.Count - 1];
                                    if (modelId != template.ModelId) { continue; }

                                    switch (slotTypeId)
                                    {
                                        case (byte)EquipmentItemSlotType.Face:
                                            template.BodyItems[rbp.SlotTypeId - 23] = (rbp.ItemId, rbp.NpcOnly);
                                            break;
                                        case (byte)EquipmentItemSlotType.Hair:
                                            if (rbp.ItemId == template.HairId)
                                            {
                                                template.BodyItems[rbp.SlotTypeId - 23] = (rbp.ItemId, rbp.NpcOnly);
                                            }
                                            else
                                            {
                                                if (template.HairId != 0)
                                                {
                                                    template.BodyItems[rbp.SlotTypeId - 23] = (template.HairId, rbp.NpcOnly);
                                                }
                                            }

                                            break;
                                        case (byte)EquipmentItemSlotType.Beard:
                                        case (byte)EquipmentItemSlotType.Body:
                                        case (byte)EquipmentItemSlotType.Glasses:
                                        case (byte)EquipmentItemSlotType.Tail:
                                            template.BodyItems[rbp.SlotTypeId - 23] = (rbp.ItemId, rbp.NpcOnly);
                                            break;
                                    }
                                }
                            }
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM unit_modifiers WHERE owner_type='Npc'";
                    command.Prepare();
                    using (var sqliteDataReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                    {
                        while (reader.Read())
                        {
                            var npcId = reader.GetUInt32("owner_id");
                            if (!_templates.ContainsKey(npcId))
                                continue;
                            var npc = _templates[npcId];
                            var template = new BonusTemplate();
                            template.Attribute = (UnitAttribute)reader.GetByte("unit_attribute_id");
                            template.ModifierType = (UnitModifierType)reader.GetByte("unit_modifier_type_id");
                            template.Value = reader.GetInt32("value");
                            template.LinearLevelBonus = reader.GetInt32("linear_level_bonus");
                            npc.Bonuses.Add(template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM npc_initial_buffs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var id = reader.GetUInt32("npc_id");
                            var buffId = reader.GetUInt32("buff_id");
                            if (!_templates.ContainsKey(id))
                                continue;
                            var template = _templates[id];
                            template.Buffs.Add(buffId);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM merchants";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var id = reader.GetUInt32("npc_id");
                            if (!_templates.ContainsKey(id))
                                continue;
                            var template = _templates[id];
                            template.MerchantPackId = reader.GetUInt32("merchant_pack_id");
                        }
                    }
                }

                _log.Info("Loaded {0} npc templates", _templates.Count);
                _log.Info("Loading merchant packs...");
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM merchant_goods";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var id = reader.GetUInt32("merchant_pack_id");
                            if (!_goods.ContainsKey(id))
                                _goods.Add(id, new MerchantGoods(id));

                            var itemId = reader.GetUInt32("item_id");
                            var grade = reader.GetByte("grade_id");

                            _goods[id].AddItemToStock(itemId, grade);
                        }
                    }
                }

                _log.Info("Loaded {0} merchant packs", _goods.Count);
            }
        }

        private void SetEquipItemTemplate(Npc npc, uint templateId, EquipmentItemSlot slot, byte grade = 0, bool npcOnly = false)
        {
            if (npcOnly && npc.Equipment.GetItemBySlot((int)slot) != null)
                return;

            Item item = null;
            if (templateId > 0)
            {
                item = ItemManager.Instance.Create(templateId, 1, grade, false);
                item.SlotType = SlotType.Equipment;
                item.Slot = (int)slot;
            }

            // npc.Equip[(int)slot] = item;
            npc.Equipment.AddOrMoveExistingItem(0, item, (int)slot);
        }
    }
}
