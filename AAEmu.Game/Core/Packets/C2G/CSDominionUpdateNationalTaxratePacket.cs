using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDominionUpdateNationalTaxratePacket : GamePacket
    {
        public CSDominionUpdateNationalTaxratePacket() : base(CSOffsets.CSDominionUpdateNationalTaxratePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSDominionUpdateNationalTaxratePacket");
        }
    }
}
