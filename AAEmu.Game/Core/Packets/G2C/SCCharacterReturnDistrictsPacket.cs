using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterReturnDistrictsPacket(Portal[] portals, uint returnDistrictId)
    : GamePacket(SCOffsets.SCCharacterReturnDistrictsPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(portals.Length);
        foreach (var portal in portals)
            stream.Write(portal);
        stream.Write(returnDistrictId);
        return stream;
    }
}
