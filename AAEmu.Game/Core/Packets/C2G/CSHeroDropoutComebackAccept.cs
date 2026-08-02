using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// ulong type
/// </remarks>
public class CSHeroDropoutComebackAccept() : GamePacket(CSOffsets.CSHeroDropoutComebackAccept, 1)
{
    public ulong Type { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadUInt64();
    }
}
