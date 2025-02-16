using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAuctionMessagePacket : GamePacket
    {
        private readonly byte _msgType;
        private readonly uint _id;
        private readonly int _moneyAmount;

        public SCAuctionMessagePacket(byte msgType, uint id, int moneyAmount) : base(SCOffsets.SCAuctionMessagePacket, 5)
        {
            _msgType = msgType;
            _id = id;
            _moneyAmount = moneyAmount;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_msgType);
            stream.Write(_id);
            stream.Write(_moneyAmount);

            return stream;
        }
    }
}
