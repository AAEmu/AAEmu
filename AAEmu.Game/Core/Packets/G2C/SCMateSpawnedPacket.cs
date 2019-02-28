using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mate;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMateSpawnedPacket : GamePacket
    {
        private readonly MateTemplate _mateTemplate;

        public SCMateSpawnedPacket(MateTemplate mateTemplate) : base(SCOffsets.SCMateSpawnedPacket, 1)
        {
            _mateTemplate = mateTemplate;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mateTemplate.TlId);
            stream.Write(_mateTemplate);
            return stream;
        }
    }
}
