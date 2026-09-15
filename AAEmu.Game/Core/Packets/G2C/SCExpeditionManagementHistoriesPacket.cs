using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions.Activities;

namespace AAEmu.Game.Core.Packets.G2C;

public sealed class SCExpeditionManagementHistoriesPacket(
    IReadOnlyCollection<ExpeditionManagementHistory> histories)
    : GamePacket(SCOffsets.SCExpeditionManagementHistoriesPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var rows = histories.Take(50).ToArray();
        stream.Write((byte)rows.Length);
        foreach (var row in rows)
            row.Write(stream);
        return stream;
    }
}
