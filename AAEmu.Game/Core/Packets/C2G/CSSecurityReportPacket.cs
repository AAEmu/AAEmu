using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSecurityReportPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

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
            Logger.Info("CSSecurityReportPacket, Msg: {0}", str);
        }
        else
        {
            Logger.Info("CSSecurityReportPacket");
        }
    }
}
