using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharacterGamePointsPacket : GamePacket
    {
        public SCCharacterGamePointsPacket() : base(0x180, 1)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            for (var i = 0; i < 10; i++)
                stream.Write(i); // p
            return stream;
        }
    }
}