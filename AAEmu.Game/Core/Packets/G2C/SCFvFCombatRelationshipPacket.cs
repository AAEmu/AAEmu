using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFvFCombatRelationshipPacket : GamePacket
    {
        private readonly (uint faction1, uint faction2, byte unitRelationshipCode, byte unitRelationshipReason)[] _relationships;

        public SCFvFCombatRelationshipPacket((uint faction1, uint faction2, byte unitRelationshipCode, byte unitRelationshipReason)[] relationships)
            : base(SCOffsets.SCFvFCombatRelationshipPacket, 5)
        {
            _relationships = relationships;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_relationships.Length);  // n
            foreach (var (faction1, faction2, unitRelationshipCode, unitRelationshipReason) in _relationships)
            {
                stream.Write(faction1);               // faction1 (type)
                stream.Write(faction2);               // faction2 (type)
                stream.Write(unitRelationshipCode);   // UnitRelationshipCode
                stream.Write(unitRelationshipReason); // UnitRelationshipReason
            }
            return stream;
        }
    }
}
