namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Wire bodies differ by case: 4 = template+amount only; 5 = slot+id+amount+template;
/// 6/7/0x10 = slot + full Item; 9 = SwapSlot; 8 = reservation-style.
/// </summary>
public enum ItemAction
{
    Invalid = 0,
    ChangeMoneyAmount = 1,
    ChangeBankMoneyAmount = 2,
    ChangeGamePoint = 3,
    AddStack = 4,
    Create = 5, // compact: slotType, slot, itemId, amount, templateId
    Take = 6,   // full Item after slot
    Remove = 7, // full Item after slot (same wire as Take)
    RemoveReservation = 8,
    SwapSlot = 9,
    UpdateDetail = 10,
    SetFlagsBits = 11,
    UpdateFlags = 12,
    RemoveCrafting = 13,
    Seize = 14,
    ChangeGrade = 15,
    ChangeOwner = 16,
    ChangeAaPoint = 17,
    ChangeBankAaPoint = 18,
    ChangeAutoUseAaPoint = 19,
    UpdateChargeUseSkillTime = 20,
}
