using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamPingPosPacket(bool hasPing, WorldSpawnPosition position, uint insId)
    : GamePacket(SCOffsets.SCTeamPingPosPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(hasPing);
        stream.Write(position.X);
        stream.Write(position.Y);
        stream.Write(position.Z);
        stream.Write(insId);
        return stream;
    }
}
