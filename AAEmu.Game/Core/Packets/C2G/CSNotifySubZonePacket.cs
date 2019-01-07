using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSNotifySubZonePacket : GamePacket
    {
        public CSNotifySubZonePacket() : base(0x10f, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("Enter RegionId: {0} ", stream.ReadInt32());
        }
    }
}