using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// bool refreshFree, sbyte shopType, bc bc (3 bytes), int type
/// </remarks>
public class CSRandomShopInfoRefreshPacket() : GamePacket(CSOffsets.CSRandomShopInfoRefreshPacket, 1)
{
    public bool RefreshFree { get; private set; }
    public sbyte ShopType { get; private set; }
    public uint Bc { get; private set; }
    public int Type { get; private set; }

    public override void Read(PacketStream stream)
    {
        RefreshFree = stream.ReadBoolean();
        ShopType = stream.ReadSByte();
        Bc = stream.ReadBc();
        Type = stream.ReadInt32();
    }
}
