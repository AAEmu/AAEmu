using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO: the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSEnsembleMidiBinReadyPacket() : GamePacket(CSOffsets.CSEnsembleMidiBinReadyPacket, 1)
{
    public uint Bc { get; private set; }
    public uint Bc2 { get; private set; }
    public uint Size { get; private set; }
    public string Data { get; private set; }

    public override void Read(PacketStream stream)
    {
        Bc = stream.ReadBc();
        Bc2 = stream.ReadBc();
        Size = stream.ReadUInt32();
        Data = stream.ReadString();
    }
}
