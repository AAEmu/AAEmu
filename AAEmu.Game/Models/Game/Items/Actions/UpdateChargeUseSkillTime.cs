using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class UpdateChargeUseSkillTime : ItemTask
{
    private readonly Item _item;
    private readonly int _count;

    /// <summary>
    /// Add or subtracts count from the item count of a given item
    /// </summary>
    /// <param name="item">Item to update</param>
    /// <param name="count">Amount to add or subtract</param>
    public UpdateChargeUseSkillTime(Item item, int count)
    {
        _type = ItemAction.UpdateChargeUseSkillTime; // 19
        _item = item;
        _count = count;
        _tLogt = SetTlogT(_type, SlotType.Bag); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write((byte)_item.SlotType); // type
        stream.Write((byte)_item.Slot);     // index
        stream.Write(_item.Id);             // itemId
        stream.Write(_item.ChargeUseSkillTime); // chargeUseSkillTime
        return stream;
    }
}
