using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Opens the target's team-summon confirmation frame: summoner name, team id, zone and position.
/// The client dialog reads only the name, the zone id and the position.
/// </summary>
public class SCTeamSummonSuggestPacket(string name, uint @type, uint zoneId, float posX, float posY, float posZ) : GamePacket(SCOffsets.SCTeamSummonSuggestPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(name);
        stream.Write(@type);
        stream.Write(zoneId);
        stream.Write(posX);
        stream.Write(posY);
        stream.Write(posZ);
        return stream;
    }
}
