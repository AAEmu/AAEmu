using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCItemUccDataPacket : StreamPacket
    {
        private uint _playerId;
        private uint _count;
        private List<ulong> _itemIds ;
        
        public TCItemUccDataPacket(uint playerId, uint count, List<ulong> itemIds) : base(TCOffsets.TCItemUccDataPacket)
        {
            _playerId = playerId;
            _count = count;
            _itemIds = itemIds;
        }
        
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_playerId);
            stream.Write(_itemIds.Count);
            foreach (var itemId in _itemIds)
            {
                var item = ItemManager.Instance.GetItemByItemId(itemId);
                stream.Write(item.Id);
                stream.Write(item.UccId);
            }
            
            return stream;
        }
    }
}
