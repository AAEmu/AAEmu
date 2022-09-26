using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCPlaytimePacket : GamePacket
    {
        private readonly int _playTime;

        public SCPlaytimePacket(int playTime) : base(SCOffsets.SCPlaytimePacket, 5)
        {
            _playTime = playTime;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_playTime);
            return stream;
        }
    }
}
