using System;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemUpdateChargeUseSkillTime : ItemTask
    {
        private readonly ulong _itemId;
        private readonly SlotType _slotType;
        private readonly byte _slot;
        private readonly DateTime _chargeUseSkillTime;

        public ItemUpdateChargeUseSkillTime(Item item)
        {
            _type = ItemAction.UpdateChargeUseSkillTime; // 19

            _itemId = item.Id;
            _slotType = item.SlotType;
            _slot = (byte)item.Slot;
            _chargeUseSkillTime = item.ChargeUseSkillTime;
        }

        public ItemUpdateChargeUseSkillTime(ulong itemId, SlotType slotType, byte slot, DateTime chargeUseSkillTime)
        {
            _type = ItemAction.UpdateChargeUseSkillTime; // 19

            _itemId = itemId;
            _slotType = slotType;
            _slot = slot;
            _chargeUseSkillTime = chargeUseSkillTime;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write((byte)_slotType);
            stream.Write(_slot); // index
            stream.Write(_itemId);
            stream.Write(_chargeUseSkillTime);
            return stream;
        }
    }
}
