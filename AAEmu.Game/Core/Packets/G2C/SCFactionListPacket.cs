using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionListPacket : GamePacket
    {
        private readonly SystemFaction[] _factions;

        public SCFactionListPacket() : base(0x007, 1)
        {
            _factions = new SystemFaction[] { };
        }

        public SCFactionListPacket(SystemFaction[] factions) : base(SCOffsets.SCFactionListPacket, 1)
        {
            _factions = factions;
        }

        public SCFactionListPacket(SystemFaction faction) : base(SCOffsets.SCFactionListPacket, 1)
        {
            _factions = new[] {faction};
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte) _factions.Length);
            foreach (var faction in _factions)
                stream.Write(faction);

            return stream;
        }
    }
}
