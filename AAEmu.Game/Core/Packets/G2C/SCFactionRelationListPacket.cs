using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionRelationListPacket : GamePacket
    {
        private readonly FactionRelation[] _relations;

        public SCFactionRelationListPacket() : base(SCOffsets.SCFactionRelationListPacket, 5)
        {
            _relations = new FactionRelation[] { };
        }

        public SCFactionRelationListPacket(FactionRelation[] relations) : base(SCOffsets.SCFactionRelationListPacket, 5)
        {
            _relations = relations;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(false); // uiRequest
            stream.Write((byte)_relations.Length); // count
            // TODO in 1.2 max length 200
            foreach (var relation in _relations)
            {
                stream.Write(relation.Id);
                stream.Write(relation.Id2);
                stream.Write((byte)relation.State);
                stream.Write(relation.ExpTime);
                stream.Write(0L);       // type(id)
                stream.Write((byte)0); // nState
            }

            return stream;
        }
    }
}
