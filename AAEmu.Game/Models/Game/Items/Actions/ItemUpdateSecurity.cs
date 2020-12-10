using System;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemUpdateSecurity : ItemTask
    {
        private readonly Item _item;
        private readonly byte _bits;
        private readonly bool _isUnsecureExcess;
        private readonly bool _isUnsecureSet;
        private readonly bool _isUnpack;

        public ItemUpdateSecurity(Item item, byte bits, bool isUnsecureExcess, bool isUnsecureSet, bool isUnpack)
        {
            _item = item;
            _bits = bits;
            _isUnsecureExcess = isUnsecureExcess;
            _isUnsecureSet = isUnsecureSet;
            _isUnpack = isUnpack;
            _type = ItemAction.UpdateFlags; // 11
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write((byte)_item.SlotType);
            stream.Write((byte)_item.Slot);
            stream.Write(_item.Id);
            stream.Write(_bits);
            stream.Write(_isUnsecureExcess);
            stream.Write(_isUnsecureSet);
            stream.Write(_isUnpack);
            stream.Write(_item.UnsecureTime);
            stream.Write(_item.UnpackTime);
            return stream;
        }
    }
}
