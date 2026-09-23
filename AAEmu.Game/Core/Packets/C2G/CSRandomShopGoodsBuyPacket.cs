using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

using NLog;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Buys one offer from the viewer's random shop window.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// u8 shopType, bc npc (3 bytes), bc doodad (3 bytes), u32 type, bool useAApoint,
/// then the requested-offer container. The container rides a reflection serializer whose element
/// layout the corpus does not resolve (its field names never appear in the dump), so this packet
/// parses the five flat fields and refuses the buy loudly instead of guessing a layout: no offer
/// is claimed and no money moves until the element fields are decoded.
/// </remarks>
public class CSRandomShopGoodsBuyPacket() : GamePacket(CSOffsets.CSRandomShopGoodsBuyPacket, 1)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public byte ShopType { get; private set; }
    public uint NpcObjId { get; private set; }
    public uint DoodadObjId { get; private set; }
    public uint Type { get; private set; }
    public bool UseAaPoint { get; private set; }

    public override void Read(PacketStream stream)
    {
        ShopType = stream.ReadByte();
        NpcObjId = stream.ReadBc();
        DoodadObjId = stream.ReadBc();
        Type = stream.ReadUInt32();
        UseAaPoint = stream.ReadBoolean();

        if (Connection?.ActiveChar is { } character &&
            RandomShopMerchantRange.ResolvePackId(character, NpcObjId, DoodadObjId) == 0)
            return;

        Logger.Error(
            "Random shop buy refused: character {0}, npc obj {1}, pack type {2} - the requested-offer list layout is not decoded",
            Connection?.ActiveChar?.Id ?? 0u, NpcObjId, Type);
    }
}
