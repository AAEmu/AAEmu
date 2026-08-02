using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// sbyte difficult, bool invalidCheck
/// </remarks>
public class CSSelectInstanceDifficultPacket() : GamePacket(CSOffsets.CSSelectInstanceDifficultPacket, 1)
{
    public sbyte Difficult { get; private set; }
    public bool InvalidCheck { get; private set; }

    public override void Read(PacketStream stream)
    {
        Difficult = stream.ReadSByte();
        InvalidCheck = stream.ReadBoolean();
    }
}
