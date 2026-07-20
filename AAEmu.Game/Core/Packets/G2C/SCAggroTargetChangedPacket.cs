using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAggroTargetChangedPacket(uint npcId, uint targetId) : GamePacket(SCOffsets.SCAggroTargetChanged, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(npcId);
        stream.WriteBc(targetId);

        return stream;
    }
}
