using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitOfflinePacket : GamePacket
    {
        private readonly uint _objId;
        private readonly bool _isOffline;

        public SCUnitOfflinePacket(uint objId, bool isOffline) : base(SCOffsets.SCUnitOfflinePacket, 5)
        {
            _objId = objId;
            _isOffline = isOffline;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_isOffline);
            return stream;
        }
    }
}
