using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C;

#pragma warning disable IDE0052 // Remove unread private members

public class TCItemUccDataPacket(uint playerId, uint count, List<ulong> itemIds)
    : StreamPacket(TCOffsets.TCItemUccDataPacket)
{
    private uint _count = count;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(playerId);
        stream.Write(itemIds.Count);
        foreach (var itemId in itemIds)
        {
            var item = ItemManager.Instance.GetItemByItemId(itemId);
            stream.Write(item.Id);
            stream.Write(item.UccId);
        }

        return stream;
    }
}
