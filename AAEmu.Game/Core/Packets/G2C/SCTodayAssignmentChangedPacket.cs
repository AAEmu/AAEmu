using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTodayAssignmentChangedPacket : GamePacket
    {
        private readonly uint _type1;
        private readonly uint _type2;
        private readonly uint _type3;
        private readonly byte _status;
        private readonly bool _init;

        public SCTodayAssignmentChangedPacket(uint type1, uint type2, uint type3, byte status, bool init)
            : base(SCOffsets.SCTodayAssignmentChangedPacket, 5)
        {
            _type1 = type1;
            _type2 = type2;
            _type3 = type3;
            _status = status;
            _init = init;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_type1);
            stream.Write(_type2);
            stream.Write(_type3);
            stream.Write(_status);
            stream.Write(_init);
            return stream;
        }
    }
}
