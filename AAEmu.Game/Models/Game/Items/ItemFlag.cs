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
    /// Item was disabled by a failed regrade and cannot be used until restored.
    /// Wire-safe: the flags byte is zero-padded on the SC unit/item stream, so the new bit
    /// is additive for clients that do not know it.
    /// </summary>
    Disabled = 0x40
}
