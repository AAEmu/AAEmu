using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// </remarks>
public class SCGimmickResetJointsPacket(uint gimmickId) : GamePacket(SCOffsets.SCGimmickResetJointsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(gimmickId);
        return stream;
    }
}
