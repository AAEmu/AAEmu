using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game;

public class SlaveEquipment : PacketMarshaler
{
    public uint Id { get; set; } // characterId
    public ushort Tl { get; set; }
    public uint DbSlaveId { get; set; }
    public bool Bts { get; set; }
    public Item Item { get; set; }
    public SlotType SlotType { get; set; }
    public byte Slot { get; set; }
    public Item Item2 { get; set; }
    public SlotType SlotType2 { get; set; }
    public byte Slot2 { get; set; }

    private readonly List<(Item, Item)> items;
    private readonly List<(SlotType, byte)> fromSlots;
    private readonly List<(SlotType, byte)> toSlots;


    public SlaveEquipment()
    {
        items = new List<(Item, Item)>();
        fromSlots = new List<(SlotType, byte)>();
        toSlots = new List<(SlotType, byte)>();
    }

    public override void Read(PacketStream stream)
    {
        Id = stream.ReadUInt32(); // EquipmentItemSlotType
        Tl = stream.ReadUInt16();
        DbSlaveId = stream.ReadUInt32();
        Bts = stream.ReadBoolean();
        var num = stream.ReadByte();
        if (num == 1)
        {
            Item.Read(stream);
            Item2.Read(stream);
            SlotType = (SlotType)stream.ReadByte();   // type
            Slot = stream.ReadByte();                 // index
            SlotType2 = (SlotType)stream.ReadByte();  // type
            Slot2 = stream.ReadByte();                // index
        }
        else
        {
            for (var i = 0; i < num; i++)
            {
                // read item1
                var item1 = new Item();
                item1.Read(stream);
                // read item2
                var item2 = new Item();
                item2.Read(stream);
                items.Add((item1, item2));

                var slotType1 = (SlotType)stream.ReadByte();   // type
                var slot1 = stream.ReadByte();            // index
                fromSlots.Add((slotType1, slot1));

                var slotType2 = (SlotType)stream.ReadByte();  // type
                var slot2 = stream.ReadByte();           // index
                toSlots.Add((slotType2, slot2));
            }
        }
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Id);
        stream.Write(Tl);
        stream.Write(DbSlaveId);
        stream.Write(Bts);
        stream.Write((byte)items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Item1.Write(stream);
            items[i].Item2.Write(stream);

            stream.Write((byte)fromSlots[i].Item1); // type
            stream.Write(fromSlots[i].Item2);       // index

            stream.Write((byte)toSlots[i].Item1); // type
            stream.Write(toSlots[i].Item2);       // index
        }
        return stream;
    }
}
