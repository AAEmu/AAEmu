using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Regrade (grade enchant) resolution rules.
///
/// The <c>item_enchant_ratios</c> columns are laid out as consecutive windows over one roll
/// (0..9999), starting at the roll origin and clamped at MaxRatio:
///
///   [ success [great-success top slice] | break | disable | downgrade | ... remainder = fail ]
///
///   roll &lt; S'                    -> Success  (GreatSuccess if lucky AND roll in the top G' slice)
///   else roll &lt; S'+B'            -> Break
///   else roll &lt; S'+B'+X          -> Disable
///   else roll &lt; S'+B'+X+D'       -> Downgrade to an absolute grade in [DowngradeMin, DowngradeMax]
///   else                          -> Fail
///
/// This is why shipped rows may sum past 10000 (the tail window clamps): crafted groups
/// disable instead of shattering at high grades, drop groups break instead. Charm support
/// widens/shrinks each ratio additively plus a percentage of base, clamped to [0, MaxRatio].
/// </summary>
public static class ItemGradeEnchantRules
{
    public const int MaxRatio = 10000;

    /// <summary>Grade-order steps applied on a great success.</summary>
    public const int GreatSuccessGradeSteps = 2;

    /// <summary>Highest defined grade order; assigned by the data loader at boot.</summary>
    public static int MaxGradeOrder { get; set; }

    /// <summary>Charm support bonuses applied on top of the base ratios.</summary>
    public readonly struct CharmAdjustment
    {
        public CharmAdjustment(int addSuccessRatio, int addSuccessMul, int addGreatSuccessRatio,
            int addGreatSuccessMul, int addBreakRatio, int addBreakMul, int addDowngradeRatio,
            int addDowngradeMul)
        {
            AddSuccessRatio = addSuccessRatio;
            AddSuccessMul = addSuccessMul;
            AddGreatSuccessRatio = addGreatSuccessRatio;
            AddGreatSuccessMul = addGreatSuccessMul;
            AddBreakRatio = addBreakRatio;
            AddBreakMul = addBreakMul;
            AddDowngradeRatio = addDowngradeRatio;
            AddDowngradeMul = addDowngradeMul;
        }

        public int AddSuccessRatio { get; }
        public int AddSuccessMul { get; }
        public int AddGreatSuccessRatio { get; }
        public int AddGreatSuccessMul { get; }
        public int AddBreakRatio { get; }
        public int AddBreakMul { get; }
        public int AddDowngradeRatio { get; }
        public int AddDowngradeMul { get; }
    }

    /// <summary>Additive ratio plus percentage-of-base bonus, clamped to the 0..MaxRatio range.</summary>
    public static int Adjust(int baseChance, int charmRatio, int charmMul)
    {
        var adjusted = baseChance + charmRatio + (int)(baseChance * (charmMul / 100.0));
        return Math.Clamp(adjusted, 0, MaxRatio);
    }

    /// <summary>
    /// Resolves one regrade attempt. <paramref name="gradeToOrder"/> converts an absolute
    /// grade id (downgrade target columns) into a grade order. Returns the outcome and the
    /// new grade ORDER (the caller converts it back into a grade id).
    /// </summary>
    public static (GradeEnchantResult Result, int NewGradeOrder) Resolve(
        ItemEnchantRatio ratio,
        GradeTemplate currentGrade,
        int roll,
        bool lucky,
        CharmAdjustment? charm,
        Func<int, int>? gradeToOrder)
    {
        var success = charm is { } c ? Adjust(ratio.SuccessRatio, c.AddSuccessRatio, c.AddSuccessMul) : ratio.SuccessRatio;
        var great = charm is { } cg ? Adjust(ratio.GreatSuccessRatio, cg.AddGreatSuccessRatio, cg.AddGreatSuccessMul) : ratio.GreatSuccessRatio;
        var brk = charm is { } cb ? Adjust(ratio.BreakRatio, cb.AddBreakRatio, cb.AddBreakMul) : ratio.BreakRatio;
        var down = charm is { } cd ? Adjust(ratio.DowngradeRatio, cd.AddDowngradeRatio, cd.AddDowngradeMul) : ratio.DowngradeRatio;
        var disable = ratio.DisableRatio;

        // Lay the columns out as consecutive windows starting at the roll origin.
        var cursor = 0;

        if (roll < (cursor += success))
        {
            // Great success is the top slice of the success window and needs a lucky scroll.
            if (lucky && great > 0 && roll >= cursor - Math.Min(great, success))
                return (GradeEnchantResult.GreatSuccess, GreatSuccessOrder(currentGrade));

            return (GradeEnchantResult.Success, currentGrade.GradeOrder + 1);
        }

        if (roll < (cursor += brk))
            return (GradeEnchantResult.Break, currentGrade.GradeOrder);

        if (roll < (cursor += disable))
            return (GradeEnchantResult.Disable, currentGrade.GradeOrder);

        if (roll < (cursor += down) && gradeToOrder != null)
        {
            // Absolute target grades; negative bounds mean downgrading is impossible.
            if (ratio.DowngradeMin < 0 || ratio.DowngradeMax < 0)
                return (GradeEnchantResult.Fail, currentGrade.GradeOrder);

            var minOrder = gradeToOrder(ratio.DowngradeMin);
            var maxOrder = gradeToOrder(ratio.DowngradeMax);
            if (maxOrder < minOrder)
                (minOrder, maxOrder) = (maxOrder, minOrder);

            var span = maxOrder - minOrder + 1;
            var targetOrder = minOrder + ((roll - cursor + down) % span);
            return (GradeEnchantResult.Downgrade, Math.Min(targetOrder, currentGrade.GradeOrder));
        }

        return (GradeEnchantResult.Fail, currentGrade.GradeOrder);
    }

    private static int GreatSuccessOrder(GradeTemplate currentGrade)
    {
        // MaxGradeOrder is assigned at boot; tests may leave it unset (no clamping then).
        var max = MaxGradeOrder;
        return max > currentGrade.GradeOrder
            ? Math.Min(currentGrade.GradeOrder + GreatSuccessGradeSteps, max)
            : currentGrade.GradeOrder + GreatSuccessGradeSteps;
    }
}
