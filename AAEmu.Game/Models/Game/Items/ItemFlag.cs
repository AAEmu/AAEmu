namespace AAEmu.Game.Models.Game.Items;

[Flags]
public enum ItemFlag : byte
{
    None = 0x00,
    SoulBound = 0x01,
    HasUCC = 0x02,
    Secure = 0x04,
    Skinized = 0x08,
    Unpacked = 0x10,
    AuctionWin = 0x20,

    /// <summary>
    /// The item is locked out of further enchanting after a failed awakening or temper, until a
    /// restore item clears it (SCRestoreDisableEnchant).
    /// </summary>
    /// <remarks>
    /// Confirmed against the client: the tooltip field <c>isEnchantDisable</c> is built by testing
    /// bit 0x40 of the item's flags byte, at struct+0xd right behind id, templateId and grade
    /// (the client serializer and 0x794b2f). The same routine emits <c>securityState</c> from the
    /// neighbouring bits, which is what anchors the offset.
    /// </remarks>
    EnchantDisabled = 0x40
}
