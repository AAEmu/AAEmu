using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// int type, string notice
/// </remarks>
public class CSExpeditionNoticeUpatePacket() : GamePacket(CSOffsets.CSExpeditionNoticeUpatePacket, 1)
{
    public int Type { get; private set; }
    public string Notice { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadInt32();
        Notice = stream.ReadString();
    }
}
