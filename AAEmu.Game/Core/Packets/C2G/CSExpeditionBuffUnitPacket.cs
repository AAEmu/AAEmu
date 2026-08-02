using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// bc bc (3 bytes)
/// </remarks>
public class CSExpeditionBuffUnitPacket() : GamePacket(CSOffsets.CSExpeditionBuffUnitPacket, 1)
{
    public uint Bc { get; private set; }

    public override void Read(PacketStream stream)
    {
        Bc = stream.ReadBc();
    }
}
