using System;

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
    AuctionWin = 0x20
}
