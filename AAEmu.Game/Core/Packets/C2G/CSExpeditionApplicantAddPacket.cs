using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// int type, string memo
/// </remarks>
public class CSExpeditionApplicantAddPacket() : GamePacket(CSOffsets.CSExpeditionApplicantAddPacket, 1)
{
    public int Type { get; private set; }
    public string Memo { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadInt32();
        Memo = stream.ReadString();
    }
}
