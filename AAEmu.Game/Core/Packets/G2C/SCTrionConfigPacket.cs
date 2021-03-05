using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTrionConfigPacket : GamePacket
    {
        private readonly bool _activate;
        //private readonly string _wikiUrl; // missing in version 1.8
        private readonly string _platformUrl;
        private readonly string _commerceUrl;
        //private readonly string _csUrl; // missing in version 1.8

        public SCTrionConfigPacket(bool activate, string platformUrl, string commerceUrl, string wikiUrl, string csUrl)
            : base(SCOffsets.SCTrionConfigPacket, 5)
        {
            _activate = activate;
            //_wikiUrl = wikiUrl; // missing in version 1.8
            _platformUrl = platformUrl;
            _commerceUrl = commerceUrl;
            //_csUrl = csUrl; // missing in version 1.8
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_activate);
            stream.Write(_platformUrl);
            stream.Write(_commerceUrl);
            //stream.Write(_wikiUrl); // missing in version 1.8
            //stream.Write(_csUrl); // missing in version 1.8
            return stream;
        }
    }
}
