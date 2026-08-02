using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// </remarks>
public class CSRemoveAllDoodadFromCell() : GamePacket(CSOffsets.CSRemoveAllDoodadFromCell, 1)
{
    public uint CellX { get; private set; }
    public uint CellY { get; private set; }

    public override void Read(PacketStream stream)
    {
        CellX = stream.ReadUInt32();
        CellY = stream.ReadUInt32();
    }
}
