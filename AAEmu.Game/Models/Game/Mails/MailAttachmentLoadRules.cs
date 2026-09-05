using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Mails;

/// <summary>
/// A mail row stores attachment item ids. After a claim those items live in the
/// bag, but a missed save can leave the old ids on the row. Reloading them
/// would put the same item back on the letter.
/// </summary>
public static class MailAttachmentLoadRules
{
    public static bool CanReload(Item item) =>
        item is { Count: > 0, SlotType: SlotType.Mail };
}
