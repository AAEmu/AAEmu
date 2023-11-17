using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.CashShop;

public class CashShopItem : PacketMarshaler
{
    /// <summary>
    /// ID for the visible shop entry
    /// </summary>
    public uint CashShopId { get; set; }
    /// <summary>
    /// Name to display
    /// </summary>
    public string CashName { get; set; }
    /// <summary>
    /// Main Tab Location
    /// </summary>
    public byte MainTab { get; set; }
    public byte SubTab { get; set; }
    public byte LevelMin { get; set; }
    public byte LevelMax { get; set; }
    public uint ItemTemplateId { get; set; }
    public byte IsSell { get; set; }
    /// <summary>
    /// Hides the entry? Does this even work?
    /// </summary>
    public byte IsHidden { get; set; }

    /// <summary>
    /// Used to limit sales by a amount per type
    /// </summary>
    public CashShopLimitType LimitType { get; set; }

    /// <summary>
    /// Amount to limit to for LimitType
    /// </summary>
    public ushort BuyLimitCount { get; set; }

    /// <summary>
    /// If restricted, the shop will not show the contents of the entry until it's unlocked
    /// </summary>
    public CashShopRestrictSaleType BuyRestrictType { get; set; }

    public uint BuyRestrictId { get; set; }

    public DateTime SDate { get; set; }
    public DateTime EDate { get; set; }
    public CashShopCurrencyType CurrencyType { get; set; }
    public uint Price { get; set; }
    public uint Remain { get; set; }
    public uint BonusType { get; set; }
    public uint BonusCount { get; set; }
    public CashShopCmdUiType CmdUi { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(CashShopId);
        stream.Write(CashName);
        stream.Write(MainTab);
        stream.Write(SubTab);
        stream.Write(LevelMin);
        stream.Write(LevelMax);
        stream.Write(ItemTemplateId);
        stream.Write(IsSell);
        stream.Write(IsHidden);
        stream.Write((byte)LimitType);
        stream.Write(BuyLimitCount);
        stream.Write((byte)BuyRestrictType);
        stream.Write(BuyRestrictId);
        stream.Write(SDate);
        stream.Write(EDate);
        stream.Write((byte)CurrencyType);
        stream.Write(Price);
        stream.Write(Remain);
        stream.Write(BonusType);
        stream.Write(BonusCount);
        stream.Write((byte)CmdUi);
        // stream.Write(0);
        // stream.Write(0); // In captures this is discount price
        return stream;
    }
}
