using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// uint count, ulong type
/// </remarks>
public class CSExpeditionApplicantAcceptPacket() : GamePacket(CSOffsets.CSExpeditionApplicantAcceptPacket, 1)
{
    public uint Count { get; private set; }
    public ulong Type { get; private set; }

    public override void Read(PacketStream stream)
    {
        Count = stream.ReadUInt32();
        Type = stream.ReadUInt64();
    }
}
