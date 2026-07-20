using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCItemUccDataChangedPacket(ulong uccId, uint playerId, ulong targetItemId)
    : GamePacket(SCOffsets.SCItemUccDataChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(uccId);
        stream.Write(playerId);
        stream.Write(targetItemId);

        return stream;
    }
}
