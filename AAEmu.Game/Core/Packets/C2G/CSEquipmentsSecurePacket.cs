using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEquipmentsSecurePacket : GamePacket
    {
        public CSEquipmentsSecurePacket() : base(CSOffsets.CSEquipmentsSecurePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Warn("EquipmentsSecure");
        }
    }
}
