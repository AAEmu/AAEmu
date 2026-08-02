using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// bool result, string name
/// </remarks>
public class CSExpeditionSummonReplyPacket() : GamePacket(CSOffsets.CSExpeditionSummonReplyPacket, 1)
{
    public bool Result { get; private set; }
    public string Name { get; private set; }

    public override void Read(PacketStream stream)
    {
        Result = stream.ReadBoolean();
        Name = stream.ReadString();
    }
}
