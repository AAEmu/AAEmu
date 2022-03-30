using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCJuryPointChangedPacket : GamePacket
    {
        private readonly int _juryPoint;

        public SCJuryPointChangedPacket(int juryPoint) : base(SCOffsets.SCJuryPointChangedPacket, 5)
        {
            _juryPoint = juryPoint;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_juryPoint);
            return stream;
        }
    }
}
