using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCOnOffSnowPacket : GamePacket
    {
        private readonly bool _on;

        public SCOnOffSnowPacket(bool @on) : base(SCOffsets.SCOnOffSnowPacket, 5)
        {
            _on = @on;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_on);
            return stream;
        }
    }
}
