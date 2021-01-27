using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCProcessingInstancePacket : GamePacket
    {
        private readonly int _zoneId;

        public SCProcessingInstancePacket(int zoneId) : base(SCOffsets.SCProcessingInstancePacket, 5)
        {
            _zoneId = zoneId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_zoneId);
            return stream;
        }
    }
}
