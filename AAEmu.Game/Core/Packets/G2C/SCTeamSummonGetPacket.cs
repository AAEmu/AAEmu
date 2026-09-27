using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public sealed class SCTeamSummonGetPacket(IReadOnlyCollection<uint> characterIds)
    : GamePacket(SCOffsets.SCTeamSummonGetPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(characterIds.Count);
        foreach (var id in characterIds)
            stream.Write((long)id);
        return stream;
    }
}
