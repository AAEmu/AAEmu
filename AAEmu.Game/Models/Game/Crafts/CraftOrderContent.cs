using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>
/// Catalog keys for the craft-order slice. Numeric ids come from compact at load; they are not
/// written here.
/// </summary>
public static class CraftOrderContent
{
    /// <summary><c>const_item_types.name</c> for the request-sheet item.</summary>
    public const string SheetItemConstName = "craft_order";

    /// <summary><c>const_skill_types.name</c> the folio casts to make a sheet.</summary>
    public const string MakeSheetSkillConstName = "make_craft_order_sheet";

    /// <summary><c>const_skill_types.name</c> the restore tab casts to break a sheet up.</summary>
    public const string RestoreSheetSkillConstName = "restore_craft_order_sheet";

    /// <summary><c>const_skill_types.name</c> the board casts to fill a posted row.</summary>
    public const string ProcessSkillConstName = "process_craft_order";

    /// <summary><c>const_skill_types.name</c> My List Complete casts.</summary>
    public const string InstantSkillConstName = "process_craft_order_instant";

    /// <summary><c>content_configs</c> permille cut taken from a listed fee.</summary>
    public const string ChargeConfigName = "craft_order_charge_for_resident";

    public static uint SheetItemId => ItemManager.Instance.GetConstItemId(SheetItemConstName);

    public static uint MakeSheetSkillId => SkillManager.Instance.GetConstSkillId(MakeSheetSkillConstName);

    public static uint RestoreSheetSkillId => SkillManager.Instance.GetConstSkillId(RestoreSheetSkillConstName);

    public static uint ProcessSkillId => SkillManager.Instance.GetConstSkillId(ProcessSkillConstName);

    public static uint InstantSkillId => SkillManager.Instance.GetConstSkillId(InstantSkillConstName);

    /// <summary>Resident cut. Missing row is not a number — the caller must refuse.</summary>
    public static bool TryChargePermille(out int permille) =>
        ContentConfigGameData.Instance.TryGetInt(ChargeConfigName, out permille);

    public static bool IsCraftOrderSkill(uint skillId) =>
        skillId != 0 && (skillId == ProcessSkillId || skillId == InstantSkillId);

    public static bool IsMakeSheetSkill(uint skillId) =>
        skillId != 0 && skillId == MakeSheetSkillId;

    public static bool IsRestoreSheetSkill(uint skillId) =>
        skillId != 0 && skillId == RestoreSheetSkillId;
}
