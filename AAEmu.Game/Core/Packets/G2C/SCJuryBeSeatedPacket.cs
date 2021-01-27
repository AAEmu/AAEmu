using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
   public class SCJuryBeSeatedPacket : GamePacket
    {
        private readonly bool _isWest;
        private readonly uint _trial;
        private readonly int _court;
        private readonly int _juryNumber;

        public SCJuryBeSeatedPacket(bool isWest, uint trial, int court, int juryNumber) : base(SCOffsets.SCJuryBeSeatedPacket, 5)
        {
            _isWest = isWest;
            _trial = trial;
            _court = court;
            _juryNumber = juryNumber;
        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_isWest);
            stream.Write(_trial);
            stream.Write(_court);
            stream.Write(_juryNumber);
            return stream;
        }
    }
}
