using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// int type, sbyte kind, sbyte order, uint page, bool possible
/// </remarks>
public class CSSearchCraftOrderPacket() : GamePacket(CSOffsets.CSSearchCraftOrderPacket, 1)
{
    public int Type { get; private set; }
    public sbyte Kind { get; private set; }
    public sbyte Order { get; private set; }
    public uint Page { get; private set; }
    public bool Possible { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadInt32();
        Kind = stream.ReadSByte();
        Order = stream.ReadSByte();
        Page = stream.ReadUInt32();
        Possible = stream.ReadBoolean();
    }
}
