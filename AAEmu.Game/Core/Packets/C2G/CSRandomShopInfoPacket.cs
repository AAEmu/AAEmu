using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Merchant;

using NLog;

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
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

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

        try
        {
            var window = RandomMerchantManager.Instance.GetWindow(character.Id, packId, DateTime.UtcNow);
            Connection.SendPacket(new SCRandomShopInfoPacket(
                0,
                TypeValue < 0 ? 0u : (uint)TypeValue,
                packId,
                (byte)Math.Min(window.FreeUsed, byte.MaxValue),
                (byte)Math.Min(window.ChargeUsed, byte.MaxValue),
                character.Id,
                window.RolledAt,
                window.Offers));
        }
        catch (RandomMerchantContentException ex)
        {
            Logger.Error(ex, "Random shop info refused for character {0}, pack {1}", character.Id, packId);
        }
    }
}
