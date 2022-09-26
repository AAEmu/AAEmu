using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharacterCreationFailedPacket : GamePacket
    {
        private readonly byte _reason;

        public SCCharacterCreationFailedPacket(byte reason) : base(SCOffsets.SCCharacterCreationFailedPacket, 5)
        {
            _reason = reason;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_reason);
            return stream;
        }
    }
}
