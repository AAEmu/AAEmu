using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetPingPosPacket : GamePacket
    {
        public CSSetPingPosPacket() : base(0x086, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // TODO break memory sector
            _log.Warn("SetPingPos");
        }
    }
}
