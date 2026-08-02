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
public class CSRandomShopInfoPacket() : GamePacket(CSOffsets.CSRandomShopInfoPacket, 1)
{
    public sbyte Unnamed1 { get; private set; }
    public sbyte ShopType { get; private set; }
    public uint Bc { get; private set; }
    public uint Bc2 { get; private set; }
    public int TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        Unnamed1 = stream.ReadSByte();
        ShopType = stream.ReadSByte();
        Bc = stream.ReadBc();
        Bc2 = stream.ReadBc();
        TypeValue = stream.ReadInt32();
    }
}
