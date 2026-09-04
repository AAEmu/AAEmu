using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Charge-lifetime gear (fishing rods, event weapons) names its reagent on
/// <see cref="EquipItemTemplate.RechargeRestrictItemId"/> and writes the new window onto the
/// piece. The client must hear that write as a detail packet, not an <c>UpdateDetail</c> task.
/// </summary>
public static class ItemChargeRules
{
    public enum RechargeApply
    {
        Applied,
        Rejected,
        Equipped
    }

    /// <summary>
    /// Starts a new charge window when the source item is the piece's named reagent and the
    /// piece is not sitting in an equipment slot.
    /// </summary>
    public static RechargeApply TryApply(EquipItem equipItem, Item sourceItem, DateTime time)
    {
        if (equipItem?.Template is not EquipItemTemplate template ||
            template.RechargeRestrictItemId == 0 ||
            sourceItem == null ||
            template.RechargeRestrictItemId != sourceItem.TemplateId)
            return RechargeApply.Rejected;

        if (equipItem.SlotType == SlotType.Equipment)
            return RechargeApply.Equipped;

        equipItem.ChargeStartTime = time;
        equipItem.ChargeCount = template.ChargeCount;
        return RechargeApply.Applied;
    }
}
