using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// ulong type, string sender
/// </remarks>
public class CSReportSpamMailPacket() : GamePacket(CSOffsets.CSReportSpamMailPacket, 1)
{
    public ulong Type { get; private set; }
    public string Sender { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadUInt64();
        Sender = stream.ReadString();
    }
}
