using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLootDiceSummaryPacket : GamePacket
    {
        private readonly ulong _iId;
        private readonly int _count;
        private readonly uint _id;
        private readonly sbyte _diceValue;

        public SCLootDiceSummaryPacket(ulong iId, int count, uint id, sbyte diceValue) : base(SCOffsets.SCLootDiceSummaryPacket,1)
        {
            _iId = iId;
            _count = count;
            _id = id;
            _diceValue = diceValue;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_iId);
            stream.Write(_count);
            stream.Write(_id);
            stream.Write(_diceValue);
            return stream;
        }
    }
}
