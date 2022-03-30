using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCResponseCommonFarmListPacket : GamePacket
    {
        private readonly int _maxCount;
        
        public SCResponseCommonFarmListPacket(int maxCount) : base(SCOffsets.SCResponseCommonFarmListPacket, 5)
        {
            _maxCount = maxCount;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_maxCount);
            stream.Write(0); // count
            for (var i = 0; i < 0; i++) // TODO growing item
            {
                stream.Write(0u); // type(id)
                stream.Write(0u); // type(id)
                stream.Write(0u); // growing
                stream.Write(0u); // currentPhase, mb id
                stream.WritePosition(0f, 0f, 0f);
                stream.Write(0L); // plantTime
            }

            return stream;
        }
    }
}
