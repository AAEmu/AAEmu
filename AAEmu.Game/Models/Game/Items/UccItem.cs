using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    public class UccItem : Item
    {
        public override ItemDetailType DetailType => ItemDetailType.Ucc;

        public UccItem()
        {
            
        }
        
        public UccItem(ulong id, ItemTemplate template, int count) : base(id, template, count)
        {
        }
        
        public override void ReadDetails(PacketStream stream)
        {
            UccId = stream.ReadUInt64();
        }

        public override void WriteDetails(PacketStream stream)
        {
            stream.Write(UccId);
            stream.Write((byte)0);
        }
    }
}
