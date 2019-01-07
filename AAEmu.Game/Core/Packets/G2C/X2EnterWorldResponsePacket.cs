using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class X2EnterWorldResponsePacket : GamePacket
    {
        private readonly short _reason;
        private readonly uint _token;
        private readonly ushort _port;
        private readonly bool _gm;

        public X2EnterWorldResponsePacket(short reason, bool gm, uint token, ushort port) : base(0x000, 1)
        {
            _reason = reason;
            _token = token;
            _port = port;
            _gm = gm;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_reason);
            stream.Write(_gm);
            stream.Write(_token); // Stream Token
            stream.Write(_port); // Stream Port
            stream.Write(Helpers.UnixTimeNow());
            return stream;
        }
    }
}