using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions.Activities;

namespace AAEmu.Game.Core.Packets.G2C;

public sealed class SCExpeditionPortalsPacket(uint expeditionId,
    IReadOnlyCollection<ExpeditionPortalPoint> portals) : GamePacket(SCOffsets.SCExpeditionPortalsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(expeditionId);
        stream.Write(portals.Count);
        foreach (var portal in portals.OrderBy(x => x.Id))
        {
            stream.Write(portal.Id); // native std::map key
            portal.Write(stream);
        }
        return stream;
    }
}
