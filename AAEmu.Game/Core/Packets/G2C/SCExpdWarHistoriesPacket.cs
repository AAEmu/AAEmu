using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions.Activities;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCExpdWarHistoriesPacket(IReadOnlyCollection<ExpeditionWarHistory> histories) : GamePacket(SCOffsets.SCExpdWarHistoriesPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var rows = histories.Take(50).ToArray();
        stream.Write((uint)rows.Length);
        foreach (var row in rows)
            row.Write(stream);
        return stream;
    }
}
