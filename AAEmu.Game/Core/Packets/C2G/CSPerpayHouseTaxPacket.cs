using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSPerpayHouseTaxPacket : GamePacket
    {
        public CSPerpayHouseTaxPacket() : base(CSOffsets.CSPerpayHouseTaxPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();
            var ausp = stream.ReadBoolean();

            _log.Debug("CSPerpayHouseTaxPacket, Tl: {0}, ausp: {1}", tl, ausp);
        
            //TODO HousingManager.Instance.HouseTaxInfo(Connection, tl, ausp);
        }
    }
}
