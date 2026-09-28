using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Items.Templates;

public class ItemTemplate
{
    public virtual Type ClassType => typeof(Item);

    public uint Id { get; set; }
    /// <summary>
    /// Original Korean name is stored here, use LocalizationManager to get the names for other languages
    /// </summary>
    public string Name { get; set; }
    public int CategoryId { get; set; }
    public int Level { get; set; }
    public int Price { get; set; }
    public int Refund { get; set; }
    public ItemBindType BindType { get; set; }
    public int PickupLimit { get; set; }
    public int MaxCount { get; set; }
    public bool Sellable { get; set; }
    public uint UseSkillId { get; set; }
    public bool UseSkillAsReagent { get; set; }
    public ItemImplEnum ImplId { get; set; }
    public uint BuffId { get; set; }
    public bool Gradable { get; set; }
    public bool LootMulti { get; set; }
    public uint LootQuestId { get; set; }
    public int HonorPrice { get; set; }
    public int ExpAbsLifetime { get; set; }
    public int ExpOnlineLifetime { get; set; }
    public DateTime ExpDate { get; set; }
    /// <summary>
    /// Lowest guild (expedition) level allowed to use this item, from <c>items.expedition_level</c>.
    /// 0 means the item is not gated behind a guild level.
    /// </summary>
    public uint ExpeditionLevel { get; set; }

    /// <summary>
    /// Day of week this item's life runs out on, as an <c>enum_day_of_weeks.id</c>: 1 is Sunday
    /// through 7 Saturday, and 8 is the row's own "no day" value. <c>ExpDayOfWeekMin</c> names the
    /// minute inside that day, counted from midnight. Both are needed: the shipped
    /// <c>검은 가시 열쇠</c> rows are the only ones that use a day other than Thursday, and they come
    /// in two variants that differ only in the minute.
    /// </summary>
    public int ExpDayOfWeekId { get; set; }

    /// <summary>Minutes past midnight on <see cref="ExpDayOfWeekId"/> the item's life runs out. 0 with a real day means midnight.</summary>
    public int ExpDayOfWeekMin { get; set; }

    /// <summary>
    /// The instant a periodic item's first period is measured from, from <c>items.period_base_date</c>.
    /// <see cref="DateTime.MinValue"/> when the row names none. It is not itself an expiry: the
    /// shipped rows all carry an <c>exp_date</c> as well, and the base date is what makes the gap
    /// between the two a whole number of the item's own period.
    /// </summary>
    public DateTime PeriodBaseDate { get; set; }

    /// <summary>
    /// <c>items.one_time_sale</c>: the item may be handed to a vendor once, after which the daily
    /// sale reset makes it sellable again.
    /// </summary>
    public bool OneTimeSale { get; set; }

    /// <summary>
    /// <c>items.limited_sale_count</c>: how many times the item may be handed to a vendor before the
    /// daily sale reset. 0 means unlimited. This is the wider of the two sale columns - 119 shipped
    /// rows set it without also setting <see cref="OneTimeSale"/>, which is the one-time sale
    /// expressed as a limit of 1.
    /// </summary>
    public int LimitedSaleCount { get; set; }

    /// <summary>
    /// <c>items.auction_only</c>: the item may not be sold to a vendor at all and has to go through
    /// the auction house instead. It still sells normally through the auction house.
    /// </summary>
    public bool AuctionOnly { get; set; }

    /// <summary>
    /// <c>items.auto_loot</c>: the item is handed to the looting character straight away instead of
    /// being listed in the loot window for a roll.
    /// </summary>
    public bool AutoLoot { get; set; }

    /// <summary>
    /// <c>items.proc_lifetime</c>: how many times the procs bound to this item may fire before the
    /// item's procs are spent. 0, which is every row but two, means the procs never run out.
    /// </summary>
    public int ProcLifetime { get; set; }

    /// <summary>
    /// <c>items.proc_recharge_restrict_item_id</c>: the item that puts a spent proc back. 0 means the
    /// procs are not rechargeable.
    /// </summary>
    public uint ProcRechargeRestrictItemId { get; set; }

    public int LevelRequirement { get; set; }
    public int AuctionCategoryA { get; set; }
    public int AuctionCategoryB { get; set; }
    public int AuctionCategoryC { get; set; }
    public int LevelLimit { get; set; }
    public int FixedGrade { get; set; }
    public bool Disenchantable { get; set; }
    public int LivingPointPrice { get; set; }
    public byte CharGender { get; set; }
    /// <summary>
    /// Highest <c>enchant_scale_ratios</c> row this item can be tempered to (items.max_enchant_scale_id).
    /// 0 means the item is not temperable.
    /// </summary>
    public byte MaxEnchantScaleId { get; set; }

    /// <summary>
    /// Highest <c>item_grades.id</c> this item may be regraded to (items.max_enchantable_grade).
    /// -1, the value most items carry, means no ceiling beyond the top of the grade table.
    /// </summary>
    public int MaxEnchantableGrade { get; set; } = -1;
    public uint SpecialtyZoneId { get; set; }
    // Defaults to the house commission rate until the template is loaded from items.
    public AuctionSettings AuctionSettings { get; set; } = new(0, 0, 0, 0, true);

    // Helpers
    public string searchString { get; set; }

    /*, 0, true*/
}
