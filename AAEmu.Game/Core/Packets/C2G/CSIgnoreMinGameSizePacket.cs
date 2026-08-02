using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// sbyte ignoreMinSize
/// </remarks>
public class CSIgnoreMinGameSizePacket() : GamePacket(CSOffsets.CSIgnoreMinGameSizePacket, 1)
{
    public sbyte IgnoreMinSize { get; private set; }

    public override void Read(PacketStream stream)
    {
        IgnoreMinSize = stream.ReadSByte();
    }
}
