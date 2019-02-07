using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEquipmentsSecurePacket : GamePacket
    {
        public CSEquipmentsSecurePacket() : base(0x04a, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Warn("EquipmentsSecure");
        }
    }
}
