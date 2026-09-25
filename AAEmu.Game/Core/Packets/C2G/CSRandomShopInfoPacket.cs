using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Opens the random shop window the NPC in front of the character runs.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSRandomShopInfoPacket() : GamePacket(CSOffsets.CSRandomShopInfoPacket, 1)
{
    public sbyte ShopType { get; private set; }
    public uint NpcObjId { get; private set; }
    public uint DoodadObjId { get; private set; }
    public int TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        ShopType = stream.ReadSByte();
        NpcObjId = stream.ReadBc();
        DoodadObjId = stream.ReadBc();
        TypeValue = stream.ReadInt32();
        HandleWindowOpen();
    }

    private void HandleWindowOpen()
    {
        if (Connection?.ActiveChar is not { } character)
            return;

        var packId = RandomShopMerchantRange.ResolvePackId(character, NpcObjId, DoodadObjId);
        if (packId == 0)
            return;

        RandomShopUiService.SendInfo(
            character,
            packId,
            TypeValue < 0 ? 0u : (uint)TypeValue);
    }
}
