using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

/// <summary>
/// Content fixtures for the reopen-box suites: packs are built directly from row-shaped values
/// so every number the tests assert against is written by the test itself - the manager never
/// defaults a gameplay value.
/// </summary>
public static class ReopenBoxTestContent
{
    public static MerchantReopenPack Pack(
        uint id = 1,
        int freeCount = 2,
        int chargeCount = 3,
        ContentCurrencyType currency = ContentCurrencyType.Gold,
        int chargePoint = 100,
        uint chargeItemId = 0,
        int lifeTime = 60,
        bool usable = true,
        int groupCount = 2,
        int goodsPerGroup = 2)
    {
        var pack = new MerchantReopenPack
        {
            Id = id,
            FreeCount = freeCount,
            ChargeCount = chargeCount,
            Currency = currency,
            ChargePoint = chargePoint,
            ChargeItemId = chargeItemId,
            LifeTime = lifeTime,
            Usable = usable
        };

        uint goodId = (id + 1) * 1000;
        for (var g = 0; g < groupCount; g++)
        {
            var group = new MerchantReopenGroup { Id = (id + 1) * 100 + (uint)g, Weight = 1000 * (g + 1) };
            for (var i = 0; i < goodsPerGroup; i++)
            {
                group.Goods.Add(new MerchantReopenGood
                {
                    Id = ++goodId,
                    ItemId = 50_000 + goodId,
                    GradeId = (byte)i,
                    Count = i + 1,
                    Weight = 0 // shipped rows are all weight zero
                });
            }
            pack.Groups.Add(group);
        }

        return pack;
    }

    public static IReadOnlyDictionary<uint, MerchantReopenPack> Content(params MerchantReopenPack[] packs) =>
        packs.ToDictionary(pack => pack.Id);

    public static DateTime Moment { get; } = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
}
