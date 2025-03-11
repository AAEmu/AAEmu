using System;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public abstract class ItemTask : PacketMarshaler
{
    protected ItemAction _type;
    protected ItemTaskLogType _tLogt;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_type);  // tasks
        stream.Write((byte)_tLogt); // tLogt
        return stream;
    }

    public ItemTaskLogType SetTlogT(ItemAction itemTask, SlotType slotType, bool added = true)
    {
        var tlogT = ItemTaskLogType.UpdateOnly;
        switch (itemTask)
        {
            case ItemAction.Invalid: // 0
                break;
            case ItemAction.ChangeMoneyAmount: // 1
                tlogT = ItemTaskLogType.UpdateOnly;
                break;
            case ItemAction.ChangeBankMoneyAmount: // 2
                tlogT = ItemTaskLogType.UpdateOnly;
                break;
            case ItemAction.ChangeGamePoint: // 3
                tlogT = ItemTaskLogType.UpdateOnly;
                break;
            case ItemAction.AddStack when added == true: // 4
                tlogT = ItemTaskLogType.MoveItem; // если добавили
                break;
            case ItemAction.AddStack when added == false: // 4
                tlogT = ItemTaskLogType.RemoveItem; // если убавили
                break;
            case ItemAction.Create: // 5
                tlogT = ItemTaskLogType.GainItem;
                break;
            case ItemAction.Take: // 6
                tlogT = ItemTaskLogType.MoveItem;
                break;
            case ItemAction.Remove when slotType == SlotType.Bag: // 7
                tlogT = ItemTaskLogType.RemoveItem;
                break;
            case ItemAction.Remove when slotType == SlotType.Equipment: // 7
                tlogT = ItemTaskLogType.Place;
                break;
            case ItemAction.SwapSlot when slotType == SlotType.Bank: // 8
                tlogT = ItemTaskLogType.MoveItem;
                break;
            case ItemAction.SwapSlot when slotType == SlotType.Bag: // 8
                tlogT = ItemTaskLogType.SwapItem;
                break;
            case ItemAction.UpdateDetail: // 9
                tlogT = ItemTaskLogType.UpdateOnly;
                break;
            case ItemAction.SetFlagsBits: // 10
                break;
            case ItemAction.UpdateFlags: // 11
                break;
            case ItemAction.RemoveCrafting: // 12
                break;
            case ItemAction.Seize: // 13
                break;
            case ItemAction.ChangeGrade: // 14
                break;
            case ItemAction.ChangeOwner: // 15
                break;
            case ItemAction.ChangeAaPoint: // 16
                break;
            case ItemAction.ChangeBankAaPoint: // 17
                break;
            case ItemAction.ChangeAutoUseAaPoint: // 18
                break;
            case ItemAction.UpdateChargeUseSkillTime: // 19
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(itemTask), itemTask, null);
        }

        return tlogT;
    }
}
