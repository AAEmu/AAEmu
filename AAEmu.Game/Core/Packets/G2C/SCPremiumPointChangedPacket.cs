using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCPremiumPointChangedPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly int _point;

        public SCPremiumPointChangedPacket(uint objId, int point) : base(SCOffsets.SCPremiumPointChangedPacket, 5)
        {
            _objId = objId;
            _point = point;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_point);
            return stream;
        }
    }
}
