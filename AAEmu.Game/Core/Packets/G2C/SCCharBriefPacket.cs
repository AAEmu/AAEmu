using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharBriefPacket : GamePacket
    {
        private readonly uint _charId;
        private readonly string _name;
        private readonly Race _race;

        public SCCharBriefPacket(uint charId, string name, Race race) : base(SCOffsets.SCCharBriefPacket, 5)
        {
            _charId = charId;
            _name = name;
            _race = race;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_charId);
            stream.Write(_name);
            stream.Write((byte)_race);
            return stream;
        }
    }
}
