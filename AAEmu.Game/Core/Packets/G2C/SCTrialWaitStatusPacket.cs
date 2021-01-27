using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;


namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTrialWaitStatusPacket : GamePacket
    {
        private readonly uint _order;
        private readonly uint _sentence;

        public SCTrialWaitStatusPacket(uint order, uint sentence) : base(SCOffsets.SCTrialWaitStatusPacket, 5)
        {
            _order = order;
            _sentence = sentence;
        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_order);
            stream.Write(_sentence);
            return stream;
        }
    }
}
