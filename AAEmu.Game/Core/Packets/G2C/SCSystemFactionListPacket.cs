using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSystemFactionListPacket : GamePacket
    {
        private readonly SystemFaction[] _factions;

        public SCSystemFactionListPacket() : base(SCOffsets.SCSystemFactionListPacket, 5)
        {
            _factions = new SystemFaction[] { };
        }

        public SCSystemFactionListPacket(SystemFaction[] factions) : base(SCOffsets.SCSystemFactionListPacket, 5)
        {
            _factions = factions;
        }

        public SCSystemFactionListPacket(SystemFaction faction) : base(SCOffsets.SCSystemFactionListPacket, 5)
        {
            _factions = new[] { faction };
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_factions.Length); // count
            foreach (var faction in _factions)
            {
                stream.Write(faction);
            }

            return stream;
        }
    }
}
