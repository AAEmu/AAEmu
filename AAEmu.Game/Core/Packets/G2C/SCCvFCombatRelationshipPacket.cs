using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCvFCombatRelationshipPacket(
    (long x, byte unitRelationshipCode, byte unitRelationshipReason)[] relationships)
    : GamePacket(SCOffsets.SCCvFCombatRelationshipPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)relationships.Length);
        foreach (var (x, unitRelationshipCode, unitRelationshipReason) in relationships)
        {
            stream.Write(x);
            stream.Write(unitRelationshipCode);
            stream.Write(unitRelationshipReason);
        }

        return stream;
    }
}
