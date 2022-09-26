using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionRetryRenamePacket : GamePacket
    {
        private readonly bool _xpdt;
        private readonly short _errorMessage;
        private readonly string _name;
        
        public SCFactionRetryRenamePacket(bool xpdt, short errorMessage, string name) : base(SCOffsets.SCFactionRetryRenamePacket, 5)
        {
            _xpdt = xpdt;
            _errorMessage = errorMessage;
            _name = name;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_xpdt);
            stream.Write(_errorMessage);
            stream.Write(_name);
            return stream;
        }
    }
}
