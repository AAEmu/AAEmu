using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions.Activities;

namespace AAEmu.Game.Core.Packets.G2C;

public sealed class SCExpeditionPortalSavedPacket(uint expeditionId, ExpeditionPortalPoint portal)
    : GamePacket(SCOffsets.SCExpeditionPortalSavedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(expeditionId);
        portal.Write(stream);
        return stream;
    }
}
