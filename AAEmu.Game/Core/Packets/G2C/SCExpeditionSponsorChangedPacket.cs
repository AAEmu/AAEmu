using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionSponsorChangedPacket(SystemFaction faction, bool success)
    : GamePacket(SCOffsets.SCExpeditionSponsorChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(faction);
        stream.Write(success);
        return stream;
    }
}
