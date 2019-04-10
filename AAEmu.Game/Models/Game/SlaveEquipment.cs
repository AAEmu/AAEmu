using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game
{
    public class SlaveEquipment : PacketMarshaler
    { 
        public uint Id { get; set; }
        public ushort Tl { get; set; }
        public uint DbSlaveId { get; set; }
        public bool Bts { get; set; }
        public Item Item { get; set; }
        public SlotType SlotType { get; set; }
        public byte Slot { get; set; }
        public Item Item2 { get; set; }
        public SlotType SlotType2 { get; set; }
        public byte Slot2 { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Tl);
            stream.Write(DbSlaveId);
            stream.Write(Bts);
            stream.Write(Item);
            stream.Write(Item2);
            
            stream.Write((byte)0);
            stream.Write((byte)SlotType);
            stream.Write((byte)0);
            stream.Write(Slot);
            
            stream.Write((byte)0);
            stream.Write((byte)SlotType2);
            stream.Write((byte)0);
            stream.Write(Slot2);
            return stream;
        }
    }
}
