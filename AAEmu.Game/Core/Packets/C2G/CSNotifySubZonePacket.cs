using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSNotifySubZonePacket() : GamePacket(CSOffsets.CSNotifySubZonePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var subZoneId = stream.ReadUInt32();
        if (subZoneId == 0) return;

        Connection.ActiveChar.SubZoneId = subZoneId; // needed to store Memory Tome points for Recall

        Logger.Info($"Enter RegionId: {subZoneId} by {Connection.ActiveChar.Name} ({Connection.ActiveChar.Id})");
        Connection.ActiveChar.Portals.NotifySubZone(subZoneId);
    }
}
