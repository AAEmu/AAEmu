namespace AAEmu.Game.Models.Game.Items;

public class ItemGradeEnchantingSupport
{
    public uint Id { get; set; }
    public uint ItemId { get; set; }
    public int RequireGradeMax { get; set; }
    public int RequireGradeMin { get; set; }
    public int AddSuccessRatio { get; set; }
    public int AddSuccessMul { get; set; }
    public int AddGreatSuccessRatio { get; set; }
    public int AddGreatSuccessMul { get; set; }
    public int AddBreakRatio { get; set; }
    public int AddBreakMul { get; set; }
    public int AddDowngradeRatio { get; set; }
    public int AddDowngradeMul { get; set; }

    /// <summary>
    /// How the charm shifts the chance a failure locks the item out of enchanting. New alongside
    /// <c>item_enchant_ratios.grade_enchant_disable_ratio</c> in 10.0.2.13.
    /// </summary>
    public int AddDisableRatio { get; set; }

    public int AddDisableMul { get; set; }

    public int AddGreatSuccessGrade { get; set; }

    /// <summary>
    /// Tag the target gear must <em>not</em> carry. Zero = no blacklist.
    /// </summary>
    public uint RestrictItemTagId { get; set; }

    /// <summary>
    /// Tag the target gear must carry. Zero = no whitelist.
    /// </summary>
    public uint ExclusiveItemTagId { get; set; }

    /// <summary>
    /// Lowest tempering step (<c>enchant_scale_ratios.id</c>) the gear may sit at. Paired with
    /// <see cref="ReqScaleMaxId"/>; both equal to
    /// <see cref="GradeEnchantCharmRules.NoScaleRequirementId"/> means no scale gate.
    /// </summary>
    public int ReqScaleMinId { get; set; }

    /// <summary>Highest tempering step the gear may sit at. See <see cref="ReqScaleMinId"/>.</summary>
    public int ReqScaleMaxId { get; set; }
}
