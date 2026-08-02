using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// string saveTitle
/// </remarks>
public class CSContentRosterSavePacket() : GamePacket(CSOffsets.CSContentRosterSavePacket, 1)
{
    public string SaveTitle { get; private set; }

    public override void Read(PacketStream stream)
    {
        SaveTitle = stream.ReadString();
    }
}
