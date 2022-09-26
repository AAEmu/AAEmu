using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnlockCurrencySlotPacket : GamePacket
    {
        private readonly SlotType _slotType;
        private readonly byte _slot;

        public SCUnlockCurrencySlotPacket(SlotType slotType, byte slot) : base(SCOffsets.SCUnlockCurrencySlotPacket, 5)
        {
            _slotType = slotType;
            _slot = slot;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_slotType);
            stream.Write(_slot);
            return stream;
        }
    }
}
