using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitLootingStatePacket :GamePacket
    {
        private readonly byte _bc;
        private readonly byte _looting;

        public SCUnitLootingStatePacket(byte bc, byte looting) : base(SCOffsets.SCUnitLootingStatePacket,1)
        {
            _bc = bc;
            _looting = looting;
        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_bc);
            stream.Write(_looting);
            return stream;
        }
    }
}
