using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCConflictZoneHonorPointSumPacket : GamePacket
    {
        private readonly ushort _zoneId;
        private readonly int _sum;

        public SCConflictZoneHonorPointSumPacket(ushort zoneId, int sum) : base(SCOffsets.SCConflictZoneHonorPointSumPacket, 5)
        {
            _zoneId = zoneId;
            _sum = sum;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_zoneId);
            stream.Write(_sum);
            return stream;
        }
    }
}
