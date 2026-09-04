using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Squad;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Board page. The client reads the outer fields on its network thread and defers the nested
/// blob to the main thread, where payload = u32 count + entries. See NestedBlobWire.
/// </summary>
public class SCSelectSquadListPacket(uint available, uint curPage, IReadOnlyList<SquadListEntry> entries)
    : GamePacket(SCOffsets.SCSelectSquadListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(available);
        stream.Write(curPage);

        var payload = new PacketStream();
        var list = entries ?? [];
        payload.Write((uint)list.Count);
        foreach (var entry in list)
            entry.Write(payload);

        NestedBlobWire.Write(stream, payload);
        return stream;
    }
}
