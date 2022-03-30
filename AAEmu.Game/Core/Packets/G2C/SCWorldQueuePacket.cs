using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCWorldQueuePacket : GamePacket
    {
        private readonly byte _worldId;
        private readonly bool _isPremium;
        private readonly ushort _myTurn;
        private readonly ushort _normalLenght;
        private readonly ushort _premiumLenght;

        public SCWorldQueuePacket(byte worldId, bool isPremium, ushort myTurn, ushort normalLenght, ushort premiumLenght) : base(SCOffsets.SCWorldQueuePacket, 5)
        {
            _worldId = worldId;
            _isPremium = isPremium;
            _myTurn = myTurn;
            _normalLenght = normalLenght;
            _premiumLenght = premiumLenght;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_worldId);
            stream.Write(_isPremium);
            stream.Write(_myTurn);
            stream.Write(_normalLenght);
            stream.Write(_premiumLenght);
            return stream;
        }
    }
}
