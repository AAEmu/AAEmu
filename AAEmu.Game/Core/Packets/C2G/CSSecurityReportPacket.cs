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
        switch (srType)
        {
            case 1:
                {
                    stream.ReadUInt32();
                    stream.ReadUInt64();
                    var str = stream.ReadString();
                    Logger.Info($"CSSecurityReportPacket, Msg: {str}");
                    break;
                }
            case 2:
                {
                    var value2 = stream.ReadUInt32();
                    Logger.Info($"CSSecurityReportPacket, value2: {value2}");
                    break;
                }
            case 3:
                {
                    var value3 = stream.ReadUInt16();
                    Logger.Info($"CSSecurityReportPacket, Msg: {value3}");
                    break;
                }
            default:
                Logger.Info("CSSecurityReportPacket");
                break;
        }
    }
}
