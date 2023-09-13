using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEquipmentsUnsecurePacket : GamePacket
    {
        public CSEquipmentsUnsecurePacket() : base(CSOffsets.CSEquipmentsUnsecurePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Warn("EquipmentsUnsecure");
        }
    }
}
