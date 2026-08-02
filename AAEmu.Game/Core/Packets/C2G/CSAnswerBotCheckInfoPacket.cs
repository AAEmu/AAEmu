using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// </remarks>
public class CSAnswerBotCheckInfoPacket() : GamePacket(CSOffsets.CSAnswerBotCheckInfoPacket, 1)
{
    public string Answer { get; private set; }

    public override void Read(PacketStream stream)
    {
        Answer = stream.ReadString();
    }
}
