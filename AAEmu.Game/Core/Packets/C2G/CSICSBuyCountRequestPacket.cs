using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// sbyte pk
/// </remarks>
public class CSICSBuyCountRequestPacket() : GamePacket(CSOffsets.CSICSBuyCountRequestPacket, 1)
{
    public sbyte Pk { get; private set; }

    public override void Read(PacketStream stream)
    {
        Pk = stream.ReadSByte();
    }
}
