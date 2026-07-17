using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDoodadCreatedPacket(Doodad doodad) : GamePacket(SCOffsets.SCDoodadCreatedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        return doodad.Write(stream);
    }
}
