using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCombatTextPacket(uint sourceUnitId, uint targetUnitId, byte textType)
    : GamePacket(SCOffsets.SCCombatTextPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(sourceUnitId);
        stream.WriteBc(targetUnitId);
        stream.Write(textType);
        return stream;
    }
}
