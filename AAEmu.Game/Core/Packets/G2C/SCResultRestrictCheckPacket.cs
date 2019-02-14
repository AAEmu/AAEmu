using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCResultRestrictCheckPacket : GamePacket
    {
        private readonly uint _characterId;
        private readonly byte _code;
        private readonly byte _result;
        
        public SCResultRestrictCheckPacket(uint characterId, byte code, byte result) : base(0x1cc, 1) // TODO 1.0 opcode: 0x1c4
        {
            _characterId = characterId;
            _code = code;
            _result = result;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_characterId);
            stream.Write(_code);
            stream.Write(_result);
            return stream;
        }
    }
}
