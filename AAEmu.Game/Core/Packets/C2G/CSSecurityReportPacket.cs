using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSecurityReportPacket : GamePacket
    {
        public CSSecurityReportPacket() : base(CSOffsets.CSSecurityReportPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var srType = stream.ReadByte();
            stream.ReadUInt32();
            stream.ReadUInt64();
            if (srType == 1)
            {
                var str = stream.ReadString();
                _log.Info("CSSecurityReportPacket, Msg: {0}", str);
            }
            else
            {
                _log.Info("CSSecurityReportPacket");
            }
        }
    }
}
