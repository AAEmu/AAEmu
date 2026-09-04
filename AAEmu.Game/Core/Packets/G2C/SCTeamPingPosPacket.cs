using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamPingPosPacket(
    uint teamId,
    bool hasPing,
    WorldSpawnPosition position,
    uint insId,
    byte setPingType = 1)
    : GamePacket(SCOffsets.SCTeamPingPosPacket, 1)
{
    /// <summary>Legacy ctor used before teamId was on the wire — teamId 0 (solo / local echo).</summary>
    public SCTeamPingPosPacket(bool hasPing, WorldSpawnPosition position, uint insId)
        : this(0, hasPing, position, insId)
    {
    }

    public override PacketStream Write(PacketStream stream)
    {
        TeamPingPosWire.Write(stream, teamId, hasPing ? setPingType : (byte)0, hasPing, position, insId);
        return stream;
    }
}
