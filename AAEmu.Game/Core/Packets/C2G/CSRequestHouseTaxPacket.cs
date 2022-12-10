using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestHouseTaxPacket : GamePacket
    {
        public CSRequestHouseTaxPacket() : base(CSOffsets.CSRequestHouseTaxPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();

            _log.Debug("RequestHouseTax, Tl: {0}", tl);
            
            HousingManager.Instance.HouseTaxInfo(Connection, tl);
        }
    }
}
