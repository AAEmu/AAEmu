using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class X2EnterWorldResponsePacket : GamePacket
    {
        private readonly GameConnection _connection;
        private readonly short _reason;
        private readonly uint _token;
        private readonly ushort _port;
        private readonly bool _gm;
        private readonly short _pubKeySize;
        private readonly int _dwKeySize;
        private readonly int _authority = 1;

        public X2EnterWorldResponsePacket(short reason, bool gm, uint token, ushort port, GameConnection connection) :
            base(SCOffsets.X2EnterWorldResponsePacket, 5)
        {
            _connection = connection;
            _reason = reason;
            _token = token;
            _port = port;
            _gm = gm;
            _pubKeySize = 128 * 2 + 4; // = 260;
            _dwKeySize = 1024;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_reason);               // Reason 0
            //stream.Write(_gm);                   // GM 0
            stream.Write(_token);                // SC 0
            stream.Write(_port);                 // SP 1250
            stream.Write(Helpers.UnixTimeNow()); // WF 0
            stream.Write((uint)0xffffff4c);      // TZ 0
            stream.Write(_pubKeySize);           // H, Public Key Size  0401 (Should be 260)
            stream.Write(_pubKeySize);           // length of data directly in pubkey
            stream.Write(_dwKeySize);            // 1024

            //----- pubKey -----
            EncryptionManager.Instance.WritePubKey(_connection.Id, _connection.AccountId, stream);
            //----- pubKey -----

            stream.Write((uint)0x0100007F); //NAT address
            stream.Write((ushort)25375);    //NAT port
            stream.Write(_authority);       // authority 1 this field is in 3.5.5.3, but not in 3.0.3.0

            _log.Warn("GamePacket: S->C X2EnterWorldResponsePacket");

            return stream;
        }
    }
}
