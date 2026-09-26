using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Merchant;

using NLog;

namespace AAEmu.Game.Core.Packets;

/// <summary>
/// Shared server-to-client random-shop response path. The client opens the store itself and
/// listens for the existing SCRandomShopInfo event; no synthetic UI packet or guessed open
/// command is introduced.
/// </summary>
internal static class RandomShopUiService
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public static bool SendInfo(Character character, uint packId, uint type)
    {
        if (character?.ParentWorld == null || packId == 0)
            return false;

        try
        {
            var window = RandomMerchantManager.Instance.GetWindow(character.Id, packId, DateTime.UtcNow);
            character.SendPacket(BuildPacket(character, packId, type, window));
            return true;
        }
        catch (RandomMerchantContentException ex)
        {
            Logger.Error(ex, "Random shop window refused for character {0}, pack {1}", character.Id, packId);
            return false;
        }
    }

    internal static SCRandomShopInfoPacket BuildPacket(
        Character character,
        uint packId,
        uint type,
        RandomShopWindow window)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));
        if (window == null)
            throw new ArgumentNullException(nameof(window));
        if (packId == 0)
            throw new ArgumentOutOfRangeException(nameof(packId));
        if (window.PackId != packId)
            throw new ArgumentException("Random-shop window pack does not match the descriptor", nameof(window));

        return new SCRandomShopInfoPacket(
            0,
            type,
            packId,
            (byte)Math.Min(window.FreeUsed, byte.MaxValue),
            (byte)Math.Min(window.ChargeUsed, byte.MaxValue),
            character.Id,
            window.RolledAt,
            window.Offers);
    }
}
