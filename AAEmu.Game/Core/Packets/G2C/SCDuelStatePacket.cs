using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDuelStatePacket(
    uint unitObjId,
    uint duelStateObjId,
    sbyte duelTeamType,
    bool isEjected = false) : GamePacket(SCOffsets.SCDuelStatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitObjId);
        stream.WriteBc(duelStateObjId);
        stream.Write(duelTeamType);
        stream.Write(isEjected);

        return stream;
    }
}
