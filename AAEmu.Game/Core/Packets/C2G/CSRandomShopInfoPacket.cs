using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
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
            // Rolls (or re-rolls on a new UTC day) and persists the viewer's window. The reply,
            // SCRandomShopInfoPacket, carries a shopDisplayInfo payload whose container layout the
            // corpus does not resolve, so no reply is written yet - the window is ready for the
            // refresh path and the reply is withheld rather than guessed.
            RandomMerchantManager.Instance.GetWindow(character.Id, packId, DateTime.UtcNow);
            Logger.Warn(
                "Random shop info: window ready for character {0}, pack {1}; SCRandomShopInfoPacket reply withheld (shopDisplayInfo layout not decoded)",
                character.Id, packId);
        }
        catch (RandomMerchantContentException ex)
        {
            Logger.Error(ex, "Random shop info refused for character {0}, pack {1}", character.Id, packId);
        }
    }
}
