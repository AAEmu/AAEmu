using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMateEquipmentChangedPacket : GamePacket
    {

        public readonly Item _item;
        public readonly SlotType _typea;
        public readonly SlotType _typeb;
        public readonly byte _slota;
        public readonly byte _slotb;
        public readonly ushort _tlId;

        public SCMateEquipmentChangedPacket(Item item, SlotType typeA, byte slotA, SlotType typeB, byte slotB, ushort tlId) : base(SCOffsets.SCMateEquipmentChangedPacket, 1)
        {
            _item = item;
            _typea = typeA;
            _typeb = typeB;
            _slota = slotA;
            _slotb = slotB;
            _tlId = tlId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(0);
            stream.Write(_tlId);
            stream.Write(1);
            stream.Write(true);
            stream.Write((byte)1);
            stream.Write(_item);
            stream.Write((byte)_typea);
            stream.Write((byte)_slota);
            stream.Write((byte)_typeb);
            stream.Write((byte)_slotb);
            return stream;
        }
    }
}
