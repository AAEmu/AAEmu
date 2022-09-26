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
        private readonly int _dwKeySize;
        private readonly short _pubKeySize;

        public X2EnterWorldResponsePacket(short reason, bool gm, uint token, ushort port, GameConnection connection) :
            base(SCOffsets.X2EnterWorldResponsePacket, 5)
        {
            _connection = connection;
            _reason = reason;
            _token = token;
            _port = port;
            _gm = gm;
            _dwKeySize = 1024;
            _pubKeySize = 260;
        }

        public override PacketStream Write(PacketStream stream)
        {
            //расшифрованные данные из снифа пакета
            //3.0.3.0
            // size hash crc idx opcode data
            //"2901 DD05 F2  00  0000   0000 00 4634FC94 E204 4D42535B00000000 4CFFFFFF 0401 0401 00040000 AFFB77BE14B5F0D8870389AE349D2ACB6AADE7426175217E2155D54A4C4D278B7B29FA00C3A1FDD8A2C7A344F111A5227E21B6F38105C7EB3C0E9748D3834EA2F0924B7B372B03ADDD41473194A7F0D5B242A15464680EC91B052334312623861051AE76E93936E462E9186A02199B6C6E16604CE5D51811A7CF75A3F35C3B390 000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000010001 773F8B05 CDC9"
            // 2901 DD05 3C  00  0000   0000 00 389416E0 E204 91E6305B00000000 4CFFFFFF 0401 0401 00040000 BBC0E9659E21640C4D689287322A627C63B8FD9EEDAF0C3999D14079393F023B1D6B032D574F2F787C814D90D137DAFD93E5577EDE35E1696A40B0DC031FB1D333E038A15163D278615FEFB9275D9FBD5B99E77F6890D8DA04F226267FCDC487E1A1DCAEB23A13399699B3617BF59C9DF85A81519C5093D61C5F44B8045FEEE90 000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000010001 FC73AE55 22ED
            //c пустым ключом pubkey не работает

            stream.Write(_reason);               // Reason 0
            stream.Write(_gm);                   // GM 0
            stream.Write(_token);                // SC 0
            stream.Write(_port);                 // SP 1250
            stream.Write(Helpers.UnixTimeNow()); // WF 0
            stream.Write(0xffffff4c);            // TZ 0
            stream.Write(_pubKeySize);           // H, Public Key Size  0401 (Should be 260)
            stream.Write(_pubKeySize);           // H, Pub key len (in pub key) 128 * 2 + 4 = 260
            stream.Write(_dwKeySize);            // 1024

            //----- RSA -----
            EncryptionManager.Instance.WriteKeyParams(_connection.Id, _connection.AccountId, stream);
            //----- RSA -----

            stream.Write((uint)0x0100007F); //NAT address
            stream.Write((ushort)25375); //NAT port

            return stream;
        }
    }
}
