using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCPauseUserMusicPacket(uint playerObjId) : GamePacket(SCOffsets.SCPauseUserMusicPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(playerObjId);
        return stream;
    }
}
