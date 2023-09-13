using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFvFCombatRelationshipPacket : GamePacket
    {
        private readonly (long x, byte unitRelationshipCode, byte unitRelationshipReason)[] _relationships;

        public SCFvFCombatRelationshipPacket((long x, byte unitRelationshipCode, byte unitRelationshipReason)[] relationships)
            : base(SCOffsets.SCFvFCombatRelationshipPacket, 1)
        {
            _relationships = relationships;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_relationships.Length);
            foreach (var (x, unitRelationshipCode, unitRelationshipReason) in _relationships)
            {
                stream.Write(x);
                stream.Write(unitRelationshipCode);
                stream.Write(unitRelationshipReason);
            }
            return stream;
        }
    }
}