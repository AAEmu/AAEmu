using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDominionUpdateTaxratePacket : GamePacket
    {
        public CSDominionUpdateTaxratePacket() : base(CSOffsets.CSDominionUpdateTaxratePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSDominionUpdateTaxratePacket");
        }
    }
}
