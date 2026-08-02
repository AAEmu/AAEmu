using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// </remarks>
public class CSReportSpammerPacket() : GamePacket(CSOffsets.CSReportSpammerPacket, 1)
{
    public string TargetName { get; private set; }
    public string Message { get; private set; }
    public byte ChatType { get; private set; }

    public override void Read(PacketStream stream)
    {
        TargetName = stream.ReadString();
        Message = stream.ReadString();
        ChatType = stream.ReadByte();
    }
}
