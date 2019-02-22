using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTrionConfigPacket : GamePacket
    {
        private readonly bool _activate;
        private readonly string _authUrl;
        private readonly string _platformUrl;
        private readonly string _commerceUrl;
        
        public SCTrionConfigPacket(bool activate, string authUrl, string platformUrl, string commerceUrl) : base(SCOffsets.SCTrionConfigPacket, 1)
        {
            _activate = activate;
            _authUrl = authUrl;
            _platformUrl = platformUrl;
            _commerceUrl = commerceUrl;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_activate);
            stream.Write(_authUrl);
            stream.Write(_platformUrl);
            stream.Write(_commerceUrl);
            return stream;
        }
    }
}
