using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// sbyte target
/// </remarks>
public class CSAntibotTransferWorldPacket() : GamePacket(CSOffsets.CSAntibotTransferWorldPacket, 1)
{
    public sbyte Target { get; private set; }

    public override void Read(PacketStream stream)
    {
        Target = stream.ReadSByte();
    }
}
