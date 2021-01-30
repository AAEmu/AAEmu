using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLevelRestrictionConfigPacket : GamePacket
    {
        private readonly byte _searchLevel;
        private readonly byte _bidLevel;
        private readonly byte _postLevel;
        private readonly byte _trade;
        private readonly byte _mail;
        private readonly byte[] _limitLevels;

        public SCLevelRestrictionConfigPacket(byte searchLevel, byte bidLevel, byte postLevel, byte trade, byte mail, byte[] limitLevels)
            : base(SCOffsets.SCLevelRestrictionConfigPacket, 5)
        {
            _searchLevel = searchLevel;
            _bidLevel = bidLevel;
            _postLevel = postLevel;
            _trade = trade;
            _mail = mail;
            _limitLevels = limitLevels;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_searchLevel);
            stream.Write(_bidLevel);
            stream.Write(_postLevel);
            stream.Write(_trade);
            stream.Write(_mail);
            for (var i = 0; i < 15; i++) // in 1.2 = 15, in 1.7 = 15, in 3.5 = 17
            {
                stream.Write(_limitLevels[i]);
            }
            return stream;
        }
    }
}
