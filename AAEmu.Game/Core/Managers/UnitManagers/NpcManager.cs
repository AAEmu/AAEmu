using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.Creatures;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers.UnitManagers;

/// <summary>
/// Loads and caches NPC templates and related game data from the compact SQLite database.
/// </summary>
public class NpcManager(IObjectIdManager objectIdManager, IModelManager modelManager, IFactionManager factionManager, IItemManager itemManager, IAIManager aiManager) : Singleton<NpcManager>, INpcManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool Loaded { get; set; }

    /// <summary>
    /// This seed gets used to populate the random values for humanoid NPCs that don't have a look defined for them.
    /// You can provide a seed here if you want NPCs to more reliable retain their appearance between reboots, or leave out the seed to get it random every time
    /// </summary>
    private readonly Random _loadCustomRandom = new(123456789);
    /// <summary>
    /// NPC Templates
    /// </summary>
    private Dictionary<uint, NpcTemplate> Templates { get; } = [];
    /// <summary>
    /// List of goods a merchant sells
    /// </summary>
    private Dictionary<uint, MerchantGoods> Goods { get; } = [];
    private Dictionary<uint, List<Merchants>> MerchantGoods = []; // npcId, list <MerchantGoods>
    private Dictionary<uint, List<MerchantPacks>> MerchantPackGoods = []; // packId, list <MerchantPacks>
    /// <summary>
    /// Definitions for custom looks of humanoid NPCs
    /// </summary>
    private Dictionary<uint, TotalCharacterCustom> TotalCharacterCustoms { get; } = [];
    /// <summary>
    /// List of body parts for a given ModelId, BodyPartTypeId, list of BodyPartTemplates
    /// </summary>
    private Dictionary<uint, Dictionary<uint, List<BodyPartTemplate>>> ItemBodyParts { get; } = [];

    /// <summary>
    /// Cached list of TotalCharacterCustoms
    /// </summary>
    private Dictionary<uint, List<uint>> TccLookup { get; } = [];
    /// <summary>
    /// Equip Body Parts packs
    /// </summary>
    private Dictionary<uint, EquipBodyPartPack> EquipPackBodyParts { get; } = [];
    /// <summary>
    /// Contains a list of NPC (names) loaded from data/creatures.xml, Used for getting default names in NPC related GM commands
    /// </summary>
    private static Dictionary<uint, Creature> Creatures { get; set; } = [];

    /// <summary>
    /// Returns the default name of a NPC as defined in data/creatures.xml
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static string GetSpawnName(uint id)
    {
        return Creatures.TryGetValue(id, out var creature) ? creature.Title : string.Empty;
    }

    /// <summary>
    /// Checks if a given NPC Template exists
    /// </summary>
    /// <param name="templateId"></param>
    /// <returns></returns>
    public bool Exist(uint templateId)
    {
        return Templates.ContainsKey(templateId);
    }

    /// <summary>
    /// Returns the NpcTemplate for a given templateId
    /// </summary>
    /// <param name="templateId"></param>
    /// <returns></returns>
    public NpcTemplate GetTemplate(uint templateId)
    {
        return Templates.GetValueOrDefault(templateId);
    }

    /// <summary>
    /// Returns the dictionary of loaded Templates
    /// </summary>
    /// <returns></returns>
    public Dictionary<uint, NpcTemplate> GetAllTemplates()
    {
        return Templates;
    }

    /// <summary>
    /// Returns a definition of goods for a given Npc merchant
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public MerchantGoods GetGoods(uint id)
    {
        return Goods.GetValueOrDefault(id);
    }

    /// <summary>
    /// Creates a new NPC
    /// </summary>
    /// <param name="parentWorld">World Instance to add this NPC to</param>
    /// <param name="objectId">Optional ObjId ot use, generate a new one if zero</param>
    /// <param name="templateId">NPC Template to use for creation</param>
    /// <returns></returns>
    public Npc Create(WorldInstance parentWorld, uint objectId, uint templateId)
    {
        var template = GetTemplate(templateId);
        if (template == null)
        {
            return null;
        }

        var npc = new Npc
        {
            ParentWorld = parentWorld,
            ObjId = objectId > 0 ? objectId : objectIdManager.GetNextId(),
            TemplateId = templateId, // duplicate Id
            Id = templateId,
            Template = template,
            ModelId = template.ModelId,
            CanFly = modelManager.IsFlyOrSwim(template.ModelId),
            Faction = factionManager.GetFaction(template.FactionId),
            Level = template.Level,
            HeirLevel = (byte)template.HeirLevel,
            Patrol = null
        };

        if (template.TotalCustomId == 0)
        {
            // load random hairstyles
            var templ = LoadCustom(template);
            template.HairId = templ.HairId;
            template.ModelParams = templ.ModelParams;
            template.BodyItems = templ.BodyItems;
        }

        npc.ModelParams = template.ModelParams;

        // TODO: Check if we need to override some body parts if template.EquipBodiesId is set or not
        if (template.EquipBodiesId > 0 && EquipPackBodyParts.TryGetValue(template.EquipBodiesId, out _)) // var equipBodyPartPack))
        {
            /*
                if (equipBodyPartPack.HairColorId > 0)
                    template.ModelParams.SetHairColorId(equipBodyPartPack.HairColorId);
                if (equipBodyPartPack.FaceId > 0)
                    template.BodyItems[(uint)EquipmentItemSlotType.Face] = (equipBodyPartPack.FaceId, false);
                if (equipBodyPartPack.HairId > 0)
                    template.BodyItems[(uint)EquipmentItemSlotType.Hair] = (equipBodyPartPack.HairId, false);
                if (equipBodyPartPack.BeardId > 0)
                    template.BodyItems[(uint)EquipmentItemSlotType.Beard] = (equipBodyPartPack.BeardId, false);
                if (equipBodyPartPack.SkinColorId > 0)
                    template.ModelParams.SetSkinColorId(equipBodyPartPack.SkinColorId);
                // if (equipBodyPartPack.BodyDiffuseMapId > 0)
            */
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
            var slot = (EquipmentItemSlot)(i + 19);
            if (slot == EquipmentItemSlot.Hair && template.ModelParams != null)
                SetEquipItemTemplate(npc, template.HairId, EquipmentItemSlot.Hair);
            else
                SetEquipItemTemplate(npc, template.BodyItems[i].ItemId, slot, 0, template.BodyItems[i].NpcOnly);
        }

        npc.InitializeSpawnBuffs();
        npc.UpdateGearBonuses(null, null);

        npc.Hp = npc.MaxHp;
        npc.Mp = npc.MaxMp;

        if (npc.Template.AiFileId > 0)
        {
            var ai = AiUtils.GetAiByType((AiParamType)npc.Template.AiFileId, npc);
            if (ai == null)
                return npc;

            npc.Ai = ai;
            aiManager.AddAi(ai);
            npc.Ai.Start();
        }

        return npc;
    }

    /// <summary>
    /// Returns a new NpcTemplate with a random Npc look based on the TotalCharacterCustoms list. Only fields related to look will be populated.
    /// </summary>
    /// <param name="template"></param>
    /// <returns></returns>
    private NpcTemplate LoadCustom(NpcTemplate template)
    {
        var randomTemplate = new NpcTemplate();
        var totalCustomId = template.TotalCustomId;

        if (totalCustomId != 0 || template.FactionId == FactionsEnum.Monstrosity || template.FactionId == FactionsEnum.Animal) // 115 - Monstrosity, 116 - Animal
        {
            return template;
        }

        //Logger.Info("Loading random npc {0} custom templates...", template.ModelId);
        var modelParamsId = 0u;
        switch ((Race)template.CharRaceId)
        {
            case Race.None:
            case Race.Nuian: // Nuian male
                modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)10 : (byte)11;
                break;
            case Race.Dwarf: // Dwarf male
                // modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)14 : (byte)15;
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
                // modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)24 : (byte)25;
                break;
            case Race.Fairy:
                // Not implemented
                break;
            case Race.Returned:
                // Not implemented
                break;
            default:
                // Invalid
                return template;
        }

        var modelType = modelManager.GetModelType(template.ModelId);

        // choose randomly from the list totalCustomId
        if (modelParamsId != 0 && modelType is { SubType: "ActorModel" })
        {
            // Get all possible hair item_ids that match this model
            var hairsForThisModel = new List<uint>();
            foreach (var item in itemManager.GetAllItems())
            {
                if (item is BodyPartTemplate bpt && bpt.ModelId == template.ModelId && bpt.SlotTypeId == (uint)EquipmentItemSlotType.Hair)
                {
                    hairsForThisModel.Add(bpt.ItemId);
                }
            }

            if (hairsForThisModel.Count > 0)
            {
                // TODO: Slow, but I don't know of a better way to do this atm
                var possibleTotalCustoms = (from tc in TotalCharacterCustoms
                    where tc.Value.ModelId == modelParamsId && hairsForThisModel.Contains(tc.Value.HairId)
                    select tc.Value.Id).ToList();

                // If anything in result, pick something random from it
                if (possibleTotalCustoms.Count > 0)
                {
                    var r = _loadCustomRandom.Next(possibleTotalCustoms.Count);
                    totalCustomId = possibleTotalCustoms[r];
                }
                else
                {
                    Logger.Trace($"No compatible TotalCharacterCustoms hair found for NPC: {template.Id}");
                }
            }
        }
        else
        {
            return template;
        }

        if (totalCustomId > 0)
        {
            var tc = TotalCharacterCustoms[totalCustomId];

            randomTemplate.HairId = tc.HairId;

            randomTemplate.ModelParams = new UnitCustomModelParams(UnitCustomModelType.Face);
            randomTemplate.ModelParams
                .SetModelId(tc.ModelId)
                .SetHairColorId(tc.HairColorId)
                .SetSkinColorId(tc.SkinColorId);

            randomTemplate.ModelParams.Face.MovableDecalAssetId = tc.FaceMovableDecalAssetId;
            randomTemplate.ModelParams.Face.MovableDecalScale = tc.FaceMovableDecalScale;
            randomTemplate.ModelParams.Face.MovableDecalRotate = tc.FaceMovableDecalRotate;
            randomTemplate.ModelParams.Face.MovableDecalMoveX = tc.FaceMovableDecalMoveX;
            randomTemplate.ModelParams.Face.MovableDecalMoveY = tc.FaceMovableDecalMoveY;

            randomTemplate.ModelParams.Face.SetFixedDecalAsset(0, tc.FaceFixedDecalAsset0Id, tc.FaceFixedDecalAsset0Weight);
            randomTemplate.ModelParams.Face.SetFixedDecalAsset(1, tc.FaceFixedDecalAsset1Id, tc.FaceFixedDecalAsset1Weight);
            randomTemplate.ModelParams.Face.SetFixedDecalAsset(2, tc.FaceFixedDecalAsset2Id, tc.FaceFixedDecalAsset2Weight);
            randomTemplate.ModelParams.Face.SetFixedDecalAsset(3, tc.FaceFixedDecalAsset3Id, tc.FaceFixedDecalAsset3Weight);

            randomTemplate.ModelParams.Face.DiffuseMapId = tc.FaceDiffuseMapId;
            randomTemplate.ModelParams.Face.NormalMapId = tc.FaceNormalMapId;
            randomTemplate.ModelParams.Face.EyelashMapId = tc.FaceEyelashMapId;
            randomTemplate.ModelParams.Face.LipColor = tc.LipColor;
            randomTemplate.ModelParams.Face.LeftPupilColor = tc.LeftPupilColor;
            randomTemplate.ModelParams.Face.RightPupilColor = tc.RightPupilColor;
            randomTemplate.ModelParams.Face.EyebrowColor = tc.EyebrowColor;
            randomTemplate.ModelParams.Face.MovableDecalWeight = tc.FaceMovableDecalWeight;
            randomTemplate.ModelParams.Face.NormalMapWeight = tc.FaceNormalMapWeight;
            randomTemplate.ModelParams.Face.DecoColor = tc.DecoColor;
            randomTemplate.ModelParams.Face.Modifier = tc.Modifier;
        }
        else
        {
            randomTemplate.ModelParams = new UnitCustomModelParams(UnitCustomModelType.Skin);
        }

        foreach (var (modelId, ibp) in ItemBodyParts)
        {
            if (modelId != template.ModelId) { continue; }

            foreach (var (slotTypeId, bp) in ibp)
            {
                var rbp = bp[^1];
                if (modelId != template.ModelId) { continue; }

                switch (slotTypeId)
                {
                    case (byte)EquipmentItemSlotType.Face:
                        randomTemplate.BodyItems[rbp.SlotTypeId - 23] = (rbp.ItemId, rbp.NpcOnly);
                        break;
                    case (byte)EquipmentItemSlotType.Hair:
                        if (rbp.ItemId == template.HairId)
                        {
                            randomTemplate.BodyItems[rbp.SlotTypeId - 23] = (rbp.ItemId, rbp.NpcOnly);
                        }
                        else
                        {
                            if (template.HairId != 0)
                            {
                                randomTemplate.BodyItems[rbp.SlotTypeId - 23] = (template.HairId, rbp.NpcOnly);
                            }
                        }

                        break;
                    case (byte)EquipmentItemSlotType.Beard:
                    case (byte)EquipmentItemSlotType.Body:
                    case (byte)EquipmentItemSlotType.Glasses:
                    case (byte)EquipmentItemSlotType.Tail:
                        randomTemplate.BodyItems[rbp.SlotTypeId - 23] = (rbp.ItemId, rbp.NpcOnly);
                        break;
                }
            }
        }

        //Logger.Info("Loaded npc {0} random hair {1} and hairColor {2}", template.ModelId, _template.HairId, _template.ModelParams.HairColorId);

        return randomTemplate;
    }

    
    public void Load()
    {
        if (Loaded)
            return;

        Templates.Clear();
        Goods.Clear();
        TccLookup.Clear();
        TotalCharacterCustoms.Clear();
        ItemBodyParts.Clear();
        EquipPackBodyParts.Clear();
        Creatures = Creature.GetAllCreatures();

        Logger.Info("Loading npc templates...");
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
                        var custom = new TotalCharacterCustom
                        {
                            Id = reader.GetUInt32("id"), ModelId = reader.GetUInt32("model_id"), Name = reader.GetString("name"),
                            NpcOnly = reader.GetBoolean("npcOnly", true),
                            HairId = reader.GetUInt32("hair_id"),
                            HairColorId = reader.GetUInt32("hair_color_id"),
                            SkinColorId = reader.GetUInt32("skin_color_id"),
                            FaceMovableDecalAssetId = reader.GetUInt32("face_movable_decal_asset_id"),
                            FaceMovableDecalScale = reader.GetFloat("face_movable_decal_scale"),
                            FaceMovableDecalRotate = reader.GetFloat("face_movable_decal_rotate"),
                            FaceMovableDecalMoveX = reader.GetInt16("face_movable_decal_move_x"),
                            FaceMovableDecalMoveY = reader.GetInt16("face_movable_decal_move_y"),
                            FaceFixedDecalAsset0Id = reader.GetUInt32("face_fixed_decal_asset_0_id"),
                            FaceFixedDecalAsset1Id = reader.GetUInt32("face_fixed_decal_asset_1_id"),
                            FaceFixedDecalAsset2Id = reader.GetUInt32("face_fixed_decal_asset_2_id"),
                            FaceFixedDecalAsset3Id = reader.GetUInt32("face_fixed_decal_asset_3_id"),
                            FaceFixedDecalAsset4Id = reader.GetUInt32("face_fixed_decal_asset_4_id"),
                            FaceFixedDecalAsset5Id = reader.GetUInt32("face_fixed_decal_asset_5_id"),
                            FaceDiffuseMapId = reader.GetUInt32("face_diffuse_map_id"),
                            FaceNormalMapId = reader.GetUInt32("face_normal_map_id"),
                            FaceEyelashMapId = reader.GetUInt32("face_eyelash_map_id"),
                            LipColor = reader.GetUInt32("lip_color"),
                            LeftPupilColor = reader.GetUInt32("left_pupil_color"),
                            RightPupilColor = reader.GetUInt32("right_pupil_color"),
                            EyebrowColor = reader.GetUInt32("eyebrow_color"),
                            BodyNormalMapId = reader.GetUInt32("body_normal_map_id"),
                            BodyNormalMapWeight = reader.GetFloat("body_normal_map_weight"),
                            DefaultHairColor = reader.GetUInt32("default_hair_color"),
                            DisplayOrder = reader.GetInt32("display_order"),
                            FaceFixedDecalAsset4Weight = reader.GetFloat("face_fixed_decal_asset_4_weight"),
                            FaceFixedDecalAsset5Weight = reader.GetFloat("face_fixed_decal_asset_5_weight"),
                            HornColorId = reader.GetUInt32("horn_color_id"),
                            HornId = reader.GetUInt32("horn_id"),
                            IconPath = reader.GetString("icon_path"),
                            TwoToneHairColor = reader.GetUInt32("two_tone_hair_color"),
                            TwoToneFirstWidth = reader.GetFloat("two_tone_first_width"),
                            TwoToneSecondWidth = reader.GetFloat("two_tone_second_width")
                        };
                        var blob = reader.GetValue("modifier");
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

                        TotalCharacterCustoms.Add(custom.Id, custom);
                    }
                }

                // Create a cached reference list by Model ID
                foreach (var c in TotalCharacterCustoms)
                {
                    if (!TccLookup.ContainsKey(c.Value.ModelId))
                        TccLookup.Add(c.Value.ModelId, []);
                    TccLookup[c.Value.ModelId].Add(c.Value.Id);
                }

                // Pre-Load body parts
                command.CommandText = "SELECT * FROM item_body_parts";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var bp = new BodyPartTemplate();
                        var bodyParts = new List<BodyPartTemplate>();
                        var slotBodyParts = new Dictionary<uint, List<BodyPartTemplate>>();

                        bp.Id = reader.GetUInt32("item_id", 0);
                        bp.ItemId = reader.GetUInt32("item_id", 0);
                        bp.ModelId = reader.GetUInt32("model_id", 0);
                        bp.NpcOnly = reader.GetBoolean("npc_only", true);
                        bp.SlotTypeId = reader.GetUInt32("slot_type_id");
                        bp.AssetId = reader.GetUInt32("asset_id");
                        bp.Asset1Id = reader.GetUInt32("asset_1_id");
                        bp.Asset2Id = reader.GetUInt32("asset_2_id");
                        bp.Asset3Id = reader.GetUInt32("asset_3_id");
                        bp.Asset4Id = reader.GetUInt32("asset_4_id");
                        bp.CustomTextureId = reader.GetUInt32("custom_texture_id");
                        bp.CustomTexture1Id = reader.GetUInt32("custom_texture_1_id");
                        bp.CustomTexture2Id = reader.GetUInt32("custom_texture_2_id");
                        bp.CustomTexture3Id = reader.GetUInt32("custom_texture_3_id");
                        bp.CustomTexture4Id = reader.GetUInt32("custom_texture_4_id");
                        bp.FaceMask = reader.GetString("face_mask");
                        bp.HairBase = reader.GetString("hair_base");
                        bp.LeftEyeHeight = reader.GetInt32("left_eye_height");
                        bp.LeftEyeWidth = reader.GetInt32("left_eye_width");
                        bp.LeftEyeX = reader.GetInt32("left_eye_x");
                        bp.LeftEyeY = reader.GetInt32("left_eye_y");
                        bp.RightEyeHeight = reader.GetInt32("right_eye_height");
                        bp.RightEyeWidth = reader.GetInt32("right_eye_width");
                        bp.RightEyeX = reader.GetInt32("right_eye_x");
                        bp.RightEyeY = reader.GetInt32("right_eye_y");
                        bp.OddEye = reader.GetBoolean("odd_eye", true);
                        bodyParts.Add(bp);

                        if (!slotBodyParts.TryGetValue(bp.SlotTypeId, out var slotBodyTemplates))
                        {
                            slotBodyParts.Add(bp.SlotTypeId, bodyParts);
                        }
                        else
                        {
                            slotBodyTemplates.Add(bp);
                        }

                        if (!ItemBodyParts.TryAdd(bp.ModelId, slotBodyParts))
                        {
                            if (!ItemBodyParts[bp.ModelId].TryGetValue(bp.SlotTypeId, out var itemBodyTemplate))
                            {
                                ItemBodyParts[bp.ModelId].Add(bp.SlotTypeId, bodyParts);
                            }
                            else
                            {
                                itemBodyTemplate.Add(bp);
                            }
                        }
                    }
                }

                // Load the actual Npc list
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
                        template.Level = (byte)Math.Clamp(reader.GetInt32("level"), byte.MinValue, byte.MaxValue);
                        template.NpcTemplateId = (NpcTemplateType)reader.GetByte("npc_template_id");
                        template.ModelId = reader.GetUInt32("model_id");
                        template.FactionId = (FactionsEnum)reader.GetUInt32("faction_id");
                        template.HeirLevel = reader.GetUInt32("heir_level");
                        template.SkillTrainer = reader.GetBoolean("skill_trainer", true);
                        template.AiFileId = reader.GetInt32("ai_file_id");
                        template.Merchant = reader.GetBoolean("merchant", true);
                        template.NpcNicknameId = reader.GetInt32("npc_nickname_id");
                        template.Auctioneer = reader.GetBoolean("auctioneer", true);
                        template.ShowNameTag = reader.GetBoolean("show_name_tag", true);
                        template.VisibleToCreatorOnly = reader.GetBoolean("visible_to_creator_only", true);
                        template.NoExp = reader.GetBoolean("no_exp", true);
                        template.PetItemId = reader.GetUInt32("pet_item_id", 0);
                        template.BaseSkillId = reader.GetInt32("base_skill_id");
                        template.TrackFriendship = reader.GetBoolean("track_friendship", true);
                        template.Priest = reader.GetBoolean("priest", true);
                        //template.NpcTedencyId = reader.GetInt32("npc_tendency_id", 0); // there is no such field in the database for version 3.0.3.0
                        template.Blacksmith = reader.GetBoolean("blacksmith", true);
                        template.Teleporter = reader.GetBoolean("teleporter", true);
                        template.Opacity = reader.GetFloat("opacity");
                        template.AbilityChanger = reader.GetBoolean("ability_changer", true);
                        template.Scale = reader.GetFloat("scale");
                        template.SightRangeScale = reader.GetFloat("sight_range_scale");
                        template.SightFovScale = reader.GetFloat("sight_fov_scale");
                        //template.MilestoneId = reader.GetInt32("milestone_id", 0); // there is no such field in the database for version 3.0.3.0
                        template.AttackStartRangeScale = reader.GetFloat("attack_start_range_scale");
                        template.Aggression = reader.GetBoolean("aggression", true);
                        template.ExpMultiplier = reader.GetFloat("exp_multiplier");
                        template.ExpAdder = reader.GetInt32("exp_adder");
                        template.Stabler = reader.GetBoolean("stabler", true);
                        template.AcceptAggroLink = reader.GetBoolean("accept_aggro_link", true);
                        //template.RecrutingBattlefieldId = reader.GetInt32("recruiting_battle_field_id"); // there is no such field in the database for version 3.0.3.0
                        template.ReturnDistance = reader.GetFloat("return_distance");
                        template.NpcAiParamId = reader.GetInt32("npc_ai_param_id");
                        template.NonPushableByActor = reader.GetBoolean("non_pushable_by_actor", true);
                        template.Banker = reader.GetBoolean("banker", true);
                        template.AggroLinkSpecialRuleId = (AggroLinkSpecialRuleKind)reader.GetInt32("aggro_link_special_rule_id");
                        template.AggroLinkHelpDist = reader.GetFloat("aggro_link_help_dist");
                        template.AggroLinkSightCheck = reader.GetBoolean("aggro_link_sight_check", true);
                        template.Expedition = reader.GetBoolean("expedition", true);
                        template.HonorPoint = reader.GetInt32("honor_point");
                        template.Trader = reader.GetBoolean("trader", true);
                        template.AggroLinkSpecialGuard = reader.GetBoolean("aggro_link_special_guard", true);
                        template.AggroLinkSpecialIgnoreNpcAttacker = reader.GetBoolean("aggro_link_special_ignore_npc_attacker", true);
                        template.AbsoluteReturnDistance = reader.GetFloat("absolute_return_distance");
                        template.Repairman = reader.GetBoolean("repairman", true);
                        template.ActivateAiAlways = reader.GetBoolean("activate_ai_always", true);
                        template.Specialty = reader.GetBoolean("specialty", true);
                        template.SpecialtyCoinId = reader.GetUInt32("specialty_coin_id", 0);
                        template.UseRangeMod = reader.GetBoolean("use_range_mod", true);
                        template.NpcPostureSetId = reader.GetInt32("npc_posture_set_id");
                        template.MateEquipSlotPackId = reader.GetInt32("mate_equip_slot_pack_id", 0);
                        template.MateKindId = reader.GetInt32("mate_kind_id", 0);
                        template.EngageCombatGiveQuestId = reader.GetUInt32("engage_combat_give_quest_id", 0);
                        template.NoApplyTotalCustom = reader.GetBoolean("no_apply_total_custom", true);
                        template.BaseSkillStrafe = reader.GetBoolean("base_skill_strafe", true);
                        template.BaseSkillDelay = reader.GetFloat("base_skill_delay");
                        template.NpcInteractionSetId = reader.GetInt32("npc_interaction_set_id", 0);
                        template.UseAbuserList = reader.GetBoolean("use_abuser_list", true);
                        template.ReturnWhenEnterHousingArea = reader.GetBoolean("return_when_enter_housing_area", true);
                        template.LookConverter = reader.GetBoolean("look_converter", true);
                        template.UseDDCMSMountSkill = reader.GetBoolean("use_ddcms_mount_skill", true);
                        template.CrowdEffect = reader.GetBoolean("crowd_effect", true);
                        //template.EquipBodiesId = reader.GetUInt32("equip_bodies_id", 0); // there is no such field in the database for version 3.0.3.0
                        template.EquipClothsId = reader.GetUInt32("equip_cloths_id", 0);
                        template.EquipWeaponsId = reader.GetUInt32("equip_weapons_id", 0);
                        template.TotalCustomId = reader.GetUInt32("total_custom_id", 0);
                        template.BattleFieldRecruiter = reader.GetBoolean("battle_field_recruiter", true);
                        template.NpcStrafeParamId = reader.GetInt32("npc_strafe_param_id");
                        template.NpcAiClientParamId = reader.GetInt32("npc_ai_client_param_id");
                        template.SoundPackId = reader.GetUInt32("sound_pack_id");
                        template.DecayingSecAfterLooted = reader.GetInt32("decaying_sec_after_looted");
                        template.DontPushableLikeGhost = reader.GetBoolean("dont_pushable_like_ghost", true);
                        template.ForceTargetMeOnAttack = reader.GetBoolean("force_target_me_on_attack", true);
                        template.CheckBackpack = reader.GetBoolean("check_backpack", true);
                        template.CheckTargetUnderTerrain = reader.GetBoolean("check_target_under_terrain", true);
                        template.NationRelationVote = reader.GetBoolean("nation_relation_vote", true);
                        template.NoPenalty = reader.GetBoolean("no_penalty", true);
                        template.RunAwayThreshold = reader.GetFloat("run_away_threshold");
                        template.ShowFactionTag = reader.GetBoolean("show_faction_tag", true);
                        template.ShowOnBossTelescope = reader.GetBoolean("show_on_boss_telescope", true);
                        template.SoState = reader.GetString("so_state");
                        template.TradegoodBuy = reader.GetBoolean("tradegood_buy", true);
                        template.UseModelCameraDistance = reader.GetBoolean("use_model_camera_distance", true);
                        template.MateReviveDelay = reader.GetInt32("mate_revive_delay");
                        template.MateReviveHpPercent = reader.GetInt32("mate_revive_hp_percent");
                        template.MateReviveMpPercent = reader.GetInt32("mate_revive_mp_percent");

                        using (var command2 = connection.CreateCommand())
                        {
                            command2.CommandText = "SELECT char_race_id, char_gender_id FROM characters WHERE model_id = @model_id";
                            command2.Parameters.AddWithValue("model_id", template.ModelId);
                            command2.Prepare();
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

                        Templates.Add(template.Id, template);

                        if (template.EquipClothsId > 0)
                        {
                            using (var command2 = connection.CreateCommand())
                            {
                                command2.CommandText = "SELECT * FROM equip_pack_cloths WHERE id=@id";
                                command2.Parameters.AddWithValue("id", template.EquipClothsId);
                                command2.Prepare();
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
                                        template.Items.Backpack = reader2.GetUInt32("backpack_id");
                                        template.Items.BackpackGrade = reader2.GetByte("backpack_grade_id");
                                        template.Items.Cosplay = reader2.GetUInt32("cosplay_id");
                                        template.Items.CosplayGrade = reader2.GetByte("cosplay_grade_id");
                                        template.Items.Stabilizer = reader2.GetUInt32("stabilizer_id");
                                        template.Items.StabilizerGrade = reader2.GetByte("stabilizer_grade_id");
                                        template.Items.Undershirts = reader2.GetUInt32("undershirt_id");
                                        template.Items.UndershirtsGrade = reader2.GetByte("undershirt_grade_id");
                                        template.Items.Underpants = reader2.GetUInt32("underpants_id");
                                        template.Items.UnderpantsGrade = reader2.GetByte("underpants_grade_id");
                                    }
                                }
                            }
                        }

                        if (template.EquipWeaponsId > 0)
                        {
                            using (var command2 = connection.CreateCommand())
                            {
                                command2.CommandText = "SELECT * FROM equip_pack_weapons WHERE id=@id";
                                command2.Parameters.AddWithValue("id", template.EquipWeaponsId);
                                command2.Prepare();
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

                        if (template.TotalCustomId > 0 && TotalCharacterCustoms.TryGetValue(template.TotalCustomId, out var tc))
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
                                // Sort it by reverse "Time Of Day" so it's easier to do searches on it later
                                command2.CommandText = "SELECT * FROM npc_postures WHERE npc_posture_set_id=@id ORDER BY start_tod_time DESC";
                                command2.Parameters.AddWithValue("id", template.NpcPostureSetId);
                                command2.Prepare();
                                using (var sqliteReader2 = command2.ExecuteReader())
                                using (var reader2 = new SQLiteWrapperReader(sqliteReader2))
                                {
                                    while (reader2.Read())
                                    {
                                        var npcPosture = new NpcPosture
                                        {
                                            Id = reader2.GetUInt32("id"),
                                            NpcPostureSetId = reader2.GetUInt32("npc_posture_set_id"),
                                            AnimActionId = reader2.GetUInt32("anim_action_id"),
                                            TalkAnim = reader2.GetString("talk_anim"),
                                            StartTodTime = reader2.GetInt32("start_tod_time")
                                        };
                                        template.NpcPostureSets.Add(npcPosture);
                                    }
                                }
                            }
                        }

                        foreach (var (modelId, ibp) in ItemBodyParts)
                        {
                            if (modelId != template.ModelId) { continue; }

                            foreach (var (slotTypeId, bp) in ibp)
                            {
                                var rbp = bp[^1];
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

            //// This table seems to no longer be in some of the later versions
            //// Load body part packs (probably not used)
            //using (var command = connection.CreateCommand())
            //{
            //    command.CommandText = "SELECT * FROM equip_pack_body_parts";
            //    command.Prepare();
            //    using (var sqliteDataReader = command.ExecuteReader())
            //    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
            //    {
            //        while (reader.Read())
            //        {
            //            var template = new EquipBodyPartPack
            //            {
            //                Id = reader.GetUInt32("id"),
            //                Name = reader.GetString("name"),
            //                ModelId = reader.GetUInt32("model_id"),
            //                HairColorId = reader.GetUInt32("hair_color_id"),
            //                FaceId = reader.GetUInt32("face_id"),
            //                HairId = reader.GetUInt32("hair_id"),
            //                BeardId = reader.GetUInt32("beard_id"),
            //                SkinColorId = reader.GetUInt32("skin_color_id"),
            //                BodyDiffuseMapId =  reader.GetUInt32("body_diffuse_map_id"),
            //            };
            //            EquipPackBodyParts.Add(template.Id, template);
            //        }
            //    }
            //}

            // Load Unit modifiers for NPCs
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
                        if (!Templates.TryGetValue(npcId, out var npc))
                            continue;
                        var template = new BonusTemplate
                        {
                            Attribute = (UnitAttribute)reader.GetByte("unit_attribute_id"), ModifierType = (UnitModifierType)reader.GetByte("unit_modifier_type_id"),
                            Value = reader.GetInt32("value"),
                            LinearLevelBonus = reader.GetInt32("linear_level_bonus")
                        };
                        npc.Bonuses.Add(template);
                    }
                }
            }

            // Load initial Npc buffs
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
                        if (!Templates.TryGetValue(id, out var template))
                            continue;
                        template.Buffs.Add(buffId);
                    }
                }
            }

            Logger.Info("Loading merchant packs...");

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM merchants";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new Merchants();
                        template.NpcId = reader.GetUInt32("npc_id");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.GradeId = reader.GetUInt32("grade_id");
                        template.KindId = reader.GetUInt32("kind_id");

                        if (MerchantGoods.ContainsKey(template.NpcId))
                            MerchantGoods[template.NpcId].Add(template);
                        else
                            MerchantGoods.TryAdd(template.NpcId, [template]);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM merchant_packs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var id = reader.GetUInt32("pack_id");
                        var template = new MerchantPacks(id);
                        template.ItemId = reader.GetUInt32("item_id");
                        template.GradeId = reader.GetUInt32("grade_id");
                        template.KindId = reader.GetUInt32("kind_id");

                        if (MerchantPackGoods.ContainsKey(id))
                            MerchantPackGoods[id].Add(template);
                        else
                            MerchantPackGoods.TryAdd(id, [template]);
                    }
                }
            }

            Logger.Info($"Loaded {MerchantGoods.Count} merchants");
            Logger.Info($"Loaded {MerchantPackGoods.Count} merchant packs");
            Logger.Info($"Loaded {Templates.Count} npc templates");
        }

        // NpcGameData.Instance.LoadMemberAndSpawnerTemplateIds();

        Loaded = true;
    }

    /// <summary>
    /// Load AI settings from AiGameData into the Npc templates
    /// </summary>
    public void LoadAiParams()
    {
        foreach (var npc in Templates.Values)
        {
            npc.AiParams = AiGameData.Instance.GetAiParamsForId((uint)npc.NpcAiParamId);
        }
    }

    /// <summary>
    /// Populate items based on Npc template Id and slot
    /// </summary>
    /// <param name="npc"></param>
    /// <param name="templateId"></param>
    /// <param name="slot"></param>
    /// <param name="grade"></param>
    /// <param name="npcOnly"></param>
    private void SetEquipItemTemplate(Npc npc, uint templateId, EquipmentItemSlot slot, byte grade = 0, bool npcOnly = false)
    {
        if (npcOnly && npc.Equipment.GetItemBySlot((int)slot) != null)
            return;

        Item item = null;
        if (templateId > 0)
        {
            item = itemManager.Create(templateId, 1, grade, false);
            item.SlotType = SlotType.Equipment;
            item.Slot = (int)slot;
        }

        // npc.Equip[(int)slot] = item;
        npc.Equipment.AddOrMoveExistingItem(0, item, (int)slot);
    }

    /// <summary>
    /// Attaches a list of skills to a Npc template
    /// </summary>
    /// <param name="templateId"></param>
    /// <param name="skills"></param>
    public void BindSkillsToTemplate(uint templateId, List<NpcSkill> skills)
    {
        if (!Templates.TryGetValue(templateId, out var value))
            return;
        value.BindSkills(skills);
    }
}
