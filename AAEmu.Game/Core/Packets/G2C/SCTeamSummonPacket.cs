using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public sealed class SCTeamSummonPacket() : GamePacket(SCOffsets.SCTeamSummonPacket, 1)
{
    public override PacketStream Write(PacketStream stream) => stream;
}
