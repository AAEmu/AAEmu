using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Duels;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDuelEndedPacket(
    uint challengerId,
    uint challengedId,
    uint challengerObjId,
    uint challengedObjId,
    DuelDetType det)
    : GamePacket(SCOffsets.SCDuelEndedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(challengerId);         // challengerId
        stream.Write(challengedId);        // challengedId
        stream.WriteBc(challengerObjId);  // challengerObjId
        stream.WriteBc(challengedObjId); // challengedObjId
        stream.Write((byte)det);        // det 00=lose, 01=win, 02=surrender, 3=draw

        return stream;
    }
}
