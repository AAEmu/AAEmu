using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMileageChangedPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly int _mileage;

        public SCMileageChangedPacket(uint objId, int mileage) : base(SCOffsets.SCMileageChangedPacket, 5)
        {
            _objId = objId;
            _mileage = mileage;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_mileage);
            return stream;
        }
    }
}
