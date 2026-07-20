using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCombatFirstHitPacket(uint vuId, uint huId, uint htId) : GamePacket(SCOffsets.SCCombatFirstHitPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(vuId);
        stream.WriteBc(huId);
        stream.Write(htId);
        return stream;
    }
}
