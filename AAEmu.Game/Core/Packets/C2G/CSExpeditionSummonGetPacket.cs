using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// uint count, string name
/// </remarks>
public class CSExpeditionSummonGetPacket() : GamePacket(CSOffsets.CSExpeditionSummonGetPacket, 1)
{
    public uint Count { get; private set; }
    public string Name { get; private set; }

    public override void Read(PacketStream stream)
    {
        Count = stream.ReadUInt32();
        Name = stream.ReadString();
    }
}
