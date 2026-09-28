using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Models.Game.Items.Templates;

/// <summary>
/// Projects one <c>items</c> row onto an <see cref="ItemTemplate"/>.
/// <para>
/// This is the only place that names the columns of the item template table, so a column that ships
/// content but is not read anywhere shows up here as a missing field rather than as a rule that
/// silently does nothing. Every read goes through <see cref="SQLiteWrapperReader"/> and its
/// <c>GetOrdinal</c>, which throws on a column the table does not have: a rename or a missing
/// column stops the load instead of quietly reading the wrong field.
/// </para>
/// <para>
/// The text booleans are read strictly. The shipped table stores them as <c>'t'</c>/<c>'f'</c> text
/// and the generic reader maps anything that is not one of the two to false, which would turn a
/// typo in the content into "not limited", so an unrecognised value throws here instead.
/// </para>
/// </summary>
public static class ItemTemplateRowLoader
{
    /// <summary>
    /// Reads the row the reader is positioned on into <paramref name="template"/>, replacing every
    /// field this projection owns. The caller decides which template instance to hand in, because a
    /// template of a narrower class (a bag, a sheet music, a craft order sheet) was already created
    /// and registered by its own table before this row was reached.
    /// </summary>
    /// <param name="reader">A reader positioned on an <c>items</c> row.</param>
    /// <param name="template">The template instance to fill in.</param>
    /// <returns>The same instance, filled in.</returns>
    /// <exception cref="InvalidDataException">A text boolean holds something other than 't' or 'f'.</exception>
    public static ItemTemplate Apply(SQLiteWrapperReader reader, ItemTemplate template)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(template);

        template.Id = reader.GetUInt32("id");
        template.Name = reader.IsDBNull("name") ? "" : reader.GetString("name");
        template.CategoryId = reader.GetInt32("category_id");
        template.Level = reader.GetInt32("level");
        // 10.0.2.13: price/refund columns removed from items
        template.BindType = (ItemBindType)reader.GetUInt32("bind_id");
        template.PickupLimit = reader.GetInt32("pickup_limit");
        template.MaxCount = reader.GetInt32("max_stack_size");
        template.Sellable = ReadFlag(reader, "sellable");
        template.UseSkillId = reader.GetUInt32("use_skill_id");
        template.UseSkillAsReagent = ReadFlag(reader, "use_skill_as_reagent");
        template.ImplId = (ItemImplEnum)reader.GetInt32("impl_id");
        template.BuffId = reader.GetUInt32("buff_id");
        template.Gradable = ReadFlag(reader, "gradable");
        template.LootMulti = ReadFlag(reader, "loot_multi");
        template.LootQuestId = reader.GetUInt32("loot_quest_id");
        // 10.0.2.13: honor_price column removed from items
        template.ExpAbsLifetime = reader.GetInt32("exp_abs_lifetime");
        template.ExpOnlineLifetime = reader.GetInt32("exp_online_lifetime");
        template.ExpDate = ReadDateTime(reader, "exp_date");
        // Lowest guild (expedition) level allowed to use this item. 0 means no gate; only a small
        // minority of the catalogue carries one.
        template.ExpeditionLevel = reader.GetUInt32("expedition_level");
        template.ExpDayOfWeekId = reader.GetInt32("exp_day_of_week_id");
        template.ExpDayOfWeekMin = reader.GetInt32("exp_day_of_week_min");
        template.PeriodBaseDate = ReadDateTime(reader, "period_base_date");
        template.OneTimeSale = ReadFlag(reader, "one_time_sale");
        template.LimitedSaleCount = reader.GetInt32("limited_sale_count");
        template.AuctionOnly = ReadFlag(reader, "auction_only");
        template.AutoLoot = ReadFlag(reader, "auto_loot");
        template.ProcLifetime = reader.GetInt32("proc_lifetime");
        template.ProcRechargeRestrictItemId = reader.GetUInt32("proc_recharge_restrict_item_id");
        template.SpecialtyZoneId = !reader.IsDBNull("specialty_zone_id") ? reader.GetUInt32("specialty_zone_id") : 0;
        template.LevelRequirement = reader.GetInt32("level_requirement");
        template.AuctionCategoryA = reader.IsDBNull("auction_a_category_id") ? 0 : reader.GetInt32("auction_a_category_id");
        template.AuctionCategoryB = reader.IsDBNull("auction_b_category_id") ? 0 : reader.GetInt32("auction_b_category_id");
        template.AuctionCategoryC = reader.IsDBNull("auction_c_category_id") ? 0 : reader.GetInt32("auction_c_category_id");
        template.LevelLimit = reader.GetInt32("level_limit");
        template.FixedGrade = reader.GetInt32("fixed_grade");
        // 10.0.2.13: -1 means uncapped; nullable in some rows
        template.MaxEnchantableGrade = reader.IsDBNull("max_enchantable_grade") ? -1 : reader.GetInt32("max_enchantable_grade");
        template.Disenchantable = ReadFlag(reader, "disenchantable");
        // 10.0.2.13: living_point_price column removed from items
        template.CharGender = reader.GetByte("char_gender_id");
        // Highest enchant_scale_ratios row this item may be tempered to. 0 means the
        // item cannot be tempered at all, which is the case for all but ~6.5k items.
        template.MaxEnchantScaleId = reader.GetByte("max_enchant_scale_id", 0);
        // Regrade ceiling. -1 on ~44.7k items (no ceiling); the ~6.3k that carry one
        // are capped at grade 7, which is what greys the scroll slot in the client.
        template.MaxEnchantableGrade = reader.GetInt32("max_enchantable_grade", -1);

        template.AuctionSettings = new AuctionSettings(
            template.AuctionCategoryA,
            template.AuctionCategoryB,
            template.AuctionCategoryC,
            // Present in the 10.0.2.13 schema; these were left commented out from a build that
            // predated them. auction_charge_default is a boolean, and auction_charge is the
            // per-item commission in basis points used when it is false.
            reader.GetInt32("auction_charge"),
            ReadFlag(reader, "auction_charge_default")
        );

        return template;
    }

    /// <summary>
    /// Reads one of the table's <c>'t'</c>/<c>'f'</c> text booleans. NULL is false, which is what the
    /// nullable columns of the table mean; anything else is content this build does not understand
    /// and throws rather than being folded into false.
    /// </summary>
    private static bool ReadFlag(SQLiteWrapperReader reader, string column)
    {
        var value = reader.GetValue(column);
        return value switch
        {
            DBNull => false,
            bool b => b,
            string { Length: 0 } => false,
            string s when s == "t" => true,
            string s when s == "f" => false,
            byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToInt64(value) != 0,
            _ => throw new InvalidDataException(
                $"items.{column} holds {value.GetType().Name} '{value}', which is neither 't' nor 'f'")
        };
    }

    /// <summary>NULL - the overwhelmingly common case for the date columns - is "no date", not the epoch.</summary>
    private static DateTime ReadDateTime(SQLiteWrapperReader reader, string column) =>
        reader.IsDBNull(column) ? DateTime.MinValue : reader.GetDateTime(column);
}
