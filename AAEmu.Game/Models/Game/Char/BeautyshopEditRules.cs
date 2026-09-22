using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Char;

/// <summary><c>enum_customizing_item_asset_categories</c>: 1 hair, 2 horn, 3 tail.</summary>
public enum CustomizingCategory : byte
{
    Hair = 1,
    Horn = 2,
    Tail = 3
}

/// <summary>One <c>customizing_item_assets</c> row: a hair, horn or tail item usable by one model.</summary>
public readonly record struct CustomizingBodyPart(uint ModelId, CustomizingCategory Category, bool TwoTone, bool UsePallet);

/// <summary>One <c>face_decal_assets</c> row.</summary>
public readonly record struct FaceDecalAsset(uint ModelId, byte Category, bool Movable, bool NpcOnly);

/// <summary>
/// What the salon rules need to know about the customization tables, so the rules stay value-in,
/// value-out and the tests can hand in a small catalog.
/// </summary>
public interface ICharacterCustomizationCatalog
{
    bool TryGetBodyPart(uint modelId, uint itemId, out CustomizingBodyPart part);
    bool IsBodyPartItem(uint modelId, EquipmentItemSlot slot, uint itemId);
    bool IsColor(CustomizingCategory category, uint colorId);
    bool IsSkinColor(uint modelId, uint id);
    bool IsFaceNormalMap(uint modelId, uint id);
    bool IsFaceDiffuseMap(uint modelId, uint id);
    bool IsFaceEyelashMap(uint modelId, uint id);
    bool IsBodyNormalMap(uint modelId, uint id);
    bool IsBodyDiffuseMap(uint modelId, uint id);
    bool TryGetFaceDecal(uint id, out FaceDecalAsset decal);
}

public enum BeautyshopEditError
{
    None = 0,
    RaceOrGenderMismatch,
    UnknownBodyPart,
    BodyPartCategoryMismatch,
    UnknownHairColor,
    UnknownHornColor,
    UnknownSkinColor,
    UnknownFaceMap,
    UnknownBodyMap,
    UnknownMovableDecal,
    UnknownFixedDecal,
    MissingFace,
    ValueNotFinite,
    ValueOutOfRange,
    ModifierTooLong
}

public enum BeautyshopEnterError
{
    None = 0,
    Dead,
    UnderEscort,
    AlreadyInside
}

public enum BeautyshopCharge
{
    Rejected = 0,
    Ticket,
    FreeWindow
}

/// <summary>
/// The salon's decision logic: which fields the character's model may take, what the edit costs, and
/// when the shop may be entered. The wire side (CSBeautyshopDataPacket, FUN_39c77890) and the apply
/// order live in CharacterManager.
/// </summary>
public static class BeautyshopEditRules
{
    /// <summary>
    /// A stock client always fills the face-maker morph block to its cap: the serializer reads it with
    /// a 0x80 capacity (x2game-dev.dll FUN_39399a30, "modifiers") and every custom_face_presets and
    /// total_character_customs row carries exactly 128 bytes.
    /// </summary>
    public const int MaxModifierLength = 128;

    /// <summary>
    /// The category each fixed decal slot carries, read off the 146 shipped PC presets
    /// (total_character_customs, owner_type_id 1, npcOnly 'f') with no exception: slot 0 tattoo (2),
    /// slot 1 makeup (3), slot 2 eyebrow (4), slot 3 deco (5), slots 4 and 5 pupil (6) for the two eyes.
    /// enum_face_decal_category names the ids. Presets also put movable assets into these slots, so
    /// only the category is checked here, not face_decal_assets.movable.
    /// </summary>
    public static readonly byte[] FixedDecalCategories = [2, 3, 4, 5, 6, 6];

    /// <summary>
    /// The movable decal transform bounds. Every shipped preset stays inside scale 0 to 1.82 and
    /// rotation -169.2 to 356.4 degrees (total_character_customs, all 1589 rows of owner types 1 to 3),
    /// but the scar sliders run 0 to 100 and the client maps them to scale v*0.017+0.3 (0.3 to 2.0) and
    /// rotation v*3.6-180 (-180 to 180) in customizing_new/beautyshop.lua; character creation uses the
    /// same sliders and the salon sends the whole face block back, so the mapped range is accepted too.
    /// The preset maximum rotation stays for values saved before this. Every weight and both two-tone
    /// widths there stay in 0 to 1.
    /// </summary>
    public const float MaxMovableDecalScale = 2f;
    public const float MinMovableDecalRotate = -180f;
    public const float MaxMovableDecalRotate = 356.4f;

    /// <summary>
    /// The client's "no item" sentinel for a customizing slot is a runtime-initialised static
    /// (x2game-dev.dll DAT_3b4e162c, compared in FUN_396105e0 before every asset lookup), so its value
    /// is not in the image. 0 and -1 are both treated as "leave that slot alone"; neither is a valid
    /// content id (customizing_item_assets and customizing_item_asset_colors ids start at 1).
    /// </summary>
    public static bool IsNone(int id) => id <= 0;

    public static bool IsNone(uint id) => id == 0 || id == uint.MaxValue;

    /// <summary>
    /// A requested hair, horn or tail item must be a customizing_item_assets row for the character's
    /// model with the category of the slot it is meant for (the client checks the same row and
    /// category in FUN_396105e0 before sending; horn is only kept for CharRace 8 and tail for 6 there,
    /// which the per-model rows already encode: category 2 exists only for models 24/25, category 3
    /// only for 20/21).
    /// </summary>
    public static BeautyshopEditError ValidateBodyPart(
        ICharacterCustomizationCatalog catalog, uint modelId, CustomizingCategory category, int itemId)
    {
        if (IsNone(itemId))
            return BeautyshopEditError.None;
        if (!catalog.TryGetBodyPart(modelId, (uint)itemId, out var part))
            return BeautyshopEditError.UnknownBodyPart;
        return part.Category == category ? BeautyshopEditError.None : BeautyshopEditError.BodyPartCategoryMismatch;
    }

    /// <summary>
    /// Every id in the requested appearance block must exist for the character's model. Colors sent as
    /// raw RGBA (defaultHairColor, twoToneHair, lip, pupils, eyebrow, deco) have no content domain and
    /// pass as-is; the two color ids are checked against customizing_item_asset_colors by category only,
    /// because 34 of the 146 shipped presets reference a color row of another model. The block must
    /// carry the face (ext at least Face): MergeModel clones the request and the character is saved and
    /// broadcast with whatever it holds, so a block without one erases the stored face for good.
    /// </summary>
    public static BeautyshopEditError ValidateModel(
        ICharacterCustomizationCatalog catalog, uint modelId, byte race, byte gender, UnitCustomModelParams requested)
    {
        if (requested.Race != race || requested.Gender != gender)
            return BeautyshopEditError.RaceOrGenderMismatch;

        // FUN_396105e0 zeroes block offset 0xa0 (defaultHairColor) unless the hair asset has use_pallet
        // and sets offset 0x20 (this first "type" u32) to the none sentinel when it has, so the first
        // u32 is the customizing_item_asset_colors id and defaultHairColor is the free RGBA.
        if (!IsNone(requested.HairColor) && !catalog.IsColor(CustomizingCategory.Hair, requested.HairColor))
            return BeautyshopEditError.UnknownHairColor;
        if (!IsNone(requested.HornColor) && !catalog.IsColor(CustomizingCategory.Horn, requested.HornColor))
            return BeautyshopEditError.UnknownHornColor;
        if (!float.IsFinite(requested.TwoToneFirstWidth) || !float.IsFinite(requested.TwoToneSecondWidth))
            return BeautyshopEditError.ValueNotFinite;
        if (!IsUnitWeight(requested.TwoToneFirstWidth) || !IsUnitWeight(requested.TwoToneSecondWidth))
            return BeautyshopEditError.ValueOutOfRange;

        // skin_colors rows are per model; every creatable model has its own (8 to 21 rows each).
        if (!catalog.IsSkinColor(modelId, requested.SkinColorId))
            return BeautyshopEditError.UnknownSkinColor;
        if (!IsNone(requested.BodyNormalMap) && !catalog.IsBodyNormalMap(modelId, requested.BodyNormalMap))
            return BeautyshopEditError.UnknownBodyMap;
        if (!IsNone(requested.BodyDiffuseMap) && !catalog.IsBodyDiffuseMap(modelId, requested.BodyDiffuseMap))
            return BeautyshopEditError.UnknownBodyMap;
        if (!float.IsFinite(requested.BodyWeight))
            return BeautyshopEditError.ValueNotFinite;

        var face = requested.Face;
        if (face == null)
            return BeautyshopEditError.MissingFace;

        if (!IsNone(face.NormalMapId) && !catalog.IsFaceNormalMap(modelId, face.NormalMapId))
            return BeautyshopEditError.UnknownFaceMap;
        // face_diffuse_maps and face_eyelash_maps ship empty, so only 0 can pass here.
        if (!IsNone(face.DiffuseMapId) && !catalog.IsFaceDiffuseMap(modelId, face.DiffuseMapId))
            return BeautyshopEditError.UnknownFaceMap;
        if (!IsNone(face.EyelashMapId) && !catalog.IsFaceEyelashMap(modelId, face.EyelashMapId))
            return BeautyshopEditError.UnknownFaceMap;

        if (!IsNone(face.MovableDecalAssetId))
        {
            if (!catalog.TryGetFaceDecal(face.MovableDecalAssetId, out var movable)
                || movable.ModelId != modelId || !movable.Movable || movable.NpcOnly)
                return BeautyshopEditError.UnknownMovableDecal;
        }
        if (!float.IsFinite(face.MovableDecalWeight) || !float.IsFinite(face.MovableDecalScale) || !float.IsFinite(face.MovableDecalRotate))
            return BeautyshopEditError.ValueNotFinite;
        if (!IsUnitWeight(face.MovableDecalWeight) || face.MovableDecalScale is < 0f or > MaxMovableDecalScale
            || face.MovableDecalRotate is < MinMovableDecalRotate or > MaxMovableDecalRotate)
            return BeautyshopEditError.ValueOutOfRange;

        for (var i = 0; i < FixedDecalCategories.Length && i < face.FixedDecalAssetCount; i++)
        {
            var slot = face.GetFixedDecalAsset(i);
            if (!float.IsFinite(slot.AssetWeight))
                return BeautyshopEditError.ValueNotFinite;
            if (!IsUnitWeight(slot.AssetWeight))
                return BeautyshopEditError.ValueOutOfRange;
            if (IsNone(slot.AssetId))
                continue;
            if (!catalog.TryGetFaceDecal(slot.AssetId, out var fixedDecal)
                || fixedDecal.ModelId != modelId || fixedDecal.NpcOnly || fixedDecal.Category != FixedDecalCategories[i])
                return BeautyshopEditError.UnknownFixedDecal;
        }

        if (!float.IsFinite(face.NormalMapWeight))
            return BeautyshopEditError.ValueNotFinite;
        if (!IsUnitWeight(face.NormalMapWeight))
            return BeautyshopEditError.ValueOutOfRange;
        if (face.Modifier != null && face.Modifier.Length > MaxModifierLength)
            return BeautyshopEditError.ModifierTooLong;

        return BeautyshopEditError.None;
    }

    /// <summary>A blend weight or width the presets keep between 0 and 1; NaN fails the comparison.</summary>
    private static bool IsUnitWeight(float value) => value is >= 0f and <= 1f;

    /// <summary>
    /// The salon has no control for the race-change override (that is special effect 189,
    /// change_visual_race), so the stored visual race trio survives the edit untouched; everything else
    /// comes from the request.
    /// </summary>
    public static UnitCustomModelParams MergeModel(UnitCustomModelParams current, UnitCustomModelParams requested)
    {
        var merged = requested.Clone();
        merged.Race = current.Race;
        merged.Gender = current.Gender;
        merged.VisualRace = current.VisualRace;
        merged.VisualGender = current.VisualGender;
        merged.VisualRaceExpiredTime = current.VisualRaceExpiredTime;
        merged.ModelId = current.ModelId;
        return merged;
    }

    /// <summary>
    /// A ticket pays while it has not run out. The five shipped tickets (tag 4990, items 50445 to 50449)
    /// are period passes: max_stack_size 1 and exp_abs_lifetime 1440, 4320, 10080, 21600 and 43200
    /// minutes, which only makes sense if a pass covers every edit inside its period, so nothing is
    /// consumed. The set only holds templates with a lifetime (tag 4990 also carries the retired 51915,
    /// which has none and the loader drops), so MinValue means a legacy row without a stored expire_time.
    /// </summary>
    public static bool IsValidTicket(uint templateId, DateTime expirationTime, DateTime now, IReadOnlySet<uint> ticketItemIds) =>
        ticketItemIds.Contains(templateId) && (expirationTime == DateTime.MinValue || expirationTime > now);

    /// <summary>
    /// game_schedule_beautyshops opens the salon for free while its game_schedules row runs; a row with
    /// is_pcbang 't' only applies to a PC-bang account. The server publishes pcbang false in
    /// SCInitialConfig, so only the 'f' rows can ever open here.
    /// </summary>
    public static bool IsFreeWindowOpen(
        IEnumerable<(int ScheduleId, bool PcBang)> beautyshopSchedules, IReadOnlySet<int> runningScheduleIds, bool accountIsPcBang)
    {
        foreach (var (scheduleId, pcBang) in beautyshopSchedules)
        {
            if (pcBang && !accountIsPcBang)
                continue;
            if (runningScheduleIds.Contains(scheduleId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The client presents the first ticket it finds and count 1, or the none id and count 0 when it
    /// holds none (FUN_396f9110 / FUN_396105e0), and either way sends the edit. The server checks the
    /// bag itself: a valid ticket pays, otherwise only a running free window does.
    /// </summary>
    public static BeautyshopCharge DecideCharge(bool holdsValidTicket, bool freeWindowOpen)
    {
        if (holdsValidTicket)
            return BeautyshopCharge.Ticket;
        return freeWindowOpen ? BeautyshopCharge.FreeWindow : BeautyshopCharge.Rejected;
    }

    /// <summary>
    /// Mirrors the client's own gate (FUN_396f6c60): a dead character and one under buff 3619
    /// (강제 연행, forced escort to trial) cannot open the shop. A second enter while inside is ignored
    /// so the session start time stays the first one.
    /// </summary>
    public static BeautyshopEnterError CanEnter(bool isDead, bool underEscort, bool alreadyInside)
    {
        if (isDead)
            return BeautyshopEnterError.Dead;
        if (underEscort)
            return BeautyshopEnterError.UnderEscort;
        return alreadyInside ? BeautyshopEnterError.AlreadyInside : BeautyshopEnterError.None;
    }
}
