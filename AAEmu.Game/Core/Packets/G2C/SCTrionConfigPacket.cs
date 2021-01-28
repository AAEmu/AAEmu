using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTrionConfigPacket : GamePacket
    {
        private readonly bool _activate;
        private readonly string _wikiUrl;
        private readonly string _platformUrl;
        private readonly string _commerceUrl;
        private readonly string _csUrl;

        public SCTrionConfigPacket(bool activate, string platformUrl, string commerceUrl, string wikiUrl, string csUrl)
            : base(SCOffsets.SCTrionConfigPacket, 5)
        {
            _activate = activate;
            _wikiUrl = wikiUrl;
            _platformUrl = platformUrl;
            _commerceUrl = commerceUrl;
            _csUrl = csUrl;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_activate);
            stream.Write(_platformUrl);
            stream.Write(_commerceUrl);
            stream.Write(_wikiUrl);
            stream.Write(_csUrl);
            return stream;
        }
    }
}
