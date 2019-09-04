using System.Collections.Generic;
using AAEmu.Commons.Models;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Login;

namespace AAEmu.Game.Core.Packets.G2L
{
    public class GLRequestInfoPacket : LoginPacket
    {
        private readonly uint _connectionId;
        private readonly uint _requestId;
        private readonly List<LoginCharacterInfo> _characters;

        public GLRequestInfoPacket(uint connectionId, uint requestId, List<LoginCharacterInfo> characters) : base(0x03)
        {
            _connectionId = connectionId;
            _requestId = requestId;
            _characters = characters;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_connectionId);
            stream.Write(_requestId);
            stream.Write(_characters);
            return stream;
        }
    }
}
