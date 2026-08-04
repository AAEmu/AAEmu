using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.Creatures;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Merchant;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers.UnitManagers;

public class NpcManager(
    IObjectIdManager objectIdManager,
    IModelManager modelManager,
    IFactionManager factionManager,
    IItemManager itemManager,
    ITaskManager taskManager) : Singleton<NpcManager>, INpcManager
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
    private readonly object _merchantPurchaseLock = new();
    private readonly Dictionary<(uint CharacterId, uint ItemTemplateId), MerchantPurchaseState> _merchantPurchases = [];
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

    public IReadOnlyDictionary<uint, MerchantPurchaseState> GetMerchantPurchaseStates(uint characterId)
    {
        var now = DateTime.UtcNow;
        lock (_merchantPurchaseLock)
        {
            return _merchantPurchases.Values
                .Where(state => state.CharacterId == characterId && state.BuyCount > 0 &&
                                state.PeriodStart == GetPeriodStart(state.PurchaseType, now))
                .ToDictionary(state => state.ItemTemplateId, state => state);
        }
    }

    public bool TryReserveMerchantPurchases(
        uint characterId,
        IEnumerable<(MerchantGoodsItem Good, int Count)> purchases,
        out MerchantGoodsItem failedGood,
        out IReadOnlyDictionary<uint, MerchantPurchaseState> updatedStates)
    {
        failedGood = null;
        updatedStates = new Dictionary<uint, MerchantPurchaseState>();
        var limited = purchases
            .Where(purchase => purchase.Good.PurchaseLimit > 0 && purchase.Count > 0)
            .GroupBy(purchase => purchase.Good.ItemTemplateId)
            .Select(group => (Good: group.First().Good, Count: group.Sum(entry => (long)entry.Count)))
            .ToList();
        if (limited.Count == 0)
            return true;

        var now = DateTime.UtcNow;
        lock (_merchantPurchaseLock)
        {
            var proposed = new Dictionary<(uint CharacterId, uint ItemTemplateId), MerchantPurchaseState>();
            foreach (var purchase in limited)
            {
                var good = purchase.Good;
                var key = (characterId, good.ItemTemplateId);
                var periodStart = GetPeriodStart(good.PurchaseType, now);
                var buyCount = 0;
                if (_merchantPurchases.TryGetValue(key, out var current) &&
                    current.PurchaseType == good.PurchaseType && current.PeriodStart == periodStart)
                {
                    buyCount = current.BuyCount;
                }

                if (purchase.Count > good.PurchaseLimit - (long)buyCount)
                {
                    failedGood = good;
                    return false;
                }

                proposed[key] = new MerchantPurchaseState
                {
                    CharacterId = characterId,
                    ItemTemplateId = good.ItemTemplateId,
                    BuyCount = buyCount + (int)purchase.Count,
                    PurchaseType = good.PurchaseType,
                    PeriodStart = periodStart
                };
            }

            try
            {
                SaveMerchantPurchases(proposed.Values);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to reserve limited merchant purchases for character {0}", characterId);
                return false;
            }

            foreach (var (key, state) in proposed)
                _merchantPurchases[key] = state;
            updatedStates = proposed.Values.ToDictionary(state => state.ItemTemplateId, state => state);
            return true;
        }
    }

    public bool TryRollbackMerchantPurchases(
        uint characterId,
        IEnumerable<(MerchantGoodsItem Good, int Count)> purchases)
    {
        var limited = purchases
            .Where(purchase => purchase.Good.PurchaseLimit > 0 && purchase.Count > 0)
            .GroupBy(purchase => purchase.Good.ItemTemplateId)
            .Select(group => (Good: group.First().Good, Count: group.Sum(entry => (long)entry.Count)))
            .ToList();
        if (limited.Count == 0)
            return true;

        lock (_merchantPurchaseLock)
        {
            var rolledBack = new Dictionary<(uint CharacterId, uint ItemTemplateId), MerchantPurchaseState>();
            foreach (var purchase in limited)
            {
                var key = (characterId, purchase.Good.ItemTemplateId);
                if (!_merchantPurchases.TryGetValue(key, out var current) ||
                    current.PurchaseType != purchase.Good.PurchaseType || current.BuyCount < purchase.Count)
                {
                    return false;
                }

                rolledBack[key] = new MerchantPurchaseState
                {
                    CharacterId = current.CharacterId,
                    ItemTemplateId = current.ItemTemplateId,
                    BuyCount = current.BuyCount - (int)purchase.Count,
                    PurchaseType = current.PurchaseType,
                    PeriodStart = current.PeriodStart
                };
            }

            try
            {
                SaveMerchantPurchases(rolledBack.Values);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to roll back limited merchant purchases for character {0}", characterId);
                return false;
            }

            foreach (var (key, state) in rolledBack)
            {
                if (state.BuyCount == 0)
                    _merchantPurchases.Remove(key);
                else
                    _merchantPurchases[key] = state;
            }
            return true;
        }
    }

    public void ResetMerchantPurchases(MerchantPurchaseType purchaseType)
    {
        lock (_merchantPurchaseLock)
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM character_merchant_purchases WHERE purchase_type = @purchase_type";
            command.Parameters.AddWithValue("@purchase_type", (byte)purchaseType);
            command.Prepare();
            command.ExecuteNonQuery();

            foreach (var key in _merchantPurchases
                         .Where(entry => entry.Value.PurchaseType == purchaseType)
                         .Select(entry => entry.Key)
                         .ToList())
            {
                _merchantPurchases.Remove(key);
            }
        }

        var resetMask = (sbyte)(2 << (byte)purchaseType);
        foreach (var character in WorldManager.Instance.GetAllCharacters())
            character.SendPacket(new SCResetMerchantGoodLimitPurchasePacket(resetMask));
    }

    public void Initialize()
    {
        taskManager.CronSchedule(
            new MerchantPurchaseResetTask(MerchantPurchaseType.Daily),
            "0 0 0 */1 * *");
        taskManager.CronSchedule(
            new MerchantPurchaseResetTask(MerchantPurchaseType.Weekly),
            "0 0 0 * * 1");
        taskManager.CronSchedule(
            new MerchantPurchaseResetTask(MerchantPurchaseType.Monthly),
            "0 0 0 1 * *");
    }

    private static DateTime GetPeriodStart(MerchantPurchaseType purchaseType, DateTime now)
    {
        var utc = now.Kind == DateTimeKind.Utc ? now : now.ToUniversalTime();
        return purchaseType switch
        {
            MerchantPurchaseType.Always => DateTime.UnixEpoch,
            MerchantPurchaseType.Daily => utc.Date,
            MerchantPurchaseType.Weekly => utc.Date.AddDays(-((7 + (int)utc.DayOfWeek - (int)DayOfWeek.Monday) % 7)),
            MerchantPurchaseType.Monthly => new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => DateTime.MaxValue
        };
    }

    private static void SaveMerchantPurchases(IEnumerable<MerchantPurchaseState> states)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var state in states)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO character_merchant_purchases " +
                "(character_id, item_id, buy_count, purchase_type, period_start) " +
                "VALUES (@character_id, @item_id, @buy_count, @purchase_type, @period_start) " +
                "ON DUPLICATE KEY UPDATE buy_count = VALUES(buy_count), " +
                "purchase_type = VALUES(purchase_type), period_start = VALUES(period_start)";
            command.Parameters.AddWithValue("@character_id", state.CharacterId);
            command.Parameters.AddWithValue("@item_id", state.ItemTemplateId);
            command.Parameters.AddWithValue("@buy_count", state.BuyCount);
            command.Parameters.AddWithValue("@purchase_type", (byte)state.PurchaseType);
            command.Parameters.AddWithValue("@period_start", state.PeriodStart);
            command.Prepare();
            command.ExecuteNonQuery();
        }
        transaction.Commit();
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

        npc.ModelParams = CopyModelParamsForNpc(template);

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
        SetEquipItemTemplate(npc, template.Items.Cosplay, EquipmentItemSlot.Cosplay, template.Items.CosplayGrade);

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
            randomTemplate.ModelParams = CreateFaceModelParams(template, tc);
        }
        else
        {
            // No total-custom row exists for this model. Preserve the appearance
            // mode selected from the database relationship: playable character
            // models use Skin, while authored special actor CDFs use None.
            randomTemplate.ModelParams = template.ModelParams ?? new UnitCustomModelParams();
        }

        foreach (var (modelId, ibp) in ItemBodyParts)
        {
            if (modelId != template.ModelId) { continue; }

            foreach (var (slotTypeId, bp) in ibp)
            {
                if (modelId != template.ModelId) { continue; }

                switch (slotTypeId)
                {
                    case (byte)EquipmentItemSlotType.Face:
                    {
                        var customFaceItemId = totalCustomId > 0 ? TotalCharacterCustoms[totalCustomId].FaceId : 0;
                        var preferredFaceItemId = customFaceItemId > 0 ? customFaceItemId : template.DefaultFaceItemId;
                        var rbp = bp.FirstOrDefault(bodyPart => bodyPart.ItemId == preferredFaceItemId) ?? bp[0];
                        randomTemplate.BodyItems[rbp.SlotTypeId - (int)EquipmentItemSlotType.Face] = (rbp.ItemId, rbp.NpcOnly);
                        break;
                    }
                    case (byte)EquipmentItemSlotType.Hair:
                    {
                        var rbp = bp.FirstOrDefault(bodyPart => bodyPart.ItemId == randomTemplate.HairId);
                        if (rbp != null)
                            randomTemplate.BodyItems[rbp.SlotTypeId - (int)EquipmentItemSlotType.Face] = (rbp.ItemId, rbp.NpcOnly);
                        break;
                    }
                    case (byte)EquipmentItemSlotType.Beard:
                    case (byte)EquipmentItemSlotType.Body:
                    case (byte)EquipmentItemSlotType.Glasses:
                    case (byte)EquipmentItemSlotType.Tail:
                    {
                        var rbp = bp[0];
                        randomTemplate.BodyItems[rbp.SlotTypeId - (int)EquipmentItemSlotType.Face] = (rbp.ItemId, rbp.NpcOnly);
                        break;
                    }
                }
            }
        }

        //Logger.Info("Loaded npc {0} random hair {1} and hairColor {2}", template.ModelId, _template.HairId, _template.ModelParams.HairColorId);

        return randomTemplate;
    }

    /// <summary>
    /// Per-instance clone so serialize-time BodyWeight defaults do not mutate the shared template.
    /// filling them from the NPC template coincides with the clothes regression under test.
    /// </summary>
    private static UnitCustomModelParams CopyModelParamsForNpc(NpcTemplate template)
    {
        var clone = (template.ModelParams ?? new UnitCustomModelParams()).Clone();
        if (clone.BodyWeight == 0f)
            clone.BodyWeight = 1f;
        return clone;
    }

    private static UnitCustomModelParams CreateSkinModelParams(NpcTemplate template)
    {
        // Skin-only actors still need race/gender for the T1 block; Face total-custom NPCs do not.
        var race = template.CharRaceId is > 0 and <= byte.MaxValue
            ? (byte)template.CharRaceId
            : template.Race;
        return new UnitCustomModelParams(UnitCustomModelType.Skin)
        {
            Race = race,
            Gender = template.Gender,
            VisualRace = race,
            VisualGender = template.Gender,
            BodyWeight = 1f,
            ModelId = template.ModelId
        };
    }

    /// <summary>
    /// hair_color_id → HairColorId via SetHairColorId, skin via SetSkinColorId, Face T3 morph,
    /// Race/Gender left 0. Canonical face mesh when face_id is zero still comes from the body-slot
    /// face item (characters.face_item_id). TODO(v10): correct HairColor vs HairColorId column
    /// </summary>
    private static UnitCustomModelParams CreateFaceModelParams(NpcTemplate template, TotalCharacterCustom custom)
    {
        var modelParams = new UnitCustomModelParams(UnitCustomModelType.Face)
            .SetModelId(custom.ModelId)
            .SetHairColorId(custom.HairColorId)
            .SetSkinColorId(custom.SkinColorId);

        modelParams.Face.MovableDecalAssetId = custom.FaceMovableDecalAssetId;
        modelParams.Face.MovableDecalWeight = custom.FaceMovableDecalWeight;
        modelParams.Face.MovableDecalScale = custom.FaceMovableDecalScale;
        modelParams.Face.MovableDecalRotate = custom.FaceMovableDecalRotate;
        modelParams.Face.MovableDecalMoveX = custom.FaceMovableDecalMoveX;
        modelParams.Face.MovableDecalMoveY = custom.FaceMovableDecalMoveY;
        modelParams.Face.SetFixedDecalAsset(0, custom.FaceFixedDecalAsset0Id, custom.FaceFixedDecalAsset0Weight);
        modelParams.Face.SetFixedDecalAsset(1, custom.FaceFixedDecalAsset1Id, custom.FaceFixedDecalAsset1Weight);
        modelParams.Face.SetFixedDecalAsset(2, custom.FaceFixedDecalAsset2Id, custom.FaceFixedDecalAsset2Weight);
        modelParams.Face.SetFixedDecalAsset(3, custom.FaceFixedDecalAsset3Id, custom.FaceFixedDecalAsset3Weight);
        modelParams.Face.SetFixedDecalAsset(4, custom.FaceFixedDecalAsset4Id, custom.FaceFixedDecalAsset4Weight);
        modelParams.Face.SetFixedDecalAsset(5, custom.FaceFixedDecalAsset5Id, custom.FaceFixedDecalAsset5Weight);
        modelParams.Face.DiffuseMapId = custom.FaceDiffuseMapId;
        modelParams.Face.NormalMapId = custom.FaceNormalMapId;
        modelParams.Face.EyelashMapId = custom.FaceEyelashMapId;
        modelParams.Face.NormalMapWeight = custom.FaceNormalMapWeight;
        modelParams.Face.LipColor = custom.LipColor;
        modelParams.Face.LeftPupilColor = custom.LeftPupilColor;
        modelParams.Face.RightPupilColor = custom.RightPupilColor;
        modelParams.Face.EyebrowColor = custom.EyebrowColor;
        modelParams.Face.DecoColor = custom.DecoColor;
        modelParams.Face.Modifier = [.. custom.Modifier];
        return modelParams;
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
                            Id = reader.GetUInt32("id"), ModelId = reader.GetUInt32("model_id"),
                            // name column removed in 10.0.2.13 schema; left as default
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
                            FaceDiffuseMapId = reader.GetUInt32("face_diffuse_map_id"),
                            FaceNormalMapId = reader.GetUInt32("face_normal_map_id"),
                            FaceEyelashMapId = reader.GetUInt32("face_eyelash_map_id"),
                            LipColor = reader.GetUInt32("lip_color"),
                            LeftPupilColor = reader.GetUInt32("left_pupil_color"),
                            RightPupilColor = reader.GetUInt32("right_pupil_color"),
                            EyebrowColor = reader.GetUInt32("eyebrow_color")
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
                        custom.TwoToneHairColor = reader.GetUInt32("two_tone_hair_color");
                        custom.TwoToneFirstWidth = reader.GetFloat("two_tone_first_width");
                        custom.TwoToneSecondWidth = reader.GetFloat("two_tone_second_width");
                        custom.DefaultHairColor = reader.GetUInt32("default_hair_color");
                        custom.BodyNormalMapId = reader.GetUInt32("body_normal_map_id");
                        custom.BodyNormalMapWeight = reader.GetFloat("body_normal_map_weight");
                        custom.FaceFixedDecalAsset4Id = reader.GetUInt32("face_fixed_decal_asset_4_id");
                        custom.FaceFixedDecalAsset4Weight = reader.GetFloat("face_fixed_decal_asset_4_weight");
                        custom.FaceFixedDecalAsset5Id = reader.GetUInt32("face_fixed_decal_asset_5_id");
                        custom.FaceFixedDecalAsset5Weight = reader.GetFloat("face_fixed_decal_asset_5_weight");
                        custom.HornColorId = reader.GetUInt32("horn_color_id");
                        custom.FaceId = reader.GetUInt32("face_id");

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
                command.CommandText = "SELECT * FROM item_body_parts ORDER BY id";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
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
                        var template = new NpcTemplate
                        {
                            Id = reader.GetUInt32("id"), Name = reader.GetString("name"), CharRaceId = reader.GetInt32("char_race_id"),
                            NpcGradeId = (NpcGradeType)reader.GetByte("npc_grade_id"),
                            NpcKindId = (NpcKindType)reader.GetByte("npc_kind_id"),
                            // 10.0.2.13: npcs.level can exceed 255 (e.g. 5055); clamp into the byte field to avoid OverflowException
                            Level = (byte)Math.Clamp(reader.GetInt32("level"), 0, 255),
                            NpcTemplateId = (NpcTemplateType)reader.GetByte("npc_template_id"),
                            ModelId = reader.GetUInt32("model_id"),
                            FactionId = (FactionsEnum)reader.GetUInt32("faction_id"),
                            SkillTrainer = reader.GetBoolean("skill_trainer", true),
                            Merchant = reader.GetBoolean("merchant", true),
                            NpcNicknameId = reader.GetInt32("npc_nickname_id"),
                            Auctioneer = reader.GetBoolean("auctioneer", true),
                            ShowNameTag = reader.GetBoolean("show_name_tag", true),
                            VisibleToCreatorOnly = reader.GetBoolean("visible_to_creator_only", true),
                            NoExp = reader.GetBoolean("no_exp", true),
                            PetItemId = reader.GetUInt32("pet_item_id", 0),
                            BaseSkillId = reader.GetInt32("base_skill_id"),
                            TrackFriendship = reader.GetBoolean("track_friendship", true),
                            Priest = reader.GetBoolean("priest", true),
                            NpcTedencyId = reader.GetInt32("npc_tendency_id", 0),
                            Blacksmith = reader.GetBoolean("blacksmith", true),
                            Teleporter = reader.GetBoolean("teleporter", true),
                            Opacity = reader.GetFloat("opacity"),
                            AbilityChanger = reader.GetBoolean("ability_changer", true),
                            Scale = reader.GetFloat("scale"),
                            SightRangeScale = reader.GetFloat("sight_range_scale"),
                            SightFovScale = reader.GetFloat("sight_fov_scale"),
                            MilestoneId = reader.GetInt32("milestone_id", 0),
                            AttackStartRangeScale = reader.GetFloat("attack_start_range_scale"),
                            Aggression = reader.GetBoolean("aggression", true),
                            ExpMultiplier = reader.GetFloat("exp_multiplier"),
                            ExpAdder = reader.GetInt32("exp_adder"),
                            Stabler = reader.GetBoolean("stabler", true),
                            AcceptAggroLink = reader.GetBoolean("accept_aggro_link", true),
                            // recruiting_battle_field_id column removed in 10.0.2.13 schema
                            ReturnDistance = reader.GetFloat("return_distance"),
                            NonPushableByActor = reader.GetBoolean("non_pushable_by_actor", true),
                            Banker = reader.GetBoolean("banker", true),
                            AggroLinkSpecialRuleId = (AggroLinkSpecialRuleKind)reader.GetInt32("aggro_link_special_rule_id"),
                            AggroLinkHelpDist = reader.GetFloat("aggro_link_help_dist"),
                            AggroLinkSightCheck = reader.GetBoolean("aggro_link_sight_check", true),
                            Expedition = reader.GetBoolean("expedition", true),
                            HonorPoint = reader.GetInt32("honor_point"),
                            Trader = reader.GetBoolean("trader", true),
                            AggroLinkSpecialGuard = reader.GetBoolean("aggro_link_special_guard", true),
                            AggroLinkSpecialIgnoreNpcAttacker = reader.GetBoolean("aggro_link_special_ignore_npc_attacker", true),
                            AbsoluteReturnDistance = reader.GetFloat("absolute_return_distance"),
                            Repairman = reader.GetBoolean("repairman", true),
                            ActivateAiAlways = reader.GetBoolean("activate_ai_always", true),
                            Specialty = reader.GetBoolean("specialty", true),
                            SpecialtyCoinId = reader.GetUInt32("specialty_coin_id", 0),
                            UseRangeMod = reader.GetBoolean("use_range_mod", true),
                            NpcPostureSetId = reader.GetInt32("npc_posture_set_id"),
                            MateEquipSlotPackId = reader.GetInt32("mate_equip_slot_pack_id", 0),
                            MateKindId = reader.GetInt32("mate_kind_id", 0),
                            EngageCombatGiveQuestId = reader.GetUInt32("engage_combat_give_quest_id", 0),
                            NoApplyTotalCustom = reader.GetBoolean("no_apply_total_custom", true),
                            BaseSkillStrafe = reader.GetBoolean("base_skill_strafe", true),
                            BaseSkillDelay = reader.GetFloat("base_skill_delay"),
                            NpcInteractionSetId = reader.GetInt32("npc_interaction_set_id", 0),
                            UseAbuserList = reader.GetBoolean("use_abuser_list", true),
                            ReturnWhenEnterHousingArea = reader.GetBoolean("return_when_enter_housing_area", true),
                            LookConverter = reader.GetBoolean("look_converter", true),
                            UseDDCMSMountSkill = reader.GetBoolean("use_ddcms_mount_skill", true),
                            CrowdEffect = reader.GetBoolean("crowd_effect", true),
                            // equip_bodies_id column removed in 10.0.2.13 schema; defaults to 0
                            EquipClothsId = reader.GetUInt32("equip_cloths_id", 0),
                            EquipWeaponsId = reader.GetUInt32("equip_weapons_id", 0),
                            TotalCustomId = reader.GetUInt32("total_custom_id", 0)
                        };

                        using (var command2 = connection.CreateCommand())
                        {
                            command2.CommandText = "SELECT char_race_id, char_gender_id, face_item_id FROM characters WHERE model_id = @model_id";
                            command2.Parameters.AddWithValue("model_id", template.ModelId);
                            command2.Prepare();
                            using (var sqliteReader2 = command2.ExecuteReader())
                            using (var reader2 = new SQLiteWrapperReader(sqliteReader2))
                            {
                                if (reader2.Read())
                                {
                                    template.UsesCharacterAppearance = true;
                                    template.Race = reader2.GetByte("char_race_id");
                                    template.Gender = reader2.GetByte("char_gender_id");
                                    template.DefaultFaceItemId = reader2.GetUInt32("face_item_id", 0);
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
                            template.ModelParams = CreateFaceModelParams(template, tc);
                        }
                        else
                        {
                            template.ModelParams = template.UsesCharacterAppearance
                                ? CreateSkinModelParams(template)
                                : new UnitCustomModelParams();
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
                                            NpcPostureSetId = reader2.GetUInt32("npc_posture_set_id"),
                                            AnimActionId = reader2.GetUInt32("anim_action_id"),
                                            TalkAnim = reader2.GetString("talk_anim"),
                                            StartTodTime = reader2.GetFloat("start_tod_time")
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
                                if (modelId != template.ModelId) { continue; }

                                switch (slotTypeId)
                                {
                                    case (byte)EquipmentItemSlotType.Face:
                                    {
                                        var customFaceItemId = template.TotalCustomId > 0 &&
                                                               TotalCharacterCustoms.TryGetValue(template.TotalCustomId, out var custom)
                                            ? custom.FaceId
                                            : 0;
                                        var preferredFaceItemId = customFaceItemId > 0
                                            ? customFaceItemId
                                            : template.DefaultFaceItemId;
                                        var rbp = bp.FirstOrDefault(bodyPart => bodyPart.ItemId == preferredFaceItemId) ?? bp[0];
                                        template.BodyItems[rbp.SlotTypeId - (int)EquipmentItemSlotType.Face] = (rbp.ItemId, rbp.NpcOnly);
                                        break;
                                    }
                                    case (byte)EquipmentItemSlotType.Hair:
                                    {
                                        var rbp = bp.FirstOrDefault(bodyPart => bodyPart.ItemId == template.HairId);
                                        if (rbp != null)
                                            template.BodyItems[rbp.SlotTypeId - (int)EquipmentItemSlotType.Face] = (rbp.ItemId, rbp.NpcOnly);
                                        break;
                                    }
                                    case (byte)EquipmentItemSlotType.Beard:
                                    case (byte)EquipmentItemSlotType.Body:
                                    case (byte)EquipmentItemSlotType.Glasses:
                                    case (byte)EquipmentItemSlotType.Tail:
                                    {
                                        var rbp = bp[0];
                                        template.BodyItems[rbp.SlotTypeId - (int)EquipmentItemSlotType.Face] = (rbp.ItemId, rbp.NpcOnly);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

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
                            // 10.0.2.13: unit_attribute_id reaches 256-261; UnitAttribute is uint-backed, read directly (no clamp/truncation).
                            Attribute = (UnitAttribute)reader.GetUInt32("unit_attribute_id"), ModifierType = (UnitModifierType)reader.GetByte("unit_modifier_type_id"),
                            Value = reader.GetInt64("value"),
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

            // Load merchant list
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM merchants";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var id = reader.GetUInt32("npc_id");
                        if (!Templates.TryGetValue(id, out var template))
                            continue;
                        template.MerchantPackId = reader.GetUInt32("merchant_pack_id");
                    }
                }
            }

            // Done loading main Npc data
            Logger.Info($"Loaded {Templates.Count} npc templates");

            // Loading merchant stuff
            Logger.Info("Loading merchant packs...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, kind_id, item_point_id FROM merchant_packs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var id = reader.GetUInt32("id");
                        Goods[id] = new MerchantGoods(
                            id,
                            (MerchantPackKind)reader.GetByte("kind_id"),
                            reader.GetUInt32("item_point_id"));
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM merchant_goods WHERE enable = 't'";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var packId = reader.GetUInt32("merchant_pack_id");
                        if (!Goods.TryGetValue(packId, out var pack))
                            continue;

                        var itemId = reader.GetUInt32("item_id");
                        var grade = reader.GetByte("grade_id");
                        var currency = pack.Kind switch
                        {
                            MerchantPackKind.Money => ShopCurrencyType.Money,
                            MerchantPackKind.Honor => ShopCurrencyType.Honor,
                            MerchantPackKind.Vocation => ShopCurrencyType.VocationBadges,
                            MerchantPackKind.ItemPoint or MerchantPackKind.CustomItemPoint => ShopCurrencyType.ItemPoint,
                            _ => (ShopCurrencyType)byte.MaxValue
                        };
                        if (currency == (ShopCurrencyType)byte.MaxValue)
                            continue;

                        var overrideCost = reader.GetInt32("cost");
                        int? price = pack.Kind == MerchantPackKind.CustomItemPoint
                            ? overrideCost
                            : overrideCost > 0
                                ? overrideCost
                                : itemManager.GetShopPrice(itemId, currency);
                        if (price is null || price < 0)
                        {
                            Logger.Warn(
                                "Skipping merchant good {0} in pack {1}: item {2} has no non-negative price for currency {3}",
                                reader.GetUInt32("id"), packId, itemId, currency);
                            continue;
                        }

                        pack.AddItemToStock(new MerchantGoodsItem
                        {
                            Id = reader.GetUInt32("id"),
                            ItemTemplateId = itemId,
                            Grade = grade,
                            Cost = price.Value,
                            Currency = currency,
                            PurchaseType = (MerchantPurchaseType)reader.GetByte("purchase_type_id"),
                            PurchaseLimit = reader.GetInt32("purchase_limit")
                        });
                    }
                }
            }

            Logger.Info($"Loaded {Goods.Count} merchant packs");
        }

        LoadMerchantPurchases();

        // NpcGameData.Instance.LoadMemberAndSpawnerTemplateIds();

        Loaded = true;
    }

    private void LoadMerchantPurchases()
    {
        lock (_merchantPurchaseLock)
        {
            _merchantPurchases.Clear();
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM character_merchant_purchases";
            command.Prepare();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var periodStart = DateTime.SpecifyKind(reader.GetDateTime("period_start"), DateTimeKind.Utc);
                var state = new MerchantPurchaseState
                {
                    CharacterId = reader.GetUInt32("character_id"),
                    ItemTemplateId = reader.GetUInt32("item_id"),
                    BuyCount = reader.GetInt32("buy_count"),
                    PurchaseType = (MerchantPurchaseType)reader.GetByte("purchase_type"),
                    PeriodStart = periodStart
                };
                if (state.BuyCount <= 0 || GetPeriodStart(state.PurchaseType, DateTime.UtcNow) == DateTime.MaxValue)
                    continue;

                _merchantPurchases[(state.CharacterId, state.ItemTemplateId)] = state;
            }
        }

        Logger.Info("Loaded {0} character merchant purchase limits", _merchantPurchases.Count);
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
            if (itemManager.GetTemplate(templateId) is ArmorTemplate armorTemplate &&
                !armorTemplate.HasCompatibleVisual(npc.ModelId))
                return;

            item = itemManager.Create(templateId, 1, grade, false);
            if (UsesNpcFullItemWire(slot) && item.Id == 0)
            {
                // wire-only identity so the record is not an all-zero item header. Does not
                // at record +0x08; visual selection uses templateId / imageTemplateId, not Id.
                item.Id = CreateTransientNpcEquipmentId(npc.ObjId, slot);
            }
            item.SlotType = SlotType.Equipment;
            item.Slot = (int)slot;
        }

        // npc.Equip[(int)slot] = item;
        npc.Equipment.AddOrMoveExistingItem(0, item, (int)slot);
    }

    private static bool UsesNpcFullItemWire(EquipmentItemSlot slot) =>
        slot == EquipmentItemSlot.Cosplay || (int)slot is >= 31 and <= 33;

    private static ulong CreateTransientNpcEquipmentId(uint npcObjId, EquipmentItemSlot slot) =>
        ((ulong)npcObjId << 32) | ((uint)slot + 1u);

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
