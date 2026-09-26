using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>
/// Making a request sheet: the folio casts a skill, the cast names the craft and how many, and the
/// server turns that into the sheet item.
/// </summary>
public static class CraftOrderSheetRules
{
    /// <summary>
    /// ActionResult kind that resets the post frame and leaves craft-order mode.
    /// The client's POST_CRAFT_ORDER handler listens for this, not the fill kind.
    /// (the SCCraftOrderActionResult handler) raises UI event 0x23a
    /// for kind 1 and 0x239 for kind 2; the event table registers from,
    /// which puts POST_CRAFT_ORDER at index 0x23a and CANCEL_CRAFT_ORDER at 0x239.
    /// </summary>
    public const byte PostActionKind = 1;

    /// <summary>ActionResult kind the board treats as CANCEL_CRAFT_ORDER.</summary>
    public const byte CancelActionKind = 2;

    /// <summary>
    /// ActionResult kind that clears the restore tab after the sheet is gone. The client treats
    /// kind 3 + true as "drop the selected restore slot"; kinds 0 and 5 close the fill window,
    /// not this one.
    /// </summary>
    public const byte RestoreActionKind = 3;

    /// <summary>
    /// The sheet's detail body: craft id, grade, count, actability group. The type byte sits in
    /// front of this and is not counted here.
    /// </summary>
    public const uint DetailBytes = sizeof(uint) + sizeof(byte) + sizeof(uint) + sizeof(uint);

    /// <summary>
    /// Bytes the cast appends for this skill: a u32 craft, a u32 count and one trailing byte whose
    /// meaning is not established (0 on every cast measured).
    /// </summary>
    public const int CastTailBytes = sizeof(uint) + sizeof(uint) + sizeof(byte);

    /// <summary>
    /// Grade a sheet (or a posted order) asks for: the craft's first product grade when that
    /// product uses a grade, otherwise zero.
    /// </summary>
    public static byte GradeOf(Craft craft)
    {
        if (craft?.CraftProducts is not { Count: > 0 })
            return 0;

        var product = craft.CraftProducts[0];
        return product.UseGrade
            ? (byte)Math.Clamp(product.ItemGradeId, 0, byte.MaxValue)
            : (byte)0;
    }

    /// <summary>
    /// Materials one run consumes, scaled by how many runs the player asked for. Long, so an absurd
    /// count cannot wrap before it is rejected.
    /// </summary>
    public static long MaterialCost(int perCraft, uint count) => Math.Max(0, perCraft) * (long)count;

    /// <summary>A cast that asks for nothing, or for more material than a bag can hold, is refused.</summary>
    public static bool IsUsableCount(uint count) => count > 0;

    /// <summary>Whether the scaled material need fits a container's item count.</summary>
    public static bool IsConsumable(long need) => need is > 0 and <= int.MaxValue;

    /// <summary>
    /// Materials a restore hands back: the same bill making the sheet took, scaled by the sheet's
    /// count. Empty when the craft is gone or the bill would not fit a stack — the sheet can still
    /// be destroyed, it just returns nothing.
    /// </summary>
    public static IReadOnlyList<(uint ItemId, int Count)> RestoreMaterials(Craft craft, uint count)
    {
        if (craft?.CraftMaterials is not { Count: > 0 } || !IsUsableCount(count))
            return [];

        var bill = new List<(uint ItemId, int Count)>(craft.CraftMaterials.Count);
        foreach (var material in craft.CraftMaterials)
        {
            var need = MaterialCost(material.Amount, count);
            if (!IsConsumable(need))
                continue;

            bill.Add((material.ItemId, (int)need));
        }

        return bill;
    }

    /// <summary>
    /// The restore tab's material request names the sheet by its item instance id. The post
    /// dialog names a product type. A bag sheet is the restore case.
    /// </summary>
    public static bool RequestNamesASheet(Item bagItem) => bagItem is CraftOrderSheetItem;

    /// <summary>
    /// Material rows 0x237 shows for one craft: item type, grade 0, stack. Restore scales by
    /// the sheet count; the post dialog asks for one run.
    /// </summary>
    public static IReadOnlyList<CraftOrderMaterialRow> MaterialRows(Craft craft, uint count)
    {
        var bill = RestoreMaterials(craft, count);
        var rows = new List<CraftOrderMaterialRow>(bill.Count);
        foreach (var (itemId, n) in bill)
            rows.Add(new CraftOrderMaterialRow(itemId, 0, (uint)n));
        return rows;
    }
}
