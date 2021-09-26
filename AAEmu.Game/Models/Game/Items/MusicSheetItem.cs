using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    public class MusicSheetItem : Item
    {
        public override ItemDetailType DetailType => ItemDetailType.MusicSheet;
        public uint SongId { get; set; }

        public MusicSheetItem()
        {
            
        }
        
        public MusicSheetItem(ulong id, ItemTemplate template, int count) : base(id, template, count)
        {
        }
        
        public override void ReadDetails(PacketStream stream)
        {
            var id64 = stream.ReadUInt64();
            SongId = (uint)id64;
        }

        public override void WriteDetails(PacketStream stream)
        {
            stream.Write((ulong)SongId);
        }
    }
}
