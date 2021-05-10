using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFestivalZoneStatePacket : GamePacket
    {
        private readonly ushort _time;

        public SCFestivalZoneStatePacket(float time) : base(SCOffsets.SCFestivalZoneStatePacket, 5)
        {
            _time = (ushort)time;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_time);

            return stream;
        }
    }
}
